"""Index chunks that already exist in pgvector into OpenSearch.

    docker compose exec ingestion-service python -m app.backfill
    docker compose exec ingestion-service python -m app.backfill --recreate
    docker compose exec ingestion-service python -m app.backfill --repair

No re-parsing and no re-embedding: the text is read straight out of `document_chunks`, so
seeding a corpus that took hours of Docling time costs seconds here and nothing in
embedding spend.

It lives in the service package rather than under scripts/ because it needs psycopg3,
pgvector, opensearch-py, `Settings` and the index mapping — all of which the service
already has and scripts/ deliberately does not (eval_retrieval.py is stdlib-only, and
eval_ragas.py already pays for a second venv). Running it with `compose exec` also means
it reads exactly the config the service is running with, rather than a host-side
approximation of it.
"""

import argparse
import logging
import sys

from . import lexical
from .config import settings
from .db import get_pool, init_db

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s [%(name)s] %(message)s")
log = logging.getLogger("backfill")

BATCH = 500


def run(recreate: bool = False, repair: bool = False, dry_run: bool = False) -> int:
    init_db()  # idempotent DDL + pool, same as the service's startup

    if recreate and not dry_run:
        # The honest reset button. A mapping change does not reanalyse existing documents,
        # so anything that alters analysis has to be followed by a rebuild — and at ~1k
        # chunks that takes seconds, which makes "drop and rebuild" strictly better than
        # reasoning about what drifted.
        #
        # Guarded by dry_run: dropping the index is the most destructive thing this module
        # does, and a flag named --dry-run must not do it.
        lexical.delete_index()
    elif recreate:
        log.info("Dry run: would drop and recreate index %s.", settings.opensearch_index)
    if not lexical.ensure_index():
        log.error("OpenSearch is not reachable — nothing indexed.")
        return 1

    sql = ("SELECT document_id, deal_id, document_type, chunk_index, page_no, text"
           " FROM document_chunks WHERE embedding_model = %s")
    params: list = [settings.embedding_model_tag]
    if repair:
        # Only documents whose dual-write failed, or that predate the lexical index at all
        # (lexical_indexed IS NULL).
        sql += (" AND document_id IN (SELECT document_id FROM ingestion_runs"
                "  WHERE status = 'succeeded' AND lexical_indexed IS NOT TRUE)")
    sql += " ORDER BY document_id, chunk_index"

    with get_pool().connection() as conn:
        # Server-side cursor: overkill at ~1k rows, but it is the shape that stays correct
        # when the corpus is not small, and it costs one line.
        with conn.cursor(name="backfill") as cur:
            cur.itersize = BATCH
            cur.execute(sql, params)
            if dry_run:
                total = sum(1 for _ in cur)
                log.info("Dry run: %d chunk(s) would be indexed.", total)
                return 0
            # run_id=0 sentinel: purely additive, no delete-by-query sweep, so a backfill
            # racing a real ingest can never delete what that ingest just wrote.
            total = lexical.bulk_rows(cur, run_id=lexical.BACKFILL_RUN_ID, batch_size=BATCH)

    lexical.refresh()
    with get_pool().connection() as conn:
        pg_count = conn.execute(
            "SELECT count(*) FROM document_chunks WHERE embedding_model = %s",
            (settings.embedding_model_tag,)).fetchone()[0]
    os_count = lexical.count()
    log.info("Indexed %d chunk(s). pgvector=%d opensearch=%d %s",
             total, pg_count, os_count,
             "(in sync)" if pg_count == os_count else "(OUT OF SYNC)")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Backfill the OpenSearch chunk index from pgvector.")
    ap.add_argument("--recreate", action="store_true",
                    help="drop and recreate the index first (required after a mapping change)")
    ap.add_argument("--repair", action="store_true",
                    help="only documents whose lexical dual-write failed or never ran")
    ap.add_argument("--dry-run", action="store_true", help="count what would be indexed, write nothing")
    args = ap.parse_args(argv)
    if args.recreate and args.repair:
        ap.error("--recreate rebuilds everything, so --repair is meaningless with it")
    return run(recreate=args.recreate, repair=args.repair, dry_run=args.dry_run)


if __name__ == "__main__":
    sys.exit(main())
