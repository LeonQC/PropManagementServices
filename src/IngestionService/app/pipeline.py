import logging
import os
from datetime import datetime, timezone

from pgvector import Vector

from . import chunking, embeddings, parsing, storage
from .config import settings
from .db import get_pool

log = logging.getLogger("pipeline")


def _now() -> datetime:
    return datetime.now(timezone.utc)


def document_id_from_pointer(storage_url: str | None) -> str | None:
    if not storage_url or not storage_url.startswith(settings.storage_url_prefix):
        return None
    doc_id = storage_url[len(settings.storage_url_prefix):].rstrip("/")
    return doc_id or None


def ingest(event: dict, publish) -> None:
    """Process one deal.document_uploaded event. Never raises — failures land
    in ingestion_runs and the log (poison-message isolation, like the .NET base).

    `publish(payload: dict, key: str)` sends document.processed.
    """
    deal_id = event.get("dealId")
    storage_url = event.get("storageUrl")
    document_id = document_id_from_pointer(storage_url)
    if document_id is None:
        log.info("Skipping deal document %s for deal %s: storageUrl is not a documents-service pointer.",
                 event.get("documentId"), deal_id)
        return

    pool = get_pool()
    with pool.connection() as conn:
        run_id = conn.execute(
            "INSERT INTO ingestion_runs (document_id, deal_id, status, embedding_model)"
            " VALUES (%s, %s, 'running', %s) RETURNING id",
            (document_id, deal_id, settings.embedding_model_tag),
        ).fetchone()[0]

    tmp = None
    try:
        key = storage.resolve_key(document_id)
        if key is None:
            raise FileNotFoundError(f"no blob under prefix {document_id}/")

        tmp = storage.download_to_temp(key)
        parsed = parsing.parse(tmp)
        storage.put_parsed(document_id, parsed.markdown)

        chunks = chunking.chunk(parsed)
        if not chunks:
            raise ValueError("document produced no text chunks")
        vectors = embeddings.embed([c.text for c in chunks])

        document_type = event.get("fileType")
        with pool.connection() as conn:
            with conn.transaction():
                # Replace-on-reingest keeps event redelivery/replay idempotent.
                conn.execute(
                    "DELETE FROM document_chunks WHERE document_id = %s AND embedding_model = %s",
                    (document_id, settings.embedding_model_tag),
                )
                with conn.cursor() as cur:
                    cur.executemany(
                        "INSERT INTO document_chunks (document_id, deal_id, document_type,"
                        " chunk_index, page_no, text, embedding, embedding_model)"
                        " VALUES (%s, %s, %s, %s, %s, %s, %s, %s)",
                        [
                            (document_id, deal_id, document_type, c.index, c.page_no,
                             c.text, Vector(v), settings.embedding_model_tag)
                            for c, v in zip(chunks, vectors)
                        ],
                    )
            conn.execute(
                "UPDATE ingestion_runs SET status='succeeded', chunk_count=%s, finished_at=%s WHERE id=%s",
                (len(chunks), _now(), run_id),
            )

        publish(
            {
                "dealId": deal_id,
                "documentId": document_id,
                "documentType": document_type,
                "chunkCount": len(chunks),
                "parsedArtifactUrl": f"/ingestion/v1/documents/{document_id}/parsed",
                "storageUrl": storage_url,
            },
            key=deal_id or document_id,
        )
        log.info("Ingested document %s (deal %s): %d chunks via %s.",
                 document_id, deal_id, len(chunks), settings.embedding_model_tag)

    except Exception as ex:  # noqa: BLE001 — recorded, never propagated
        log.error("Ingestion failed for document %s: %s", document_id, ex, exc_info=True)
        with pool.connection() as conn:
            conn.execute(
                "UPDATE ingestion_runs SET status='failed', error=%s, finished_at=%s WHERE id=%s",
                (str(ex)[:2000], _now(), run_id),
            )
    finally:
        if tmp is not None:
            try:
                os.unlink(tmp)
            except OSError:
                pass
