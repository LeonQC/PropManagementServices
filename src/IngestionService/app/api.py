import json
import logging
import uuid
from datetime import datetime, timezone
from typing import Literal

import httpx
from fastapi import APIRouter, HTTPException, Response
from pgvector import Vector
from pydantic import BaseModel, Field

from . import embeddings, fusion, lexical, rerank, storage
from .auth import Claims
from .config import settings
from .db import get_pool

router = APIRouter()
log = logging.getLogger("api")

MODES = ("dense", "lexical", "hybrid")


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

    # Retrieval mode. Omitted means the service default (SEARCH_MODE), so the shipped
    # behaviour is a deployment decision while the eval harness can A/B all three against
    # one running stack.
    mode: Literal["dense", "lexical", "hybrid"] | None = None
    rrfK: int | None = Field(default=None, ge=1, le=1000)
    candidateK: int | None = Field(default=None, ge=1, le=500)

    # Reranking is deliberately NOT a fourth `mode`. `mode` selects which candidate
    # generators run AND doubles as the degradation channel (a lexical failure rewrites it
    # to "dense"), whereas this reorders whatever those generators produced. Folding it in
    # would mean encoding a product of degraded states in one string, and would turn the
    # three modes into six the moment reranking has to compose with each of them — which
    # the unscoped eval arm requires. Same separation of concerns that keeps scoring and
    # ordering apart everywhere else in this file.
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


def _rows_for_keys(conn, qvec, deal_id, document_id, keys):
    """Cosine scores for specific chunks — the back-fill for lexical hits that dense
    retrieval did not return.

    This is what keeps `score` on the calibrated cosine scale in every mode. A BM25 hit
    arrives with a BM25 score, which is meaningless to MinScore/RelativeFloor; looking the
    chunk up here gives it the same number the dense query would have given it.

    The deal/document predicates are re-applied deliberately. They are redundant when the
    two stores agree, and they are the thing that keeps a drifted lexical index from
    leaking another deal's chunk into an answer — cheap defence on a security-relevant
    boundary.
    """
    return conn.execute(
        f"""
        SELECT {_ROW_COLUMNS},
               1 - (embedding <=> %s) AS score
        FROM document_chunks
        WHERE embedding_model = %s
          AND (%s::text IS NULL OR deal_id = %s)
          AND (%s::text IS NULL OR document_id = %s)
          AND (document_id, chunk_index) IN (
                SELECT d, i FROM unnest(%s::text[], %s::int[]) AS t(d, i))
        """,
        (qvec, settings.embedding_model_tag, deal_id, deal_id, document_id, document_id,
         [k[0] for k in keys], [k[1] for k in keys]),
    ).fetchall()


def _key(row) -> fusion.Key:
    return (row[0], row[2])


@router.post("/ingestion/v1/search")
def search(req: SearchRequest, claims: dict = Claims):
    # The query must be embedded with the same model that embedded the chunks —
    # the reason retrieval lives in this service (architecture v1.1 §2.5).
    qvec = Vector(embeddings.embed_one(req.query))

    mode = req.mode or settings.search_mode
    if mode not in MODES:
        raise HTTPException(status_code=400, detail=f"mode must be one of {', '.join(MODES)}")
    rrf_k = req.rrfK or settings.rrf_k
    # Candidate depth is deliberately NOT derived from topK: a smaller topK must return a
    # prefix of a larger one, which is what lets the eval harness fetch each question once
    # and truncate. See Settings.candidate_k.
    candidate_k = req.candidateK or settings.candidate_k
    # `is None` rather than `or`: this is a boolean, so an explicit false from the caller
    # has to be able to override a service default of true. Omitted means "service
    # default", the same contract as `mode`.
    do_rerank = settings.rerank_enabled if req.rerank is None else req.rerank

    lex_hits: list[tuple[fusion.Key, float]] = []
    degraded = False
    if mode in ("lexical", "hybrid"):
        try:
            lex_hits = lexical.search(req.query, req.dealId, req.documentId,
                                      candidate_k, settings.lexical_min_should_match)
        except Exception as ex:  # noqa: BLE001 — degrade, but never silently
            # Falling back is right (dense alone still answers the question); hiding it is
            # not. An undeclared fallback in the middle of a 100-question A/B corrupts the
            # result with no trace, so it is reported in the response and the harness
            # refuses to score a run that contains one.
            log.error("Lexical search failed (%s) — degrading to dense.", ex)
            degraded, mode = True, "dense"

    with get_pool().connection() as conn:
        dense_rows = []
        if mode in ("dense", "hybrid"):
            dense_rows = _dense_rows(conn, qvec, req.dealId, req.documentId,
                                     max(candidate_k, req.topK))

        by_key = {_key(r): r for r in dense_rows}
        dense_order = [_key(r) for r in dense_rows]

        missing = [k for k, _ in lex_hits if k not in by_key]
        if missing:
            for row in _rows_for_keys(conn, qvec, req.dealId, req.documentId, missing):
                by_key[_key(row)] = row

    # pgvector is the source of truth: a lexical hit with no row behind it under the
    # current embedding model is, by definition, not retrievable. Dropping it handles a
    # half-migrated corpus without any extra bookkeeping — but count it, so "half
    # migrated" is visible rather than a quiet recall loss.
    lex_order = [k for k, _ in lex_hits if k in by_key]
    unresolved = len(lex_hits) - len(lex_order)
    if unresolved:
        log.warning("%d lexical hit(s) had no document_chunks row under %s — dropped. "
                    "Run `python -m app.backfill --recreate` if this persists.",
                    unresolved, settings.embedding_model_tag)

    lex_scores = dict(lex_hits)
    cosine = {k: float(row[5]) for k, row in by_key.items()}
    fused: dict[fusion.Key, float] = {}
    # Note the absent [:req.topK] in all three branches: truncation happens once, at the
    # end, after every reordering stage has run. Truncating here would cap what the
    # reranker can see at topK and make its depth topK-dependent, breaking the prefix
    # property the eval harness's fetch cache rests on.
    if mode == "dense":
        ranked = dense_order
    elif mode == "lexical":
        ranked = lex_order
    else:
        fused = fusion.rrf([dense_order, lex_order], rrf_k)
        ranked = fusion.order(fused, cosine)

    rerank_scores: dict[fusion.Key, float] = {}
    if do_rerank:
        # Fixed depth, never max(rerank_candidates, topK) — see Settings.rerank_candidates.
        pool = ranked[:settings.rerank_candidates]
        try:
            scored = rerank.score(req.query, [by_key[k][4] for k in pool])
            rerank_scores = dict(zip(pool, scored))
            # The tail past the rerank depth keeps its fused order, so a topK deeper than
            # rerank_candidates still returns topK chunks rather than silently short ones.
            ranked = rerank.order(pool, rerank_scores, cosine) + ranked[len(pool):]
        except Exception as ex:  # noqa: BLE001 — degrade, but never silently
            # Same contract as the lexical fallback above: the fused ordering is still a
            # good ordering, so the question is answered, but the response says so and the
            # eval harness refuses to score a run containing it. `mode` is deliberately
            # left alone here — it describes which retrievers ran, and they all ran; the
            # `rerank: false` field is what says which stage dropped out.
            log.error("Rerank failed (%s) — keeping the fused ordering.", ex)
            degraded, do_rerank = True, False

    ranked = ranked[:req.topK]

    chunks = []
    for position, key in enumerate(ranked, start=1):
        row = by_key[key]
        chunks.append({
            "documentId": row[0],
            "dealId": row[1],
            "chunkIndex": row[2],
            "pageNo": row[3],
            "text": row[4],
            # Always cosine, in every mode. The floors in ai-service are calibrated on this
            # scale and off-domain rejection depends on it — a fused score here would look
            # like a harmless field change and would quietly break the ability to decline.
            "score": round(float(row[5]), 4),
            "rank": position,
            "lexScore": round(lex_scores[key], 4) if key in lex_scores else None,
            "fusedScore": round(fused[key], 6) if fused else None,
            # Cross-encoder relevance, when a reranker ran. Diagnostic only, exactly like
            # the two above: the ordering it produced is already expressed by `rank`, and
            # putting it anywhere near `score` would break the cosine calibration.
            "rerankScore": round(rerank_scores[key], 6) if key in rerank_scores else None,
        })

    return envelope({
        "query": req.query,
        "embeddingModel": settings.embedding_model_tag,
        "mode": mode,
        "degraded": degraded,
        # The stage that ACTUALLY ran, not the one requested — same semantics as `mode`.
        # A caller that asked for reranking and got `false` back knows the fused ordering
        # is what it is holding.
        "rerank": do_rerank,
        "chunks": chunks,
    })


@router.get("/ingestion/v1/lexical/status")
def lexical_status(claims: dict = Claims):
    """Are the two stores in sync? The pre-flight before trusting any hybrid measurement."""
    with get_pool().connection() as conn:
        pg_chunks = conn.execute(
            "SELECT count(*) FROM document_chunks WHERE embedding_model = %s",
            (settings.embedding_model_tag,)).fetchone()[0]
    try:
        os_docs = lexical.count()
    except Exception as ex:  # noqa: BLE001
        raise HTTPException(status_code=503, detail=f"OpenSearch unavailable: {ex}") from ex
    return envelope({
        "index": settings.opensearch_index,
        "embeddingModel": settings.embedding_model_tag,
        "pgChunks": pg_chunks,
        "osDocs": os_docs,
        "inSync": pg_chunks == os_docs,
        "defaultMode": settings.search_mode,
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

    if settings.lexical_enabled or settings.search_mode != "dense":
        try:
            lexical.ping()
            checks["opensearch"] = "ok"
        except Exception as ex:  # noqa: BLE001
            # Advisory while the default mode is dense: retrieval still works without it,
            # and 503-ing a service that is serving every request correctly would be a lie
            # that takes down callers' health gates for a degraded-but-working dependency.
            checks["opensearch"] = f"error: {ex}"
            if settings.search_mode != "dense":
                required.add("opensearch")
            else:
                checks["opensearch"] += " (advisory: mode=dense)"

    if settings.rerank_enabled:
        # Only probed when reranking is on. The container is profile-gated and absent from
        # a default `docker compose up`, so probing it unconditionally would report an
        # error on every healthy dev stack. Required once enabled, for the same reason
        # OpenSearch is required once the mode needs it: a stage that is configured on and
        # silently failing is worse than one that is off.
        try:
            rerank.ping()
            checks["rerank"] = "ok"
        except Exception as ex:  # noqa: BLE001
            checks["rerank"] = f"error: {ex}"
            required.add("rerank")

    status = 200 if all(checks.get(k) == "ok" for k in required) else 503
    return Response(content=json.dumps(envelope(checks)), media_type="application/json", status_code=status)
