#!/usr/bin/env python3
"""Is the mode comparison rigged in BM25's favour? Partition the questions and see.

The eval scores a retrieval as correct when a returned chunk *contains the gold snippet*
(eval_retrieval.matches). That criterion is itself lexical, so a retriever that ranks by
term overlap is being graded by a term-overlap test. If the questions also happen to
contain the snippet's words verbatim, a BM25 win measures the question generator's
phrasing rather than retrieval quality.

This splits the question set by how much verbatim help BM25 gets for free — the share of
gold-snippet tokens that appear in the question — and reports each mode's recall within
each stratum. The zero-overlap stratum is the honest test: BM25 has no free token there.

    python3 scripts/analyze_lexical_bias.py [results.json]
"""
from __future__ import annotations

import json
import re
import statistics
import sys
from pathlib import Path

DEFAULT = Path("scripts/eval-results-rerank.json")
QUESTIONS = Path("scripts/eval-questions.json")

# Function words plus domain words that appear in nearly every question, so "overlap"
# reflects the discriminating tokens rather than the boilerplate.
STOP = set("the a an of is are was were for what how much does do this that in on to and "
           "or at by with its it their there which who whom when where property deal".split())


def tokens(text: str) -> set[str]:
    return {t for t in re.findall(r"[a-z0-9]+", text.lower()) if t not in STOP and len(t) > 2}


def lexical_help(question: dict) -> float | None:
    """Max share of a gold snippet's tokens that the question already contains."""
    qt = tokens(question["question"])
    shares = [len(tokens(g["snippet"]) & qt) / len(tokens(g["snippet"]))
              for g in question.get("gold", []) if tokens(g["snippet"])]
    return max(shares) if shares else None


def main(argv: list[str]) -> int:
    results = json.loads(Path(argv[1] if len(argv) > 1 else DEFAULT).read_text())
    questions = json.loads(QUESTIONS.read_text())

    helps = {q["id"]: h for q in questions if (h := lexical_help(q)) is not None}
    strata = {
        "zero":    {q for q, h in helps.items() if h == 0.0},
        "partial": {q for q, h in helps.items() if 0.0 < h < 1.0},
        "full":    {q for q, h in helps.items() if h == 1.0},
    }
    by_slice: dict[str, list[float]] = {}
    for q in questions:
        if q["id"] in helps:
            by_slice.setdefault(q["slice"], []).append(helps[q["id"]])

    print("Verbatim lexical help available to BM25 (share of gold-snippet tokens "
          "already present in the question):\n")
    for name, vals in sorted(by_slice.items()):
        print(f"  {name:<16} n={len(vals):>3}  mean={statistics.mean(vals):.2f}  "
              f"fully-overlapping {sum(1 for v in vals if v == 1.0)}/{len(vals)}")
    print(f"\n  strata: zero={len(strata['zero'])} partial={len(strata['partial'])} "
          f"full={len(strata['full'])}")
    print("\n  'zero' is the unbiased stratum. A mode that only wins on 'full' is winning "
          "\n  because the question repeats the answer's label, not because it retrieves better.\n")

    header = f"  {'config':<54}" + "".join(f"{k:>14}" for k in ("zero", "partial", "full", "all"))
    print(header)
    print("  " + "-" * (len(header) - 2))
    for result in results:
        rows = {r["id"]: r for r in result["rows"]}
        cells = []
        for name in ("zero", "partial", "full", None):
            subset = strata[name] if name else set(helps)
            sel = [rows[q] for q in subset if q in rows]
            cells.append(f"{sum(r['hitKept'] for r in sel)/len(sel):.2f} (n={len(sel)})"
                         if sel else "-")
        print(f"  {result['config']:<54}" + "".join(f"{c:>14}" for c in cells))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
