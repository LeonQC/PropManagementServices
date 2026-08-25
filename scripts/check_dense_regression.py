#!/usr/bin/env python3
"""Gate: unreranked retrieval must behave exactly as it did before the ranking work.

ai-service and this harness now order chunks by the server's `rank` rather than re-sorting
by cosine. With no reranker in play, rank order IS cosine order, so the two must produce
identical results. If they don't, every A/B number downstream is uninterpretable, because
the comparison would be measuring the plumbing change rather than the reranker.

    python3 scripts/eval_retrieval.py --out /tmp/regress.json
    python3 scripts/check_dense_regression.py /tmp/regress.json

Tolerance is asymmetric, on purpose. Per-question decisions and every recall-style metric
must match exactly. Score *means* are compared loosely because the embedding API is not
bit-deterministic: re-embedding the same question moves cosine scores by ~1e-4, which is
one unit in the last decimal place the search response rounds to. That noise cannot change
which chunks are kept (the floors sit 0.05+ away from any real score) and it must not be
allowed to fail a gate that is checking ordering.
"""
import json
import sys
from pathlib import Path

BASELINE = Path("scripts/dense-baseline-reference.json")

# Must be bit-identical: these are outcomes, not measurements of a float.
EXACT = ("n", "recallFetched", "recallFinal", "mrr", "meanRank", "precisionFinal",
         "avgKeptChunks", "avgKeptChars", "abstainRate")
# Derived from raw cosine scores, so subject to embedding non-determinism.
APPROX = {"avgTopScore": 1e-3}
# Per-question fields that encode a decision rather than a score.
ROW_FIELDS = ("slice", "fetchedCount", "keptCount", "keptChars", "hitFetched", "hitKept",
              "rank", "goldInContext")


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    if not BASELINE.exists():
        print(f"Missing {BASELINE} — the frozen pre-ranking reference. It is committed; "
              f"restore it from git rather than regenerating it.", file=sys.stderr)
        return 2
    baseline = json.loads(BASELINE.read_text())
    candidate = json.loads(Path(argv[1]).read_text())[0]

    failures: list[str] = []

    for name, expected in baseline["summary"].items():
        actual = candidate["summary"].get(name, {})
        for metric in EXACT:
            if metric not in expected:
                continue
            a, b = expected[metric], actual.get(metric)
            if a is None and b is None:
                continue
            if a is None or b is None or abs(a - b) > 1e-9:
                failures.append(f"{name}.{metric}: baseline={a} candidate={b}")
        for metric, tol in APPROX.items():
            if metric not in expected:
                continue
            a, b = expected[metric], actual.get(metric)
            if b is None or abs(a - b) > tol:
                failures.append(f"{name}.{metric}: baseline={a} candidate={b} (tol {tol})")

    base_rows = {r["id"]: r for r in baseline["rows"]}
    cand_rows = {r["id"]: r for r in candidate["rows"]}
    if set(base_rows) != set(cand_rows):
        failures.append(f"question sets differ: {len(base_rows)} vs {len(cand_rows)} rows")
    else:
        drifted = 0
        for qid, expected in base_rows.items():
            for field in ROW_FIELDS:
                if field in expected and expected[field] != cand_rows[qid].get(field):
                    failures.append(
                        f"row {qid}.{field}: baseline={expected[field]} "
                        f"candidate={cand_rows[qid].get(field)}")
                    drifted += 1
            if drifted > 20:
                failures.append("... further row differences suppressed")
                break

    if failures:
        print(f"GATE FAILED ({len(failures)} difference(s)) — do not trust any mode "
              f"comparison until this is resolved:", file=sys.stderr)
        for f in failures[:25]:
            print(f"  {f}", file=sys.stderr)
        return 1

    print(f"GATE PASSED: unreranked retrieval reproduces {BASELINE} "
          f"({len(base_rows)} questions, {len(baseline['summary'])} slices).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
