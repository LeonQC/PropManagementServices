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

Served by a HuggingFace TEI container (`tei-rerank` in docker-compose.yml) rather than
through the LiteLLM proxy that fronts the embedding models. That looks inconsistent and is
deliberate: TEI's `/rerank` is not the OpenAI-shaped `/v1/rerank`, and LiteLLM's TEI rerank
transform hard-codes `"truncate": false` with no way to override it. This corpus has chunks
of 544 tokens against the model's 512-token limit, so that path returns HTTP 422 and drops
the whole batch — intermittently, depending on which chunks a query happens to retrieve.
Calling TEI directly is the same shape as lexical.py calling OpenSearch directly.
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
        _client = httpx.Client(base_url=settings.rerank_url, timeout=settings.rerank_timeout)
    return _client


def ping() -> None:
    """Raises if the reranker is not reachable. Used by /health."""
    client().get("/health").raise_for_status()


def score(query: str, texts: list[str]) -> list[float]:
    """Relevance scores in INPUT order, one per text. Raises on failure — the caller
    decides whether to degrade.

    TEI returns hits sorted best-first, so the results are scattered back by their `index`
    field. Returning TEI's order instead would work by accident today and break silently
    the moment a caller zips these against its own list.
    """
    out: list[float] = [0.0] * len(texts)
    for start in range(0, len(texts), _BATCH):
        batch = texts[start:start + _BATCH]
        response = client().post("/rerank", json={
            "query": query,
            "texts": batch,
            # Mandatory, not a default worth trusting. Chunks run to 2,089 characters
            # (544 tokens) against this model's 512-token limit, and TEI's default
            # truncate=false rejects the ENTIRE batch with a 422 when one member is
            # oversized. Right-truncation keeps the head of the chunk, which is where a
            # templated CRE document puts its label/value summary block.
            "truncate": True,
            "truncation_direction": "Right",
        })
        response.raise_for_status()
        hits = response.json()
        if len(hits) != len(batch):
            raise RuntimeError(f"reranker returned {len(hits)} scores for {len(batch)} texts")
        for hit in hits:
            out[start + hit["index"]] = float(hit["score"])
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
