import logging
import time

import psycopg
from pgvector.psycopg import register_vector
from psycopg_pool import ConnectionPool

from .config import settings

log = logging.getLogger("db")

# Idempotent DDL applied at startup — mirrors the .NET services' migrate-on-boot
# (two tables don't warrant Alembic yet; revisit if the schema grows).
DDL = """
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS document_chunks (
    id              BIGSERIAL PRIMARY KEY,
    document_id     TEXT NOT NULL,          -- documents-service record id (no cross-service FK)
    deal_id         TEXT,
    document_type   TEXT,
    chunk_index     INTEGER NOT NULL,       -- preserves original document order
    page_no         INTEGER,                -- citation metadata (NULL for docx/xlsx)
    text            TEXT NOT NULL,
    embedding       vector(1024) NOT NULL,
    embedding_model TEXT NOT NULL,          -- e.g. 'embed-openai@1024'
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (document_id, chunk_index, embedding_model)
);
CREATE INDEX IF NOT EXISTS idx_chunks_embedding
    ON document_chunks USING hnsw (embedding vector_cosine_ops);
CREATE INDEX IF NOT EXISTS idx_chunks_document ON document_chunks (document_id);
CREATE INDEX IF NOT EXISTS idx_chunks_deal ON document_chunks (deal_id);

CREATE TABLE IF NOT EXISTS ingestion_runs (
    id              BIGSERIAL PRIMARY KEY,
    document_id     TEXT NOT NULL,
    deal_id         TEXT,
    status          TEXT NOT NULL,          -- running | succeeded | failed | skipped
    error           TEXT,
    chunk_count     INTEGER,
    embedding_model TEXT,
    started_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at     TIMESTAMPTZ
);
"""

pool: ConnectionPool | None = None


def _configure(conn) -> None:
    register_vector(conn)


def init_db() -> None:
    """Apply DDL, then open the pool, retrying while Postgres boots (fresh-volume race).

    DDL runs over a plain connection BEFORE the pool exists: the pool's configure
    hook registers the pgvector adapter, which needs the vector extension the DDL
    itself creates — opening the pool first would deadlock on that dependency.
    """
    global pool
    for attempt in range(1, 7):
        try:
            with psycopg.connect(settings.database_url, autocommit=True) as conn:
                conn.execute(DDL)
            pool = ConnectionPool(
                settings.database_url, min_size=1, max_size=8, open=True,
                configure=_configure,
            )
            log.info("Database ready — DDL applied.")
            return
        except Exception as ex:  # noqa: BLE001 — startup retry loop
            delay = 2 ** attempt
            log.warning("Database not ready (attempt %d): %s. Retrying in %ds.", attempt, ex, delay)
            time.sleep(delay)
    raise RuntimeError("Database unavailable after retries")


def get_pool() -> ConnectionPool:
    assert pool is not None, "init_db() has not run"
    return pool
