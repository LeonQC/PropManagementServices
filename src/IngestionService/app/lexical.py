"""BM25 half of hybrid retrieval: the OpenSearch chunk index.

pgvector stays the source of truth. This index only *proposes* candidates — every hit is
resolved back to a `document_chunks` row before it can reach a caller, so an OpenSearch
document with no matching row (a half-finished migration, a stale index) is dropped rather
than returned without a score. That one invariant is what lets the two stores drift
without producing wrong answers.

Deliberately absent from the mapping: `embedding_model`. Chunking is model-independent —
`chunk_index` for a document is the same whichever embedder runs — and BM25 has no
dependence on the embedding at all. Indexing per model would store one physical document
per model per chunk, which doubles every term's document frequency and so silently changes
BM25 ranking as a side effect of an unrelated config change. It would also make the
`EMBEDDING_MODEL: embed-openai -> embed-local` switch that docker-compose.yml invites
require a full lexical reindex on top of the re-embed.
"""

import json
import logging
import time
from pathlib import Path

from opensearchpy import OpenSearch, helpers

from .config import settings

log = logging.getLogger("lexical")

_MAPPING_PATH = Path(__file__).parent / "indexing" / "document_chunks_v1.json"

# Sentinel run id for backfilled documents. Any real ingestion_runs.id is >= 1, so a real
# ingest always supersedes a backfill through the normal sweep below.
BACKFILL_RUN_ID = 0

_client: OpenSearch | None = None


def client() -> OpenSearch:
    global _client
    if _client is None:
        _client = OpenSearch(
            hosts=[settings.opensearch_url],
            timeout=settings.opensearch_timeout,
            max_retries=2,
            retry_on_timeout=True,
        )
    return _client


# ---------- identity ----------

def doc_id(document_id: str, chunk_index: int) -> str:
    """The cross-store key.

    NOT `document_chunks.id`: that bigserial is re-minted on every re-ingest, because
    pipeline.ingest() replaces a document's rows with DELETE-then-INSERT. An OpenSearch
    document holding a stale bigserial would be unjoinable. `(document_id, chunk_index)`
    is stable across re-ingests and is already two thirds of the table's UNIQUE
    constraint, so the join back to pgvector is index-backed for free.
    """
    return f"{document_id}:{chunk_index}"


def parse_doc_id(raw: str) -> tuple[str, int]:
    """Inverse of doc_id(). rpartition, because document ids are opaque and may not be
    guaranteed ':'-free forever; the chunk index never contains one."""
    document_id, _, index = raw.rpartition(":")
    return document_id, int(index)


# ---------- index lifecycle ----------

def ensure_index(retries: int = 6) -> bool:
    """Create the index if absent, retrying while OpenSearch boots.

    Returns True if the index is ready. Never raises: `depends_on` in compose orders
    container *start*, not readiness, and OpenSearch needs ~20-30s before it accepts
    requests. More importantly, a dead OpenSearch must not take down dense retrieval —
    the service degrades, it does not fail.
    """
    for attempt in range(1, retries + 1):
        try:
            if client().indices.exists(index=settings.opensearch_index):
                log.info("Lexical index %s is ready.", settings.opensearch_index)
                return True
            body = json.loads(_MAPPING_PATH.read_text())
            client().indices.create(index=settings.opensearch_index, body=body)
            log.info("Created lexical index %s.", settings.opensearch_index)
            return True
        except Exception as ex:  # noqa: BLE001 — startup retry loop, mirrors db.init_db
            # Two containers racing to create the same index is benign.
            if "resource_already_exists_exception" in str(ex):
                return True
            delay = min(2 ** attempt, 30)
            log.warning("OpenSearch not ready (attempt %d): %s. Retrying in %ds.", attempt, ex, delay)
            time.sleep(delay)
    log.error("OpenSearch unavailable after %d attempts — continuing in dense-only mode.", retries)
    return False


def delete_index() -> None:
    client().indices.delete(index=settings.opensearch_index, ignore=[404])
    log.info("Deleted lexical index %s.", settings.opensearch_index)


def refresh() -> None:
    client().indices.refresh(index=settings.opensearch_index)


def ping() -> None:
    """Raises if OpenSearch is not reachable. Used by /health."""
    if not client().ping():
        raise RuntimeError(f"no response from {settings.opensearch_url}")


def count() -> int:
    return int(client().count(index=settings.opensearch_index)["count"])


# ---------- write path ----------

def _action(document_id: str, deal_id: str | None, document_type: str | None,
            chunk_index: int, page_no: int | None, text: str, run_id: int) -> dict:
    return {
        "_op_type": "index",  # overwrite, not create — see index_document()
        "_index": settings.opensearch_index,
        "_id": doc_id(document_id, chunk_index),
        "_source": {
            "documentId": document_id,
            "dealId": deal_id,
            "documentType": document_type,
            "chunkIndex": chunk_index,
            "pageNo": page_no,
            "text": text,
            "ingestionRunId": run_id,
        },
    }


def index_document(document_id: str, deal_id: str | None, document_type: str | None,
                   chunks, run_id: int) -> int:
    """Index one document's chunks, then sweep away anything the previous run left.

    Overwrite-then-sweep, in that order. Delete-by-query first would open a window where
    the document is absent from the lexical index while the bulk runs; overwriting
    deterministic ids has no such window.

    The sweep is `ingestionRunId < run_id` rather than `chunkIndex >= len(chunks)` because
    it covers two cases with one predicate: trailing chunks when a re-parse produces fewer
    of them, and a concurrent re-ingest race — an older run cannot delete a newer run's
    documents, which the chunk-index form gets wrong.

    Deliberately no `version_type=external`, unlike SearchService's OpenSearchDealIndex:
    that exists because Kafka entity snapshots genuinely arrive out of order, whereas this
    service's consumer (kafka_io.consume_loop) is a single thread processing documents
    sequentially, and the run-id sweep already gives last-writer-wins.
    """
    actions = [
        _action(document_id, deal_id, document_type, c.index, c.page_no, c.text, run_id)
        for c in chunks
    ]
    # refresh="wait_for": the caller publishes document.processed straight after this, and
    # an eval run kicked off on that signal must not read a stale index.
    helpers.bulk(client(), actions, refresh="wait_for")

    client().delete_by_query(
        index=settings.opensearch_index,
        body={"query": {"bool": {"filter": [
            {"term": {"documentId": document_id}},
            {"range": {"ingestionRunId": {"lt": run_id}}},
        ]}}},
        refresh=True,
        conflicts="proceed",
    )
    return len(actions)


def bulk_rows(rows, run_id: int = BACKFILL_RUN_ID, batch_size: int = 500) -> int:
    """Index an iterable of (document_id, deal_id, document_type, chunk_index, page_no,
    text) tuples straight from a pgvector cursor. Purely additive — no sweep — so it can
    never delete a document a real ingest just wrote."""
    actions = (_action(r[0], r[1], r[2], r[3], r[4], r[5], run_id) for r in rows)
    indexed = 0
    for ok, _ in helpers.streaming_bulk(client(), actions, chunk_size=batch_size):
        indexed += int(ok)
    return indexed


# ---------- read path ----------

def build_query(query: str, deal_id: str | None, document_id: str | None,
                size: int, min_should_match: str | None) -> dict:
    """BM25 candidates. The filters mirror the SQL WHERE clause exactly, including its
    NULL-means-no-filter semantics: a `term` clause is simply not appended when the value
    is None. (The semantics line up on the other end too — SQL's `deal_id = %s` excludes
    NULL rows, and a `term` filter excludes documents missing the field.)"""
    match: dict = {
        "multi_match": {
            "query": query,
            # One content field, two analyses. `most_fields` sums both signals rather than
            # taking the best, so an exact-token hit adds to the stemmed hit instead of
            # replacing it — which is the point of text.exact: APNs, "$67,834,000",
            # "6.69%" are precisely what the english stemmer blurs.
            "fields": ["text", "text.exact^1.5"],
            "type": "most_fields",
        }
    }
    # No minimum_should_match by default. The default OR is right for a natural-language
    # question: requiring a fraction of the terms drops the chunk that matches the one
    # rare discriminating token, which is exactly the APN case this is meant to fix. A
    # long low-quality BM25 tail is harmless — RRF reads ranks only, and the cosine floors
    # kill the tail downstream.
    if min_should_match:
        match["multi_match"]["minimum_should_match"] = min_should_match

    filters = []
    if deal_id is not None:
        filters.append({"term": {"dealId": deal_id}})
    if document_id is not None:
        filters.append({"term": {"documentId": document_id}})

    return {
        "size": size,
        # Everything the caller needs is the key; text and cosine come from pgvector, which
        # is the source of truth for both. _source stays enabled in the mapping so the
        # index is still inspectable from Dashboards — it just isn't shipped on the hot path.
        "_source": False,
        "query": {"bool": {"must": [match], "filter": filters}},
    }


def search(query: str, deal_id: str | None, document_id: str | None,
           size: int, min_should_match: str | None = None) -> list[tuple[tuple[str, int], float]]:
    """Ranked [(key, bm25_score)], best first. Raises on failure — callers decide whether
    to degrade."""
    body = build_query(query, deal_id, document_id, size, min_should_match)
    res = client().search(index=settings.opensearch_index, body=body)
    return [(parse_doc_id(h["_id"]), float(h["_score"])) for h in res["hits"]["hits"]]
