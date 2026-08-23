"""Re-embed existing chunks under a different embedding model.

    docker compose exec ingestion-service python -m app.reembed --model embed-local
    docker compose exec ingestion-service python -m app.reembed --model embed-local --dry-run

Switching EMBEDDING_MODEL does not migrate anything by itself. `embedding_model_tag` is
stored on every chunk row and filtered on at query time, so the flip alone makes the whole
corpus unreachable — search returns empty, not an error. This is what fills the new tag in.

No re-parsing and no re-chunking: chunk text, page numbers and chunk indices are already in
`document_chunks`, and none of them depend on the embedding model. Only the vectors do.
That is the difference between this and a re-ingest — minutes of embedding calls instead of
hours of Docling — and it holds precisely as long as the chunker has not changed. If it
HAS, this script is the wrong tool: it would carry the old chunk boundaries forward under a
new tag and quietly misrepresent them as current.

Rows are added, not replaced. The unique key is (document_id, chunk_index, embedding_model),
so the old tag's rows stay put and both models coexist — which makes the switch reversible
by flipping EMBEDDING_MODEL back, with no second migration. Run this BEFORE flipping the
config and there is no window where search is empty at all:

    1. python -m app.reembed --model embed-local      (search still served by the old tag)
    2. set EMBEDDING_MODEL=embed-local, restart        (new tag is already populated)

OpenSearch needs nothing: lexical.py deliberately keeps `embedding_model` out of its
mapping, because chunking is model-independent and the lexical index is about text.
"""

import argparse
import logging
import sys

from pgvector import Vector

from . import embeddings
from .config import settings
from .db import get_pool, init_db

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s [%(name)s] %(message)s")
log = logging.getLogger("reembed")

BATCH = 64  # matches embeddings._BATCH, so one read batch is one upstream request


def _tag(model: str) -> str:
    return f"{model}@{settings.embedding_dimensions}"


def run(model: str, source_tag: str | None = None, dry_run: bool = False) -> int:
    init_db()
    target_tag = _tag(model)

    with get_pool().connection() as conn:
        tags = [r[0] for r in conn.execute(
            "SELECT DISTINCT embedding_model FROM document_chunks").fetchall()]
    if source_tag is None:
        candidates = [t for t in tags if t != target_tag]
        if len(candidates) != 1:
            log.error("Cannot infer --source-tag; tags present: %s", tags or "(none)")
            return 1
        source_tag = candidates[0]
    if source_tag == target_tag:
        log.error("Source and target tag are both %s — nothing to do.", target_tag)
        return 1

    with get_pool().connection() as conn:
        todo = conn.execute(
            "SELECT count(*) FROM document_chunks WHERE embedding_model = %s", (source_tag,)
        ).fetchone()[0]
        done = conn.execute(
            "SELECT count(*) FROM document_chunks WHERE embedding_model = %s", (target_tag,)
        ).fetchone()[0]
    log.info("Re-embedding %s -> %s via model %r (%d source chunk(s), %d already at target).",
             source_tag, target_tag, model, todo, done)
    if dry_run:
        log.info("Dry run: nothing written.")
        return 0
    if not todo:
        log.error("No chunks carry the source tag %s.", source_tag)
        return 1

    # embeddings.embed() reads settings.embedding_model at call time, so overriding it here
    # is what lets this run while the service is still configured for — and still serving
    # search from — the old model. That is the whole reason the flip can be zero-downtime.
    previous, settings.embedding_model = settings.embedding_model, model

    written = 0
    try:
        with get_pool().connection() as read_conn:
            with read_conn.cursor(name="reembed") as cur:
                cur.itersize = BATCH
                cur.execute(
                    "SELECT document_id, deal_id, document_type, chunk_index, page_no, text"
                    " FROM document_chunks WHERE embedding_model = %s"
                    " ORDER BY document_id, chunk_index", (source_tag,))
                batch: list[tuple] = []
                while True:
                    rows = cur.fetchmany(BATCH)
                    if not rows:
                        break
                    batch = list(rows)
                    vectors = embeddings.embed([r[5] for r in batch])
                    if len(vectors) != len(batch):
                        raise RuntimeError(
                            f"embedder returned {len(vectors)} vectors for {len(batch)} chunks")
                    with get_pool().connection() as write_conn:
                        with write_conn.cursor() as wcur:
                            wcur.executemany(
                                "INSERT INTO document_chunks (document_id, deal_id,"
                                " document_type, chunk_index, page_no, text, embedding,"
                                " embedding_model) VALUES (%s, %s, %s, %s, %s, %s, %s, %s)"
                                " ON CONFLICT (document_id, chunk_index, embedding_model)"
                                " DO UPDATE SET embedding = EXCLUDED.embedding",
                                [(r[0], r[1], r[2], r[3], r[4], r[5], Vector(v), target_tag)
                                 for r, v in zip(batch, vectors)])
                    written += len(batch)
                    log.info("  %d/%d chunk(s) re-embedded.", written, todo)
    finally:
        settings.embedding_model = previous

    with get_pool().connection() as conn:
        final = conn.execute(
            "SELECT count(*) FROM document_chunks WHERE embedding_model = %s", (target_tag,)
        ).fetchone()[0]
    log.info("Done. %d written; %s now holds %d chunk(s) against %d at %s. %s",
             written, target_tag, final, todo, source_tag,
             "(in sync)" if final == todo else "(MISMATCH)")
    log.info("Now set EMBEDDING_MODEL=%s and restart ingestion-service.", model)
    return 0 if final == todo else 1


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Re-embed existing chunks under another model.")
    ap.add_argument("--model", required=True,
                    help="target LiteLLM route, e.g. embed-local (becomes <model>@<dims>)")
    ap.add_argument("--source-tag", help="tag to read from; inferred when exactly one other exists")
    ap.add_argument("--dry-run", action="store_true", help="report counts, write nothing")
    args = ap.parse_args(argv)
    return run(model=args.model, source_tag=args.source_tag, dry_run=args.dry_run)


if __name__ == "__main__":
    sys.exit(main())
