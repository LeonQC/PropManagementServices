#!/usr/bin/env python3
"""Measure Deal Q&A retrieval against the labeled question set. No LLM involved.

This scores the *retrieval* half of the feature only — did the right text reach the
prompt? — which is deterministic, free, and where the tuned constants live. Whether
the model then wrote a good answer is a separate question, measured by eval_ragas.py.
Keeping them apart is what lets a bad answer be attributed to one half or the other.

Each question is sent to ingestion-service's /ingestion/v1/search, which returns the
raw ranked chunks with cosine scores and costs one embedding call. The filter chain
that ai-service applies on top is then re-run here, in Python.

That duplicates ~20 lines of RetrievalService.cs, and the two copies can drift. It's
deliberate: with the filters in the harness a parameter sweep is a loop rather than a
rebuild-redeploy cycle, which is where nearly all the value of this script is. Guard
against drift by keeping --min-score/--relative-floor/--max-chunks defaults identical
to RetrievalOptions.cs and re-reading both when either changes.

Usage:
    python3 scripts/eval_retrieval.py                      # baseline, current config
    python3 scripts/eval_retrieval.py --limit 10           # smoke test
    python3 scripts/eval_retrieval.py --sweep              # parameter grid
    python3 scripts/eval_retrieval.py --no-floors          # ablation
"""
from __future__ import annotations

import argparse
import itertools
import json
import statistics
import sys
import urllib.error
import urllib.request
from collections import defaultdict
from pathlib import Path

AUTH_URL = "http://localhost:5300"
INGESTION_URL = "http://localhost:5500"

# Defaults mirror RetrievalOptions.cs. Keep them in sync — see module docstring.
FETCH_TOP_K = 20
MAX_CONTEXT_CHUNKS = 8
MIN_SCORE = 0.15
RELATIVE_FLOOR = 0.55
MAX_CONTEXT_CHARS = 24_000

SLICES = ("single-fact", "table-lookup", "cross-document", "unanswerable", "off-domain")
POSITIVE_SLICES = ("single-fact", "table-lookup", "cross-document")


# ---------- HTTP ----------

def _request(method: str, url: str, *, token: str | None = None, payload: dict | None = None):
    body = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=body, method=method)
    if payload is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req) as resp:
        text = resp.read()
        return json.loads(text) if text else None


def login(email: str, password: str) -> str:
    return _request("POST", f"{AUTH_URL}/auth/v1/login",
                    payload={"email": email, "password": password})["data"]["accessToken"]


def search(token: str, question: str, deal_id: str, top_k: int) -> list[dict]:
    payload = {"query": question, "dealId": deal_id, "topK": top_k}
    return _request("POST", f"{INGESTION_URL}/ingestion/v1/search",
                    token=token, payload=payload)["data"]["chunks"]


# ---------- the filter chain, mirroring RetrievalService.cs ----------

class Config:
    def __init__(self, min_score=MIN_SCORE, relative_floor=RELATIVE_FLOOR,
                 max_chunks=MAX_CONTEXT_CHUNKS, max_chars=MAX_CONTEXT_CHARS, fetch_k=FETCH_TOP_K):
        self.min_score = min_score
        self.relative_floor = relative_floor
        self.max_chunks = max_chunks
        self.max_chars = max_chars
        self.fetch_k = fetch_k

    def label(self) -> str:
        return (f"min={self.min_score:.2f} rel={self.relative_floor:.2f} "
                f"k={self.max_chunks} fetch={self.fetch_k}")


def apply_filters(chunks: list[dict], cfg: Config) -> list[dict]:
    """ApplyRelevanceFloor -> TakeWithinBudget. Reading-order regrouping is omitted:
    it changes presentation order only, never which chunks are kept, so it cannot
    affect any metric here."""
    if not chunks:
        return []
    best = max(c["score"] for c in chunks)
    relative = best * cfg.relative_floor
    kept = sorted((c for c in chunks if c["score"] >= cfg.min_score and c["score"] >= relative),
                  key=lambda c: -c["score"])

    out, chars = [], 0
    for chunk in kept:
        if len(out) >= cfg.max_chunks:
            break
        if chars + len(chunk["text"]) > cfg.max_chars and out:
            break
        out.append(chunk)
        chars += len(chunk["text"])
    return out


# ---------- scoring ----------

def matches(chunk: dict, gold: dict) -> bool:
    """A chunk satisfies a gold reference when it comes from the right document and
    contains the label text. Snippet match rather than page match, because chunks
    split within a page and a page number alone would credit the wrong chunk."""
    return chunk["documentId"] == gold["documentId"] and gold["snippet"] in chunk["text"]


def gold_hit(chunks: list[dict], question: dict) -> bool:
    """Did retrieval satisfy this question's gold requirement?

    cross-document questions set requiresAll: the answer *is* the comparison, so
    retrieving one of the two sources is a failure, not a half-success."""
    gold = question["gold"]
    if not gold:
        return False
    found = [any(matches(c, g) for c in chunks) for g in gold]
    return all(found) if question.get("requiresAll") else any(found)


def gold_rank(chunks: list[dict], question: dict) -> int | None:
    """1-based rank of the first chunk satisfying any gold ref, or None."""
    for i, chunk in enumerate(chunks, start=1):
        if any(matches(chunk, g) for g in question["gold"]):
            return i
    return None


def fetch_all(token: str, questions: list[dict], fetch_k: int) -> dict[str, list[dict]]:
    """Ranked chunks per question, fetched once.

    The ranked list depends only on the question and fetch_k — the floors and the
    context cap are applied afterwards, in this process. So a 36-config sweep needs
    100 search calls, not 3600. Without this the sweep would re-embed every question
    for every config, which is both slow and a pointless embedding bill."""
    cache: dict[str, list[dict]] = {}
    for i, q in enumerate(questions, start=1):
        if not q.get("dealId"):
            print(f"  skipping {q['id']}: no dealId (was the manifest present at build time?)",
                  file=sys.stderr)
            continue
        try:
            cache[q["id"]] = search(token, q["question"], q["dealId"], fetch_k)
        except urllib.error.HTTPError as ex:
            print(f"  {q['id']}: search failed {ex.code} {ex.read().decode(errors='replace')[:120]}",
                  file=sys.stderr)
        if i % 25 == 0:
            print(f"  fetched {i}/{len(questions)}", file=sys.stderr)
    return cache


def evaluate(questions: list[dict], fetched_by_id: dict[str, list[dict]],
             cfg: Config, verbose: bool = False) -> dict:
    rows = []
    for q in questions:
        fetched = fetched_by_id.get(q["id"])
        if fetched is None:
            continue

        # A sweep may ask for fewer candidates than were fetched; truncating the cached
        # ranked list is identical to having requested that topK, since the ordering is
        # by score and does not depend on the limit.
        fetched = fetched[:cfg.fetch_k]
        kept = apply_filters(fetched, cfg)
        row = {
            "id": q["id"],
            "slice": q["slice"],
            "fetchedCount": len(fetched),
            "keptCount": len(kept),
            "keptChars": sum(len(c["text"]) for c in kept),
            "topScore": max((c["score"] for c in fetched), default=0.0),
            "hitFetched": gold_hit(fetched, q),
            "hitKept": gold_hit(kept, q),
            "rank": gold_rank(fetched, q),
            "goldInContext": sum(1 for c in kept if any(matches(c, g) for g in q["gold"])),
        }
        rows.append(row)
        if verbose:
            mark = "ok " if row["hitKept"] or not q["gold"] else "MISS"
            print(f"  [{mark}] {q['slice']:<14} rank={row['rank']} kept={row['keptCount']} {q['question'][:60]}")
    return {"config": cfg.label(), "rows": rows}


# ---------- reporting ----------

def summarize(result: dict) -> dict:
    by_slice: dict[str, list[dict]] = defaultdict(list)
    for row in result["rows"]:
        by_slice[row["slice"]].append(row)

    summary = {}
    for name in SLICES:
        rows = by_slice.get(name, [])
        if not rows:
            continue
        n = len(rows)
        entry = {"n": n, "avgKeptChunks": statistics.mean(r["keptCount"] for r in rows),
                 "avgKeptChars": statistics.mean(r["keptChars"] for r in rows)}
        if name in POSITIVE_SLICES:
            ranks = [r["rank"] for r in rows if r["rank"]]
            entry.update({
                "recallFetched": sum(r["hitFetched"] for r in rows) / n,
                "recallFinal": sum(r["hitKept"] for r in rows) / n,
                "mrr": sum(1 / r["rank"] for r in rows if r["rank"]) / n,
                "meanRank": statistics.mean(ranks) if ranks else None,
                # Share of delivered context that is gold. Low is not automatically
                # bad -- supporting context has value -- but it is what a bigger
                # MaxContextChunks costs.
                "precisionFinal": statistics.mean(
                    (r["goldInContext"] / r["keptCount"]) if r["keptCount"] else 0.0 for r in rows),
            })
        else:
            # For negatives the correct outcome is an empty context: no chunks survive
            # the floors, so DealQaService answers without calling the model at all.
            entry["abstainRate"] = sum(1 for r in rows if r["keptCount"] == 0) / n
            entry["avgTopScore"] = statistics.mean(r["topScore"] for r in rows)
        summary[name] = entry
    return summary


def print_summary(config_label: str, summary: dict) -> None:
    print(f"\n  config: {config_label}")
    print(f"  {'slice':<16}{'n':>4}{'R@fetch':>9}{'R@final':>9}{'MRR':>7}"
          f"{'meanRk':>8}{'P@final':>9}{'chunks':>8}{'chars':>8}")
    print("  " + "-" * 78)
    for name in POSITIVE_SLICES:
        s = summary.get(name)
        if not s:
            continue
        mean_rank = f"{s['meanRank']:.1f}" if s["meanRank"] is not None else "-"
        print(f"  {name:<16}{s['n']:>4}{s['recallFetched']:>9.2f}{s['recallFinal']:>9.2f}"
              f"{s['mrr']:>7.2f}{mean_rank:>8}{s['precisionFinal']:>9.2f}"
              f"{s['avgKeptChunks']:>8.1f}{s['avgKeptChars']:>8.0f}")

    positives = [summary[n] for n in POSITIVE_SLICES if n in summary]
    if positives:
        total = sum(s["n"] for s in positives)
        overall_fetch = sum(s["recallFetched"] * s["n"] for s in positives) / total
        overall_final = sum(s["recallFinal"] * s["n"] for s in positives) / total
        print("  " + "-" * 78)
        print(f"  {'ALL POSITIVE':<16}{total:>4}{overall_fetch:>9.2f}{overall_final:>9.2f}")
        # The number the filters cost, stated directly rather than left to be inferred.
        print(f"  {'filter cost':<16}{'':>4}{'':>9}{overall_fetch - overall_final:>9.2f}"
              f"   (recall lost between fetch and prompt)")

    print()
    for name in ("unanswerable", "off-domain"):
        s = summary.get(name)
        if not s:
            continue
        print(f"  {name:<16}{s['n']:>4}  abstain {s['abstainRate']:>5.2f}   "
              f"avg top score {s['avgTopScore']:.3f}   avg kept {s['avgKeptChunks']:.1f}")


# ---------- CLI ----------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--questions", type=Path, default=Path("scripts/eval-questions.json"))
    ap.add_argument("--out", type=Path, default=Path("scripts/eval-results.json"))
    ap.add_argument("--email", default="admin@proptrack.local")
    ap.add_argument("--password", default="ChangeMe123!")
    ap.add_argument("--limit", type=int, help="only run the first N questions (smoke test)")
    ap.add_argument("--slice", choices=SLICES, help="only run one slice")
    ap.add_argument("--min-score", type=float, default=MIN_SCORE)
    ap.add_argument("--relative-floor", type=float, default=RELATIVE_FLOOR)
    ap.add_argument("--max-chunks", type=int, default=MAX_CONTEXT_CHUNKS)
    ap.add_argument("--fetch-k", type=int, default=FETCH_TOP_K)
    ap.add_argument("--no-floors", action="store_true", help="ablation: disable both floors")
    ap.add_argument("--sweep", action="store_true", help="run the parameter grid")
    ap.add_argument("-v", "--verbose", action="store_true", help="per-question results")
    args = ap.parse_args()

    if not args.questions.exists():
        print(f"No question set at {args.questions}. Run build_eval_set.py first.", file=sys.stderr)
        return 1

    questions = json.loads(args.questions.read_text())
    if args.slice:
        questions = [q for q in questions if q["slice"] == args.slice]
    if args.limit:
        questions = questions[:args.limit]
    print(f"{len(questions)} question(s) loaded from {args.questions}", file=sys.stderr)

    try:
        token = login(args.email, args.password)
    except urllib.error.URLError as ex:
        print(f"Could not reach auth-service at {AUTH_URL}: {ex}\n"
              f"Is the stack up? `docker compose up -d`", file=sys.stderr)
        return 1

    configs = []
    if args.sweep:
        # Grid chosen around the production values. RelativeFloor gets the widest
        # range because it is the constant most likely to be dropping good chunks:
        # at a top score of 0.50 the current 0.55 kills everything below 0.275.
        for min_score, rel, k in itertools.product((0.10, 0.15, 0.20), (0.0, 0.35, 0.55, 0.70), (5, 8, 12)):
            configs.append(Config(min_score, rel, k, fetch_k=args.fetch_k))
    elif args.no_floors:
        configs.append(Config(0.0, 0.0, args.max_chunks, fetch_k=args.fetch_k))
    else:
        configs.append(Config(args.min_score, args.relative_floor, args.max_chunks, fetch_k=args.fetch_k))

    # Fetch at the widest candidate depth any config asks for, then reuse for all of them.
    max_fetch = max(cfg.fetch_k for cfg in configs)
    print(f"\nFetching ranked chunks for {len(questions)} question(s) at topK={max_fetch}...",
          file=sys.stderr)
    fetched_by_id = fetch_all(token, questions, max_fetch)

    all_results = []
    for cfg in configs:
        print(f"\nScoring at {cfg.label()}...", file=sys.stderr)
        result = evaluate(questions, fetched_by_id, cfg, verbose=args.verbose)
        summary = summarize(result)
        result["summary"] = summary
        all_results.append(result)
        print_summary(cfg.label(), summary)

    if args.sweep:
        print("\n\n  === sweep, ranked by positive-slice recall@final ===")
        print(f"  {'config':<38}{'R@final':>9}{'chunks':>8}{'chars':>8}")
        print("  " + "-" * 63)
        ranked = []
        for result in all_results:
            positives = [result["summary"][n] for n in POSITIVE_SLICES if n in result["summary"]]
            if not positives:
                continue
            total = sum(s["n"] for s in positives)
            ranked.append((
                sum(s["recallFinal"] * s["n"] for s in positives) / total,
                sum(s["avgKeptChunks"] * s["n"] for s in positives) / total,
                sum(s["avgKeptChars"] * s["n"] for s in positives) / total,
                result["config"],
            ))
        for recall, chunks, chars, label in sorted(ranked, reverse=True):
            print(f"  {label:<38}{recall:>9.3f}{chunks:>8.1f}{chars:>8.0f}")

    args.out.write_text(json.dumps(all_results, indent=2) + "\n")
    print(f"\nWrote results to {args.out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
