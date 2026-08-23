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
    python3 scripts/eval_retrieval.py --sweep-mode         # dense vs lexical vs hybrid
    python3 scripts/eval_retrieval.py --sweep-mode --fetch-k 5     # truncated haystack
    python3 scripts/eval_retrieval.py --sweep-mode --no-deal-scope # corpus-wide haystack
    python3 scripts/eval_retrieval.py --sweep-rerank       # the cross-encoder A/B
    python3 scripts/eval_retrieval.py --mode hybrid --rerank       # one reranked arm

The rerank arms need the tei-rerank container up; it starts with the rest of the stack.
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
# 12 since cross-encoder reranking shipped; it was 8 through the dense and hybrid eras.
# check_dense_regression.py's frozen baseline is a k=8 artifact, so the gate command has to
# pass --max-chunks 8 explicitly — see docs/retrieval-eval.md.
MAX_CONTEXT_CHUNKS = 12
# 0.375 for embed-local (bge-m3); was 0.15 under embed-openai. Calibrated per embedding
# model and NOT portable — see RetrievalOptions.MinScore.
MIN_SCORE = 0.375
RELATIVE_FLOOR = 0.55
MAX_CONTEXT_CHARS = 24_000

# Hybrid retrieval. Mirrors Settings.rrf_k / Settings.candidate_k in ingestion-service.
MODES = ("dense", "lexical", "hybrid")
RRF_K = 10
CANDIDATE_K = 50

# Cross-encoder reranking. Mirrors Settings.rerank_candidates in ingestion-service.
RERANK_CANDIDATES = 30

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


def search(token: str, question: str, deal_id: str | None, top_k: int, *,
           mode: str | None = None, rrf_k: int | None = None,
           candidate_k: int | None = None, rerank: bool = False) -> list[dict]:
    payload = {"query": question, "dealId": deal_id, "topK": top_k}
    if mode:
        payload["mode"] = mode
    if rrf_k:
        payload["rrfK"] = rrf_k
    if candidate_k:
        payload["candidateK"] = candidate_k
    # ALWAYS sent, true or false — never omitted. Omitting it hands the decision to the
    # server's RERANK_ENABLED, so the moment reranking was turned on in compose every
    # "control" arm here would silently become a reranked arm and the A/B would compare a
    # thing to itself. This is the same failure the fetch-cache key guards against, and it
    # is why `mode` is sent explicitly too.
    payload["rerank"] = bool(rerank)
    data = _request("POST", f"{INGESTION_URL}/ingestion/v1/search",
                    token=token, payload=payload)["data"]
    if data.get("degraded"):
        # ingestion-service fell back to dense because OpenSearch errored, or the reranker
        # was unreachable. Scoring it would silently mix arms and report "hybrid made no
        # difference" — the exact false negative this whole comparison exists to avoid.
        raise RuntimeError(f"ingestion-service degraded (mode={data['mode']}, "
                           f"rerank={data.get('rerank')}): refusing to score a mixed run. "
                           f"Are OpenSearch and tei-rerank up?")
    if rerank and not data.get("rerank"):
        # A server that predates the rerank stage — or one that ignores the flag — returns
        # a perfectly plausible un-reranked list, and the arm reads as "reranking changed
        # nothing". Same class of silent false negative as the fetch-cache key below, one
        # layer up, so it gets the same treatment: refuse rather than report.
        raise RuntimeError("requested rerank but the server did not apply it: refusing to "
                           "score. Is ingestion-service running the reranking build?")
    return data["chunks"]


# ---------- the filter chain, mirroring RetrievalService.cs ----------

class Config:
    def __init__(self, min_score=MIN_SCORE, relative_floor=RELATIVE_FLOOR,
                 max_chunks=MAX_CONTEXT_CHUNKS, max_chars=MAX_CONTEXT_CHARS, fetch_k=FETCH_TOP_K,
                 mode="dense", rrf_k=RRF_K, candidate_k=CANDIDATE_K, deal_scope=True,
                 rerank=False, rerank_candidates=RERANK_CANDIDATES):
        self.min_score = min_score
        self.relative_floor = relative_floor
        self.max_chunks = max_chunks
        self.max_chars = max_chars
        self.fetch_k = fetch_k
        self.mode = mode
        self.rrf_k = rrf_k
        self.candidate_k = candidate_k
        self.deal_scope = deal_scope
        self.rerank = rerank
        self.rerank_candidates = rerank_candidates

    def label(self) -> str:
        # Mode first so a mode sweep sorts readably. The stage tag stays terse because this
        # string is printed in a fixed-width column in the comparison table.
        rrf = f" rrf={self.rrf_k}" if self.mode == "hybrid" else ""
        scope = "" if self.deal_scope else " unscoped"
        tag = " +rr" if self.rerank else ""
        return (f"{self.mode}{rrf}{scope}{tag} min={self.min_score:.2f} "
                f"rel={self.relative_floor:.2f} k={self.max_chunks} fetch={self.fetch_k}")

    def fetch_key(self) -> tuple:
        """What the server-side ranked list depends on.

        min_score / relative_floor / max_chunks are deliberately absent: they are applied
        here in-process, which is what makes a multi-config sweep cost one search call per
        question rather than one per question per config.

        mode/candidate_k/rrf_k are NOT optional here. Keying the cache by question id
        alone — as this script did before hybrid existed — would serve the first mode's
        ranked list to every later mode, producing three identical columns that read as
        "hybrid changed nothing". That is the single most plausible way to get a wrong
        answer out of this comparison.

        rerank/rerank_candidates are here for exactly the same reason, and it is
        worth being blunt about it: they reorder the list SERVER-side, so a rerank arm that
        shared the control's cache would report "reranking changed nothing" — the identical
        false negative, one stage later. search() carries a second, independent guard (it
        rejects a response whose `rerank` flag came back false), because this one is a
        silent omission and that one is a loud assertion.
        """
        return (self.mode, self.candidate_k, self.deal_scope,
                self.rrf_k if self.mode == "hybrid" else None,
                self.rerank,
                self.rerank_candidates if self.rerank else None)


def apply_filters(chunks: list[dict], cfg: Config) -> list[dict]:
    """ApplyRelevanceFloor -> TakeWithinBudget. Reading-order regrouping is omitted:
    it changes presentation order only, never which chunks are kept, so it cannot
    affect any metric here."""
    if not chunks:
        return []
    best = max(c["score"] for c in chunks)
    relative = best * cfg.relative_floor
    # Filter on cosine, order on the server's rank — mirroring RetrievalService.
    # ApplyRelevanceFloor. The two are separate decisions: the floors are calibrated on the
    # cosine scale and stay there, while the ordering is the server's, which in hybrid mode
    # is the RRF ordering. `rank` is absent from a pre-hybrid server, and the sentinel then
    # collapses this to the previous pure-score ordering.
    kept = sorted((c for c in chunks if c["score"] >= cfg.min_score and c["score"] >= relative),
                  key=lambda c: (c.get("rank") or 1 << 30, -c["score"]))

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


def depth_to_satisfy(chunks: list[dict], question: dict) -> int | None:
    """How deep the ranked list must be read before the question becomes answerable.

    gold_rank() gives the rank of the *first* gold hit, which is the wrong statistic for
    requiresAll questions: the answer *is* the comparison, so what matters is the rank of
    the *last* required source. That is precisely the number better ranking moves, and
    unlike the binary hitKept it shows partial progress — a cross-document question whose
    second source climbs from rank 14 to rank 9 has improved even though both still miss
    an 8-chunk budget."""
    gold = question["gold"]
    if not gold:
        return None
    ranks = [next((i for i, c in enumerate(chunks, start=1) if matches(c, g)), None) for g in gold]
    if question.get("requiresAll"):
        return max(ranks) if all(r is not None for r in ranks) else None
    found = [r for r in ranks if r is not None]
    return min(found) if found else None


def fetch_all(token: str, questions: list[dict], fetch_k: int, *, mode: str | None = None,
              rrf_k: int | None = None, candidate_k: int | None = None,
              deal_scope: bool = True, rerank: bool = False) -> dict[str, list[dict]]:
    """Ranked chunks per question, fetched once per distinct Config.fetch_key().

    The ranked list depends only on the question and the fetch key — the floors and the
    context cap are applied afterwards, in this process. So a 36-config sweep needs
    100 search calls, not 3600. Without this the sweep would re-embed every question
    for every config, which is both slow and a pointless embedding bill.

    fetch_k is correctly absent from the key, but that now rests on a *cross-service*
    invariant rather than a local one: ingestion-service's candidate depth is independent
    of the request's topK (see Settings.candidate_k AND Settings.rerank_candidates — both
    have to hold), so a smaller topK returns a prefix of a larger one and truncating the
    cached list stays exact. If either ever becomes a function of topK on the server, this
    optimisation silently starts lying."""
    cache: dict[str, list[dict]] = {}
    failures: list[str] = []
    for i, q in enumerate(questions, start=1):
        if deal_scope and not q.get("dealId"):
            print(f"  skipping {q['id']}: no dealId (was the manifest present at build time?)",
                  file=sys.stderr)
            continue
        try:
            cache[q["id"]] = search(token, q["question"], q["dealId"] if deal_scope else None,
                                    fetch_k, mode=mode, rrf_k=rrf_k, candidate_k=candidate_k,
                                    rerank=rerank)
        except urllib.error.HTTPError as ex:
            detail = ex.read().decode(errors="replace")[:120]
            failures.append(f"{q['id']}: {ex.code} {detail}")
            print(f"  {q['id']}: search failed {ex.code} {detail}", file=sys.stderr)
        if i % 25 == 0:
            print(f"  fetched {i}/{len(questions)}", file=sys.stderr)
    if failures:
        # Never return a partial cache quietly. A mode arm that lost half its questions
        # still produces a summary — with a different denominator — and lands in the
        # comparison table looking like a real measurement. That happened once, to an
        # expired token on a long multi-mode sweep, and the arms silently dropped to n=25.
        raise RuntimeError(
            f"{len(failures)} of {len(questions)} searches failed for mode={mode}: "
            f"refusing to score a partial arm.\n  " + "\n  ".join(failures[:5])
            + ("\n  ..." if len(failures) > 5 else ""))
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
            "depth": depth_to_satisfy(fetched, q),
            "goldInContext": sum(1 for c in kept if any(matches(c, g) for g in q["gold"])),
        }
        # Budget-free proxy for recallFinal: would this question be satisfied if the only
        # thing standing between fetch and prompt were the chunk count? Isolates ranking
        # quality from the floors entirely.
        row["depthWithinK"] = row["depth"] is not None and row["depth"] <= cfg.max_chunks
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
            depths = [r["depth"] for r in rows if r["depth"]]
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
                "meanDepthToSatisfy": statistics.mean(depths) if depths else None,
                "depthWithinK": sum(r["depthWithinK"] for r in rows) / n,
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
          f"{'meanRk':>8}{'depth':>7}{'d<=k':>7}{'P@final':>9}{'chunks':>8}{'chars':>8}")
    print("  " + "-" * 92)
    for name in POSITIVE_SLICES:
        s = summary.get(name)
        if not s:
            continue
        mean_rank = f"{s['meanRank']:.1f}" if s["meanRank"] is not None else "-"
        depth = f"{s['meanDepthToSatisfy']:.1f}" if s["meanDepthToSatisfy"] is not None else "-"
        print(f"  {name:<16}{s['n']:>4}{s['recallFetched']:>9.2f}{s['recallFinal']:>9.2f}"
              f"{s['mrr']:>7.2f}{mean_rank:>8}{depth:>7}{s['depthWithinK']:>7.2f}"
              f"{s['precisionFinal']:>9.2f}{s['avgKeptChunks']:>8.1f}{s['avgKeptChars']:>8.0f}")

    positives = [summary[n] for n in POSITIVE_SLICES if n in summary]
    if positives:
        total = sum(s["n"] for s in positives)
        overall_fetch = sum(s["recallFetched"] * s["n"] for s in positives) / total
        overall_final = sum(s["recallFinal"] * s["n"] for s in positives) / total
        print("  " + "-" * 92)
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
    ap.add_argument("--mode", choices=MODES, default="dense",
                    help="retrieval mode requested from ingestion-service")
    ap.add_argument("--rrf-k", type=int, default=RRF_K, help="RRF rank constant (hybrid only)")
    ap.add_argument("--candidate-k", type=int, default=CANDIDATE_K,
                    help="per-source candidate depth requested from the server")
    ap.add_argument("--rerank", action="store_true",
                    help="ask the server to cross-encoder rerank the fused candidates")
    ap.add_argument("--rerank-candidates", type=int, default=RERANK_CANDIDATES,
                    help="reported in the fetch key; the server's depth is RERANK_CANDIDATES")
    ap.add_argument("--sweep-rerank", action="store_true",
                    help="the rerank A/B: control, treatment, and the cheap baselines")
    ap.add_argument("--no-deal-scope", action="store_true",
                    help="secondary experiment: search the whole corpus, not one deal")
    ap.add_argument("--no-floors", action="store_true", help="ablation: disable both floors")
    ap.add_argument("--sweep", action="store_true", help="run the parameter grid")
    ap.add_argument("--sweep-mode", action="store_true",
                    help="the mode A/B: dense vs lexical vs hybrid, filters held fixed")
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

    scope = not args.no_deal_scope
    common = dict(fetch_k=args.fetch_k, candidate_k=args.candidate_k, deal_scope=scope,
                  rerank_candidates=args.rerank_candidates)
    staged = dict(rerank=args.rerank)
    configs = []
    if args.sweep:
        # Grid chosen around the production values. RelativeFloor gets the widest
        # range because it is the constant most likely to be dropping good chunks:
        # at a top score of 0.50 the current 0.55 kills everything below 0.275.
        for min_score, rel, k in itertools.product((0.30, 0.375, 0.45), (0.0, 0.35, 0.55, 0.70), (5, 8, 12)):
            configs.append(Config(min_score, rel, k, mode=args.mode, rrf_k=args.rrf_k,
                                  **common, **staged))
    elif args.sweep_rerank:
        # Six arms, NOT rerank crossed into the 11-config mode sweep. Reranking costs
        # ~2.5s per query, so a 22-arm cross product is over an hour of wall clock to
        # re-measure constants the existing sweep already pinned at 0.000 effect. Each arm
        # is here to falsify a different claim.
        #
        # k=8 and k=12 are written literally rather than via MAX_CONTEXT_CHUNKS: that
        # constant tracks RetrievalOptions.cs and moved to 12 when reranking shipped, but
        # these arms are a fixed historical comparison and must not drift with it.
        base = dict(mode="hybrid", rrf_k=args.rrf_k)
        configs += [
            # The pre-rerank control.
            Config(MIN_SCORE, RELATIVE_FLOOR, 8, **base, **common),
            # The treatment, at the pre-rerank context budget.
            Config(MIN_SCORE, RELATIVE_FLOOR, 8, **base, **common, rerank=True),
            # The cheap baseline: k=12 buys 0.975 for +32% context and no latency. If
            # reranking only matches this, it is not worth a container.
            Config(MIN_SCORE, RELATIVE_FLOOR, 12, **base, **common),
            # The shipped configuration: both levers. They turned out to be complements,
            # which is the finding the k=8 rows alone would have missed.
            Config(MIN_SCORE, RELATIVE_FLOOR, 12, **base, **common, rerank=True),
            # Is the cross-encoder adding to BM25, or replacing it? dense+rerank matching
            # hybrid+rerank means fusion has stopped contributing to the ordering.
            Config(MIN_SCORE, RELATIVE_FLOOR, 8, mode="dense", rrf_k=args.rrf_k,
                   **common, rerank=True),
            # The uncomfortable control: pure BM25 scores 0.950 / x-doc 0.90 on a question
            # set that analyze_lexical_bias.py shows flatters it. The reranker faces the
            # same audit.
            Config(MIN_SCORE, RELATIVE_FLOOR, 8, mode="lexical", rrf_k=args.rrf_k, **common),
        ]
    elif args.sweep_mode:
        # k=8/k=12 are literal here, like in --sweep-rerank above: MAX_CONTEXT_CHUNKS
        # tracks the shipped RetrievalOptions value and is now 12, which would collapse
        # these two arms into one and silently halve the sweep.
        #
        # Deliberately NOT the 36-config grid crossed with 3 modes. The existing sweep
        # shows min_score and relative_floor move recall by exactly 0.000, so crossing
        # them in would triple the runtime to re-measure a constant. Production filters
        # are held fixed instead, because MaxContextChunks is the one variable that does
        # move recall (by 0.087-0.174) and would swamp the mode effect being measured.
        for mode in MODES:
            for rrf_k in ((10, 20, 60) if mode == "hybrid" else (RRF_K,)):
                for k in (8, 12):
                    configs.append(Config(MIN_SCORE, RELATIVE_FLOOR, k,
                                          mode=mode, rrf_k=rrf_k, **common))
        # One floors-off hybrid arm. The relative floor can veto exactly the chunks BM25
        # promotes — a cosine-0.20 chunk dies against a 0.45 best however good its RRF
        # rank. It is not binding today (rel moves recall by 0.000), but if hybrid gains
        # materially more here than at rel=0.55, the floor is capping fusion and that is
        # a finding rather than a bug to paper over.
        configs.append(Config(MIN_SCORE, 0.0, 8,
                              mode="hybrid", rrf_k=args.rrf_k, **common, **staged))
    elif args.no_floors:
        configs.append(Config(0.0, 0.0, args.max_chunks, mode=args.mode, rrf_k=args.rrf_k,
                              **common, **staged))
    else:
        configs.append(Config(args.min_score, args.relative_floor, args.max_chunks,
                              mode=args.mode, rrf_k=args.rrf_k, **common, **staged))

    # Fetch at the widest candidate depth any config asks for, once per distinct fetch
    # key. Configs that differ only in filters share a fetch; configs that differ in mode
    # must not (see Config.fetch_key).
    max_fetch = max(cfg.fetch_k for cfg in configs)
    caches: dict[tuple, dict[str, list[dict]]] = {}
    for key in dict.fromkeys(cfg.fetch_key() for cfg in configs):
        mode, candidate_k, deal_scope, rrf_k, rerank, _ = key
        stages = "" if not rerank else " +rerank"
        print(f"\nFetching ranked chunks for {len(questions)} question(s) at topK={max_fetch} "
              f"mode={mode}{'' if rrf_k is None else f' rrf={rrf_k}'}"
              f"{'' if deal_scope else ' unscoped'}{stages}...", file=sys.stderr)
        try:
            # Fresh token per arm. Access tokens are short-lived and a mode sweep makes
            # one search call per question per arm, which outlives the TTL — an expiry
            # halfway through corrupts every arm after it.
            token = login(args.email, args.password)
            caches[key] = fetch_all(token, questions, max_fetch, mode=mode, rrf_k=rrf_k,
                                    candidate_k=candidate_k, deal_scope=deal_scope,
                                    rerank=rerank)
        except RuntimeError as ex:
            print(f"\n{ex}", file=sys.stderr)
            return 1

    all_results = []
    for cfg in configs:
        print(f"\nScoring at {cfg.label()}...", file=sys.stderr)
        result = evaluate(questions, caches[cfg.fetch_key()], cfg, verbose=args.verbose)
        summary = summarize(result)
        result["summary"] = summary
        all_results.append(result)
        print_summary(cfg.label(), summary)

    if args.sweep_mode or args.sweep_rerank:
        if args.sweep_rerank:
            print("\n\n  === rerank comparison, production filters held fixed ===")
            print("  The ceiling is small and known: at the shipped config only 5 of 80")
            print("  positive questions are fetched-then-out-ranked, all cross-document.")
            print("  Read x-doc and depth; R@final can move by at most 0.062.")
        else:
            print("\n\n  === mode comparison, production filters held fixed ===")
            print("  R@fetch is a CONTROL, not an outcome: on a deal-scoped haystack of ~17")
            print("  chunks it is saturated at 0.99 and cannot move. Read R@final and depth.")
        print(f"\n  {'config':<52}{'R@fetch':>9}{'R@final':>9}{'MRR':>7}"
              f"{'depth':>7}{'d<=k':>7}{'x-doc':>7}{'abstain':>9}")
        print("  " + "-" * 107)
        for result in all_results:
            sm = result["summary"]
            positives = [sm[n] for n in POSITIVE_SLICES if n in sm]
            if not positives:
                continue
            total = sum(x["n"] for x in positives)
            wavg = lambda key: sum(x[key] * x["n"] for x in positives) / total  # noqa: E731
            depths = [x["meanDepthToSatisfy"] for x in positives if x["meanDepthToSatisfy"]]
            depth = f"{statistics.mean(depths):.1f}" if depths else "-"
            xdoc = sm.get("cross-document", {}).get("recallFinal")
            abstain = sm.get("off-domain", {}).get("abstainRate")
            print(f"  {result['config']:<52}{wavg('recallFetched'):>9.3f}{wavg('recallFinal'):>9.3f}"
                  f"{wavg('mrr'):>7.3f}{depth:>7}{wavg('depthWithinK'):>7.2f}"
                  f"{'-' if xdoc is None else f'{xdoc:.2f}':>7}"
                  f"{'-' if abstain is None else f'{abstain:.2f}':>9}")

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
