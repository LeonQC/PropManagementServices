#!/usr/bin/env python3
"""Gate: the shipped retrieval config must reproduce its frozen reference exactly.

Run this before and after any change to the retrieval path (ingestion-service search,
the rerank stage, chunking, the harness's filter chain). If a change is *meant* to move
the numbers, the gate failing is expected — re-measure, then refreeze deliberately.

    python3 scripts/eval_retrieval.py --rerank --out /tmp/regress.json
    python3 scripts/check_retrieval_regression.py /tmp/regress.json

The reference is the shipped configuration: bge-m3 (`embed-local`), cross-encoder rerank
on, MinScore 0.35 / RelativeFloor 0.55 / MaxContextChunks 12. The candidate's config label
is checked first, because a run with the wrong flags (no --rerank, a stale --max-chunks)
fails on every row and reads as a regression rather than a typo.

This replaces check_dense_regression.py, whose reference was captured under OpenAI
embeddings before reranking existed. Once embeddings moved to bge-m3 it could not pass
under any flags — cosine scores differ per model — so it was no longer gating anything.

Tolerance is asymmetric, on purpose. Per-question decisions and every recall-style metric
must match exactly. Score *means* are compared loosely: re-embedding the same question
moves cosine scores by ~1e-4, one unit in the last decimal the search response rounds to.
Two back-to-back runs of the shipped config were identical on every EXACT field and row
field when this reference was frozen, so any difference there is real.

To refreeze after an intended change:

    python3 scripts/check_retrieval_regression.py --freeze /tmp/regress.json
"""
import json
import sys
from pathlib import Path

BASELINE = Path("scripts/retrieval-baseline-reference.json")
EXPECTED_CONFIG = "rerank min=0.35 rel=0.55 k=12 fetch=20"

# Must be bit-identical: these are outcomes, not measurements of a float.
EXACT = ("n", "recallFetched", "recallFinal", "mrr", "meanRank", "precisionFinal",
         "avgKeptChunks", "avgKeptChars", "abstainRate")
# Derived from raw cosine scores, so subject to embedding non-determinism.
APPROX = {"avgTopScore": 1e-3}
# Per-question fields that encode a decision rather than a score.
ROW_FIELDS = ("slice", "fetchedCount", "keptCount", "keptChars", "hitFetched", "hitKept",
              "rank", "depth", "goldInContext")


def freeze(path: Path) -> int:
    run = json.loads(path.read_text())[0]
    if run["config"] != EXPECTED_CONFIG:
        print(f"Refusing to freeze a '{run['config']}' run as the reference "
              f"(expected '{EXPECTED_CONFIG}').", file=sys.stderr)
        return 2
    reference = {
        "_comment": "Frozen shipped retrieval behaviour (embed-local/bge-m3, rerank on, "
                    "0.35/0.55/k=12). check_retrieval_regression.py compares against this. "
                    "Committed because eval results are gitignored. Refreeze only after an "
                    "intended, measured change.",
        "config": run["config"],
        "summary": run["summary"],
        "rows": [{k: r[k] for k in ("id", *ROW_FIELDS) if k in r} for r in run["rows"]],
    }
    BASELINE.write_text(json.dumps(reference, indent=2) + "\n")
    print(f"Froze {len(reference['rows'])} rows to {BASELINE}")
    return 0


def main(argv: list[str]) -> int:
    if len(argv) == 3 and argv[1] == "--freeze":
        return freeze(Path(argv[2]))
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    if not BASELINE.exists():
        print(f"Missing {BASELINE}. It is committed; restore it from git, or --freeze a "
              f"fresh run if the retrieval path changed on purpose.", file=sys.stderr)
        return 2
    baseline = json.loads(BASELINE.read_text())
    candidate = json.loads(Path(argv[1]).read_text())[0]

    if candidate["config"] != baseline["config"]:
        print(f"Wrong config: candidate '{candidate['config']}', reference "
              f"'{baseline['config']}'. Run eval_retrieval.py with --rerank and the "
              f"default floors.", file=sys.stderr)
        return 2

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
        print(f"GATE FAILED ({len(failures)} difference(s)):", file=sys.stderr)
        for f in failures[:25]:
            print(f"  {f}", file=sys.stderr)
        return 1

    print(f"GATE PASSED: shipped retrieval reproduces {BASELINE} "
          f"({len(base_rows)} questions, {len(baseline['summary'])} slices).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
