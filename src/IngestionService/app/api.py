import json
import logging
import uuid
from datetime import datetime, timezone
import httpx
from fastapi import APIRouter, HTTPException, Response
from pgvector import Vector
from pydantic import BaseModel, Field

from . import embeddings, rerank, storage
from .auth import Claims
from .config import settings
from .db import get_pool

router = APIRouter()
log = logging.getLogger("api")


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

    candidateK: int | None = Field(default=None, ge=1, le=500)

    # Omitted means the service default (RERANK_ENABLED), so whether reranking runs is a
    # deployment decision while the eval harness can still A/B both arms against one
    # running stack.
    rerank: bool | None = None


# ---------- pgvector: the source of truth for text and for score ----------

_ROW_COLUMNS = "document_id, deal_id, chunk_index, page_no, text"


def _dense_rows(conn, qvec, deal_id, document_id, limit):
    """Top-`limit` chunks by cosine similarity, best first."""
    return conn.execute(
        f"""
        SELECT {_ROW_COLUMNS},
               1 - (embedding <=> %s) AS score
        FROM document_chunks
        WHERE embedding_model = %s
          AND (%s::text IS NULL OR deal_id = %s)
          AND (%s::text IS NULL OR document_id = %s)
        ORDER BY embedding <=> %s
        LIMIT %s
        """,
        (qvec, settings.embedding_model_tag, deal_id, deal_id,
         document_id, document_id, qvec, limit),
    ).fetchall()


def _key(row) -> rerank.Key:
    return (row[0], row[2])


@router.post("/ingestion/v1/search")
def search(req: SearchRequest, claims: dict = Claims):
    # The query must be embedded with the same model that embedded the chunks —
    # the reason retrieval lives in this service (architecture v1.1 §2.5).
    qvec = Vector(embeddings.embed_one(req.query))

    # Candidate depth is deliberately NOT derived from topK: a smaller topK must return a
    # prefix of a larger one, which is what lets the eval harness fetch each question once
    # and truncate. See Settings.candidate_k.
    candidate_k = req.candidateK or settings.candidate_k
    # `is None` rather than `or`: this is a boolean, so an explicit false from the caller
    # has to be able to override a service default of true.
    do_rerank = settings.rerank_enabled if req.rerank is None else req.rerank

    with get_pool().connection() as conn:
        rows = _dense_rows(conn, qvec, req.dealId, req.documentId,
                           max(candidate_k, req.topK))

    by_key = {_key(r): r for r in rows}
    cosine = {k: float(row[5]) for k, row in by_key.items()}
    # Note the absent [:req.topK]: truncation happens once, at the end, after reranking has
    # run. Truncating here would cap what the reranker can see at topK and make its depth
    # topK-dependent, breaking the prefix property the eval harness's fetch cache rests on.
    ranked = [_key(r) for r in rows]

    rerank_scores: dict[rerank.Key, float] = {}
    if do_rerank:
        # Fixed depth, never max(rerank_candidates, topK) — see Settings.rerank_candidates.
        pool = ranked[:settings.rerank_candidates]
        try:
            scored = rerank.score(req.query, [by_key[k][4] for k in pool])
            rerank_scores = dict(zip(pool, scored))
            # The tail past the rerank depth keeps its cosine order, so a topK deeper than
            # rerank_candidates still returns topK chunks rather than silently short ones.
            ranked = rerank.order(pool, rerank_scores, cosine) + ranked[len(pool):]
        except Exception as ex:  # noqa: BLE001 — degrade, but never silently
            # Cosine order is still a good ordering, so the question is answered — but the
            # response says which stage dropped out, and the eval harness refuses to score
            # a run whose `rerank` came back false after asking for true. An undeclared
            # fallback mid-A/B corrupts the result with no trace.
            log.error("Rerank failed (%s) — keeping the cosine ordering.", ex)
            do_rerank = False

    chunks = []
    for position, key in enumerate(ranked[:req.topK], start=1):
        row = by_key[key]
        chunks.append({
            "documentId": row[0],
            "dealId": row[1],
            "chunkIndex": row[2],
            "pageNo": row[3],
            "text": row[4],
            # Cosine similarity. The floors in ai-service are calibrated on this scale and
            # off-domain rejection depends on it — putting any other score here would look
            # like a harmless field change and would quietly break the ability to decline.
            "score": round(float(row[5]), 4),
            "rank": position,
            # Cross-encoder relevance, when a reranker ran. Diagnostic only: the ordering it
            # produced is already expressed by `rank`, and putting it anywhere near `score`
            # would break the cosine calibration.
            "rerankScore": round(rerank_scores[key], 6) if key in rerank_scores else None,
        })

    return envelope({
        "query": req.query,
        "embeddingModel": settings.embedding_model_tag,
        # The stage that ACTUALLY ran, not the one requested. A caller that asked for
        # reranking and got `false` back knows it is holding the cosine ordering.
        "rerank": do_rerank,
        "chunks": chunks,
    })


@router.get("/ingestion/v1/documents/{document_id}/parsed")
def parsed(document_id: str, claims: dict = Claims):
    md = storage.get_parsed(document_id)
    if md is None:
        raise HTTPException(status_code=404, detail="No parsed artifact for this document.")
    return Response(content=md, media_type="text/markdown; charset=utf-8")


@router.get("/health")
def health():
    checks: dict[str, str] = {}
    required = {"database", "litellm"}
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

    if settings.rerank_enabled:
        # Only probed when reranking is on. The container is profile-gated and absent from
        # a default `docker compose up`, so probing it unconditionally would report an
        # error on every healthy dev stack. Required once enabled, though: a stage that is
        # configured on and silently failing is worse than one that is off.
        try:
            rerank.ping()
            checks["rerank"] = "ok"
        except Exception as ex:  # noqa: BLE001
            checks["rerank"] = f"error: {ex}"
            required.add("rerank")

    status = 200 if all(checks.get(k) == "ok" for k in required) else 503
    return Response(content=json.dumps(envelope(checks)), media_type="application/json", status_code=status)
