#!/usr/bin/env python3
"""Simulate diversity reranking over the real corpus, without any diversity code shipped.

This regenerates the table in docs/retrieval-eval.md that justifies NOT implementing MMR.
The diversity stage was built, measured as harmful, and deleted, so the eval harness can no
longer produce those arms — but the *simulation* needs nothing from the server beyond a
normal search, because both operators are pure re-orderings of a ranked list:

  - MMR:   lambda * cosine(query, chunk) - (1 - lambda) * max cosine(chunk, already-picked)
  - quota: keep the first N chunks of each document in rank order, hold back the rest

Pairwise chunk-chunk similarity comes from pgvector's own `<=>` operator via a self-join,
so the arithmetic is the same one the retrieval path uses rather than a reimplementation.

The metric is depth_to_satisfy: for a cross-document question the rank of the LAST required
source, which is exactly what a reordering stage is supposed to move. Lower is better; the
context budget is 8, so a question is fixed when its depth drops to <= 8.

Targets are chosen from the live ranking rather than hard-coded: every cross-document
question whose sources are all fetched in the top 20 but which cosine order fails to satisfy
within the budget. Those are exactly the questions a reordering stage could fix. The search
is cosine order (rerank off) so MMR is compared against the ranking it would replace.

Usage (needs the stack up):
    python3 scripts/analyze_diversity_sim.py                 # embed-local@1024, the shipped model
    python3 scripts/analyze_diversity_sim.py --model embed-openai@1024
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import eval_retrieval as ev  # noqa: E402

LAMBDAS = (0.3, 0.5, 0.7, 0.9)
QUOTAS = (1, 2)
BUDGET = 8


def sim_matrix(keys: list[tuple[str, int]], model: str) -> dict:
    """Pairwise cosine between chunks, computed by pgvector via a self-join."""
    values = ",".join(f"('{d}',{i})" for d, i in keys)
    sql = f"""
        WITH k(d,i) AS (VALUES {values})
        SELECT a.document_id, a.chunk_index, b.document_id, b.chunk_index,
               1 - (a.embedding <=> b.embedding)
        FROM document_chunks a
        JOIN k ka ON ka.d = a.document_id AND ka.i = a.chunk_index
        JOIN document_chunks b ON b.embedding_model = a.embedding_model
        JOIN k kb ON kb.d = b.document_id AND kb.i = b.chunk_index
        WHERE a.embedding_model = '{model}';
    """
    out = subprocess.run(
        ["docker", "compose", "exec", "-T", "rag-db", "psql", "-U", "proptrack",
         "-d", "proptrack_rag", "-t", "-A", "-F", "|", "-c", sql],
        capture_output=True, text=True, check=True)
    sim = {}
    for line in out.stdout.strip().splitlines():
        ad, ai, bd, bi, score = line.split("|")
        sim[((ad, int(ai)), (bd, int(bi)))] = float(score)
    return sim


def mmr(chunks: list[dict], sim: dict, lam: float) -> list[dict]:
    pool, picked = list(chunks), []
    while pool:
        best, best_score = None, None
        for c in pool:
            key = (c["documentId"], c["chunkIndex"])
            redundancy = max(
                (sim.get((key, (p["documentId"], p["chunkIndex"])), 0.0) for p in picked),
                default=0.0)
            score = lam * c["score"] - (1 - lam) * redundancy
            if best_score is None or score > best_score:
                best, best_score = c, score
        picked.append(best)
        pool.remove(best)
    return picked


def quota(chunks: list[dict], n: int) -> list[dict]:
    taken, held, seen = [], [], {}
    for c in chunks:
        doc = c["documentId"]
        if seen.get(doc, 0) < n:
            taken.append(c)
            seen[doc] = seen.get(doc, 0) + 1
        else:
            held.append(c)
    return taken + held


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    # Must match the tag ingestion-service is serving (EMBEDDING_MODEL), or the pairwise
    # similarities come from a different vector space than the ranking they reorder.
    ap.add_argument("--model", default="embed-local@1024", help="embedding_model tag in document_chunks")
    args = ap.parse_args()

    questions = [q for q in json.loads(Path("scripts/eval-questions.json").read_text())
                 if q["slice"] == "cross-document"]
    token = ev.login("admin@proptrack.local", "ChangeMe123!")

    ranked = {q["id"]: ev.search(token, q["question"], q["dealId"], 20,
                                 candidate_k=ev.CANDIDATE_K, rerank=False)
              for q in questions}
    targets = [q for q in questions
               if ev.depth_to_satisfy(ranked[q["id"]], q)
               and ev.depth_to_satisfy(ranked[q["id"]], q) > BUDGET]
    print(f"\n  {len(targets)} of {len(questions)} cross-document questions are fetched but "
          f"cut at k={BUDGET} in cosine order ({args.model}).")

    cols = [f"mmr{lam}" for lam in LAMBDAS] + [f"q={n}" for n in QUOTAS]
    print(f"\n  {'question':<26}{'base':>6}" + "".join(f"{c:>8}" for c in cols))
    print("  " + "-" * (32 + 8 * len(cols)))

    fixed = dict.fromkeys(cols, 0)
    worse = dict.fromkeys(cols, 0)

    for q in targets:
        chunks = ranked[q["id"]]
        sim = sim_matrix([(c["documentId"], c["chunkIndex"]) for c in chunks], args.model)
        base = ev.depth_to_satisfy(chunks, q)
        row = f"  {q['id'][:25]:<26}{base if base else '-':>6}"
        for col, reordered in zip(
                cols, [mmr(chunks, sim, lam) for lam in LAMBDAS]
                      + [quota(chunks, n) for n in QUOTAS]):
            depth = ev.depth_to_satisfy(reordered, q)
            row += f"{depth if depth else '-':>8}"
            if depth and depth <= BUDGET:
                fixed[col] += 1
            if depth and base and depth > base:
                worse[col] += 1
        print(row)

    n = len(targets)
    print("  " + "-" * (32 + 8 * len(cols)))
    print(f"  {'fixed at k=8':<26}{'':>6}" + "".join(f"{f'{fixed[c]}/{n}':>8}" for c in cols))
    print(f"  {'worse than base':<26}{'':>6}" + "".join(f"{f'{worse[c]}/{n}':>8}" for c in cols))
    print("\n  Cross-document questions ask whether two documents AGREE, so the chunk that")
    print("  must be promoted states the same fact as one already picked — its nearest")
    print("  neighbour. MMR is defined to demote exactly that. See docs/retrieval-eval.md.\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
