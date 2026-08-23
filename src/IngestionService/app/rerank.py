"""Cross-encoder reranking: the third stage of hybrid retrieval.

Dense retrieval and BM25 both score a chunk *without ever looking at the query and the
chunk together* — cosine compares two independently-produced vectors, BM25 sums per-term
statistics. A cross-encoder reads the concatenated pair and emits one relevance score, so
it can tell "the occupancy figure the rent roll reports" from "a paragraph that happens to
be about the same building". That is the whole reason for the extra hop.

It reorders; it never retrieves. The candidate set is settled by the time this module is
called, which is what keeps the contract simple: reranking can change `rank`, and nothing
else. In particular it does NOT touch `score`, which stays cosine in every mode so that
ai-service's MinScore/RelativeFloor stay calibrated and off-domain questions still abstain
(see RetrievalOptions.cs and docs/retrieval-eval.md).

Routed through the LiteLLM proxy (`rerank-local` in litellm/config.yaml), the same way
embeddings.py reaches its model: which container actually serves the reranker is a config
decision, not a code one. The backing service is still the `tei-rerank` TEI container —
LiteLLM's HuggingFace rerank provider speaks TEI's `/rerank` and presents it as the
Cohere-shaped `/v1/rerank`.

This module used to call TEI directly and send `truncate: true`, because LiteLLM's rerank
transform hard-codes `"truncate": false` and exposes no parameter to override it, and a
chunk over the model's 512-token input window 422s and takes its whole BATCH with it —
degrading a search rather than failing it, which is the kind of regression nobody notices.
The corpus was believed to hold 544-token chunks, which made that a live hazard.

Re-measured, it does not and never did: the worst chunk is 350 tokens, and the 544 came
from estimating tokens off a character count. The proxy route is safe here because the
margin is genuinely wide, NOT because anything enforces it — app/chunking.py still sizes
in tiktoken, which is a different tokenizer from this model's. That mismatch is documented
and accepted there, with the measurements behind it. If chunk sizing changes, or the
service starts ingesting document types unlike the templated CRE PDFs it was tuned on,
that comment is the thing to re-read before trusting this path.
"""

import logging

import httpx

from . import fusion
from .config import settings

log = logging.getLogger("rerank")

Key = fusion.Key

# Pairs per HTTP request. A deal holds ~17 chunks (max 33), so a deal-scoped rerank is
# normally a single call; the batch only bites on the unscoped haystack, where the pool is
# capped at candidate_k. Measured at 1.2s for 32 pairs of the longest chunks in the corpus.
_BATCH = 32

_client: httpx.Client | None = None


def client() -> httpx.Client:
    global _client
    if _client is None:
        _client = httpx.Client(
            base_url=settings.litellm_base_url,
            timeout=settings.rerank_timeout,
            headers={"Authorization": f"Bearer {settings.litellm_api_key}"},
        )
    return _client


def _rerank(query: str, documents: list[str]) -> list[dict]:
    response = client().post("/v1/rerank", json={
        "model": settings.rerank_route,
        "query": query,
        "documents": documents,
        # Explicit, because Cohere's default top_n is not "all" everywhere it is
        # implemented, and a silently short result set would be scattered into a
        # zero-filled score list rather than raising. score() checks the count for the
        # same reason.
        "top_n": len(documents),
    })
    response.raise_for_status()
    return response.json().get("results", [])


def ping() -> None:
    """Raises if the reranker is not reachable. Used by /health.

    A real one-pair rerank rather than a liveness probe: /health already checks that the
    proxy is up, so the only thing left worth learning here is whether the `rerank-local`
    route resolves to a model that answers — which is exactly the failure a proxy hop
    introduces. Costs one CPU forward pass on two short strings.
    """
    if not _rerank("ping", ["ping"]):
        raise RuntimeError("reranker returned no results for the health probe")


def score(query: str, texts: list[str]) -> list[float]:
    """Relevance scores in INPUT order, one per text. Raises on failure — the caller
    decides whether to degrade.

    Results come back sorted best-first, so they are scattered back by their `index`
    field. Returning the API's order instead would work by accident today and break
    silently the moment a caller zips these against its own list.
    """
    out: list[float] = [0.0] * len(texts)
    for start in range(0, len(texts), _BATCH):
        batch = texts[start:start + _BATCH]
        results = _rerank(query, batch)
        if len(results) != len(batch):
            raise RuntimeError(f"reranker returned {len(results)} scores for {len(batch)} texts")
        for hit in results:
            out[start + hit["index"]] = float(hit["relevance_score"])
    return out


def order(keys: list[Key], scores: dict[Key, float], cosine: dict[Key, float]) -> list[Key]:
    """Rerank score descending, with a fully deterministic tie-break.

    Same discipline as fusion.order() and for the same reason: an arbitrary order among
    equal scores would make eval runs irreproducible, which quietly poisons an A/B whose
    entire signal is ordering. Cosine descending comes first so ties resolve to the
    pre-rerank behaviour; the key itself then guarantees a total order.

    Cross-encoder scores are far more peaked than cosine — a sigmoid output sits at 0.996
    for a direct answer and 1e-5 for an unrelated chunk — so exact ties are rarer here than
    in RRF. The tie-break is cheap insurance, not a load-bearing mechanism.
    """
    return sorted(keys, key=lambda key: (-scores.get(key, 0.0), -cosine.get(key, 0.0),
                                         key[0], key[1]))
