import json
import uuid
from datetime import datetime, timezone

import httpx
from fastapi import APIRouter, HTTPException, Response
from pgvector import Vector
from pydantic import BaseModel, Field

from . import embeddings, storage
from .auth import Claims
from .config import settings
from .db import get_pool

router = APIRouter()


def _meta() -> dict:
    return {"timestamp": datetime.now(timezone.utc).isoformat(), "requestId": uuid.uuid4().hex[:16]}


def envelope(data) -> dict:
    """House {data, meta} success envelope (architecture §5.1)."""
    return {"data": data, "meta": _meta()}


class SearchRequest(BaseModel):
    query: str = Field(min_length=1)
    dealId: str | None = None
    documentId: str | None = None
    topK: int = Field(default=5, ge=1, le=50)


@router.post("/ingestion/v1/search")
def search(req: SearchRequest, claims: dict = Claims):
    # The query must be embedded with the same model that embedded the chunks —
    # the reason retrieval lives in this service (architecture v1.1 §2.5).
    qvec = Vector(embeddings.embed_one(req.query))
    tag = settings.embedding_model_tag
    with get_pool().connection() as conn:
        rows = conn.execute(
            """
            SELECT document_id, deal_id, chunk_index, page_no, text,
                   1 - (embedding <=> %s) AS score
            FROM document_chunks
            WHERE embedding_model = %s
              AND (%s::text IS NULL OR deal_id = %s)
              AND (%s::text IS NULL OR document_id = %s)
            ORDER BY embedding <=> %s
            LIMIT %s
            """,
            (qvec, tag, req.dealId, req.dealId, req.documentId, req.documentId, qvec, req.topK),
        ).fetchall()
    chunks = [
        {"documentId": r[0], "dealId": r[1], "chunkIndex": r[2], "pageNo": r[3],
         "text": r[4], "score": round(float(r[5]), 4)}
        for r in rows
    ]
    return envelope({"query": req.query, "embeddingModel": tag, "chunks": chunks})


@router.get("/ingestion/v1/documents/{document_id}/parsed")
def parsed(document_id: str, claims: dict = Claims):
    md = storage.get_parsed(document_id)
    if md is None:
        raise HTTPException(status_code=404, detail="No parsed artifact for this document.")
    return Response(content=md, media_type="text/markdown; charset=utf-8")


@router.get("/health")
def health():
    checks: dict[str, str] = {}
    try:
        with get_pool().connection() as conn:
            conn.execute("SELECT 1")
        checks["database"] = "ok"
    except Exception as ex:  # noqa: BLE001
        checks["database"] = f"error: {ex}"
    try:
        httpx.get(f"{settings.litellm_base_url}/health/liveliness", timeout=3.0).raise_for_status()
        checks["litellm"] = "ok"
    except Exception as ex:  # noqa: BLE001
        checks["litellm"] = f"error: {ex}"
    status = 200 if all(v == "ok" for v in checks.values()) else 503
    return Response(content=json.dumps(envelope(checks)), media_type="application/json", status_code=status)
