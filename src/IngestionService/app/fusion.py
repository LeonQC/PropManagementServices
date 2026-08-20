"""Reciprocal Rank Fusion for hybrid retrieval.

RRF combines ranked lists by rank alone, never by score. That is the whole reason to use
it here: BM25 scores and cosine similarities are not commensurable, and min-max
normalising them per query would invent a calibration that does not exist — while also
destroying the absolute cosine floor that lets Deal Q&A decline off-domain questions
(see RetrievalOptions.cs and docs/retrieval-eval.md).
"""

from collections import defaultdict

# (document_id, chunk_index) — see lexical.doc_id for why this is the cross-store key.
Key = tuple[str, int]


def rrf(ranked_lists: list[list[Key]], k: int) -> dict[Key, float]:
    """Sum 1/(k + rank) over every list a key appears in, rank being 1-based.

    A key present in two lists at rank r scores 2/(k+r); a key in one list at rank 1
    scores 1/(k+1). The former wins whenever r < k+2 — so with any sane k, agreement
    between the two retrievers outranks a strong showing in just one. That premium is the
    entire mechanism.

    Note it goes inert in deal-scoped mode on this corpus: dense retrieval returns the
    whole ~17-chunk haystack, so every lexical hit is also a dense hit and the premium
    applies uniformly. Fusion then degenerates to harmonic rank-averaging of two orderings
    over the same set — it changes the order, never the set. That is a property of the
    corpus, not of RRF, and it is why the eval also runs at --fetch-k 5 and unscoped.
    """
    scores: dict[Key, float] = defaultdict(float)
    for ranked in ranked_lists:
        for rank, key in enumerate(ranked, start=1):
            scores[key] += 1.0 / (k + rank)
    return scores


def order(fused: dict[Key, float], cosine: dict[Key, float]) -> list[Key]:
    """Fused score descending, with a fully deterministic tie-break.

    The tie-break is not cosmetic. At k=60 over a 17-item list the fused scores are nearly
    flat (rank-1 to rank-20 weight ratio 1.31x), so ties and near-ties are common and an
    arbitrary order would make eval runs irreproducible — which would quietly poison an
    A/B whose entire signal is ordering. Cosine descending comes first so that ties
    resolve to exactly today's dense behaviour; the key itself then guarantees a total
    order.
    """
    return sorted(fused, key=lambda key: (-fused[key], -cosine.get(key, 0.0), key[0], key[1]))
