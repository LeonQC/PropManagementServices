#!/usr/bin/env python3
"""Score Deal Q&A end to end with RAGAS: retrieval *and* generation.

This runs the same eval-questions.json that eval_retrieval.py uses, but through
the real /ai/v1/deals/{id}/ask endpoint, so the answers and contexts are the ones
the feature actually produces. It then scores them with RAGAS in two tiers:

  non-LLM   NonLLMContextRecall, NonLLMContextPrecisionWithReference
            String-similarity against the gold chunks. Free and deterministic.
            These should track eval_retrieval.py's recall@final closely — the
            cross-check below is the point of running them at all.

  LLM-judged  Faithfulness, ResponseRelevancy, FactualCorrectness
            The generation half, which no amount of retrieval measurement reaches.
            Costs money, and the scores move between runs.

Why RAGAS here and not for the retrieval metrics: use RAGAS where the metric needs
an LLM judge and the *prompt* is the hard part; write your own where the metric is
arithmetic. recall@k and MRR are ten lines and already standard vocabulary, so
eval_retrieval.py computes them directly. Faithfulness needs a tuned claim-
decomposition prompt, which is exactly what a library is worth paying for.

The judge is `judge-default` on the LiteLLM proxy, which resolves to OpenAI.
Deal Q&A generates on claude-sonnet-5 (AnthropicOptions.Model, called directly
via the Anthropic SDK) — so the judge is deliberately the *other* provider.
Judging a model's output with the same model or family measures self-preference
as much as faithfulness. Do not repoint judge-default at a Claude model.

The LLM-judged tier is opt-in. A bare run scores the free, deterministic metrics
only; `--llm-judge` adds the paid ones and prints its expected call volume first.
The default used to be the other way round, and a full run — 80 samples x 3
metrics x 3 passes, roughly 1,300 judge calls — was reachable without anything in
the invocation hinting at the bill.

Usage:
    scripts/.venv/bin/python scripts/eval_ragas.py                        # free
    scripts/.venv/bin/python scripts/eval_ragas.py --limit 5              # smoke test
    scripts/.venv/bin/python scripts/eval_ragas.py --llm-judge            # paid, 1 pass
    scripts/.venv/bin/python scripts/eval_ragas.py --llm-judge --repeats 3  # judge variance
"""
from __future__ import annotations

import argparse
import json
import os
import statistics
import sys
import urllib.error
import urllib.request
from pathlib import Path

# The deterministic harness is stdlib-only, so importing it here costs nothing
# and guarantees both scripts apply byte-identical filter logic. If they drifted,
# the cross-check below would compare two different context sets and quietly pass.
sys.path.insert(0, str(Path(__file__).resolve().parent))
import eval_retrieval  # noqa: E402

AUTH_URL = "http://localhost:5300"
AI_URL = "http://localhost:5700"
LITELLM_URL = "http://localhost:4000"

# Slices whose correct behaviour is an empty context. RAGAS metrics divide by the
# number of retrieved contexts, so these rows are degenerate here — they are
# scored by eval_retrieval.py's abstention rate instead, which is the right
# measurement for them anyway.
NEGATIVE_SLICES = {"unanswerable", "off-domain"}


# ---------- HTTP ----------

def _request(method: str, url: str, *, token: str | None = None, payload: dict | None = None,
             timeout: int = 120):
    body = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=body, method=method)
    if payload is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        text = resp.read()
        return json.loads(text) if text else None


def login(email: str, password: str) -> str:
    return _request("POST", f"{AUTH_URL}/auth/v1/login",
                    payload={"email": email, "password": password})["data"]["accessToken"]


def ask(token: str, deal_id: str, question: str) -> dict:
    return _request("POST", f"{AI_URL}/ai/v1/deals/{deal_id}/ask",
                    token=token, payload={"question": question})["data"]


# ---------- collecting answers ----------

def retrieved_context(token: str, question: str, deal_id: str) -> list[str]:
    """The chunk texts that actually reached the prompt, reconstructed.

    NOT the answer's citations. Citations are only the sources the model chose to
    reference — a subset of the context, and often a small one. Scoring those as
    `retrieved_contexts` measures which sources the model cited, not what
    retrieval delivered, and it produced a flat 0.000 on the first run of this
    harness: for "how much is the seller asking?", the offering memorandum was in
    the context but uncited, so the gold passage matched nothing.

    Re-running the same deterministic search + filter chain ai-service applies
    reproduces the real context set exactly, costs one embedding call, and keeps
    both harnesses scoring the same quantity — which is what makes the
    NonLLMContextRecall / recall@final cross-check meaningful at all."""
    chunks = eval_retrieval.search(token, question, deal_id, eval_retrieval.FETCH_TOP_K)
    return [c["text"] for c in eval_retrieval.apply_filters(chunks, eval_retrieval.Config())]


def gold_chunk_texts(token: str, question: dict) -> list[str]:
    """The real indexed chunk text for each gold reference.

    Why not the passage reconstructed from the PDF: edit distance punishes
    boundary misalignment, and a window cut from page text never lines up with
    where RecursiveCharacterTextSplitter actually split. A reconstructed passage
    scored 0.300 against a retrieval the deterministic harness scored 0.95 —
    the retrieval was right and the reference was merely differently shaped.
    Using the indexed chunk makes a correct retrieval score 1.0, so
    NonLLMContextRecall and recall@final finally measure the same event.

    This is not circular. The search is pinned to the gold documentId and the
    result is accepted only if it contains the gold anchor, so the ranker chooses
    nothing — it is being used to read the index, not to judge itself."""
    texts = []
    for gold in question.get("gold", []):
        if not gold.get("documentId"):
            continue
        payload = {"query": gold["snippet"], "dealId": question["dealId"],
                   "documentId": gold["documentId"], "topK": 20}
        try:
            chunks = _request("POST", f"{eval_retrieval.INGESTION_URL}/ingestion/v1/search",
                              token=token, payload=payload)["data"]["chunks"]
        except (urllib.error.HTTPError, urllib.error.URLError):
            chunks = []
        match = next((c["text"] for c in chunks if gold["snippet"] in c["text"]), None)
        # Fall back to the reconstructed passage when the anchor isn't in any
        # indexed chunk — rare, and better than dropping the reference entirely.
        texts.append(match or gold.get("passage") or gold["snippet"])
    return texts


def collect(token: str, questions: list[dict]) -> list[dict]:
    """Run every question through the real endpoint and keep what RAGAS needs.

    This is the expensive half — one full Deal Q&A call per question, billed to
    the generation model. Cache it to disk so repeated scoring runs (--repeats)
    re-score the same answers rather than regenerating them: judge variance is
    what we want to measure, not generator variance on top of it."""
    rows = []
    for i, q in enumerate(questions, start=1):
        try:
            answer = ask(token, q["dealId"], q["question"])
            contexts = retrieved_context(token, q["question"], q["dealId"])
            gold_texts = gold_chunk_texts(token, q)
        except urllib.error.HTTPError as ex:
            print(f"  {q['id']}: ask failed {ex.code} {ex.read().decode(errors='replace')[:120]}",
                  file=sys.stderr)
            continue
        except urllib.error.URLError as ex:
            print(f"  {q['id']}: ask failed {ex}", file=sys.stderr)
            continue

        rows.append({
            "id": q["id"],
            "slice": q["slice"],
            "question": q["question"],
            "answer": answer.get("answer", ""),
            "answeredFromDocuments": answer.get("answeredFromDocuments", False),
            "citedCount": len(answer.get("citations", [])),
            "contexts": contexts,
            "expectedAnswer": q.get("expectedAnswer"),
            # Passages, not the short label anchors. The NonLLM context metrics
            # score by edit distance, so comparing an 8-character label against a
            # 300-token chunk returns ~0 however good the retrieval was — the
            # first run of this harness scored a flat 0.000 for exactly that
            # reason. build_eval_set.py records both forms per gold reference.
            "goldPassages": gold_texts,
        })
        if i % 10 == 0:
            print(f"  asked {i}/{len(questions)}", file=sys.stderr)
    return rows


def to_dataset(rows: list[dict]):
    """Map our records onto RAGAS's sample schema.

    The field names are RAGAS's, not ours — user_input/retrieved_contexts/response/
    reference/reference_contexts. Everything here already exists in the question
    set; nothing had to be generated specially for RAGAS, which is the payoff of
    having built the labelled set first."""
    from ragas import EvaluationDataset
    from ragas.dataset_schema import SingleTurnSample

    samples = []
    for r in rows:
        if r["slice"] in NEGATIVE_SLICES:
            continue  # empty context by design — see NEGATIVE_SLICES
        if not r["contexts"]:
            continue  # nothing retrieved; RAGAS would divide by zero
        samples.append(SingleTurnSample(
            user_input=r["question"],
            retrieved_contexts=r["contexts"],
            response=r["answer"],
            reference=r["expectedAnswer"] or "",
            reference_contexts=r["goldPassages"],
        ))
    return EvaluationDataset(samples=samples)


def build_metrics(use_llm: bool, judge_model: str):
    """Two tiers. The non-LLM pair is the cross-check against eval_retrieval.py."""
    from ragas.metrics import (
        NonLLMContextRecall,
        NonLLMContextPrecisionWithReference,
    )

    metrics = [NonLLMContextRecall(), NonLLMContextPrecisionWithReference()]
    evaluator_llm = None

    if use_llm:
        from langchain_openai import ChatOpenAI
        from ragas.llms import LangchainLLMWrapper
        from ragas.metrics import Faithfulness, ResponseRelevancy
        from ragas.embeddings import LangchainEmbeddingsWrapper
        from langchain_openai import OpenAIEmbeddings

        # Pointed at the LiteLLM proxy, not at a provider. `judge-default` is the
        # route name; the proxy decides what serves it. api_key is required by the
        # client but unused — the proxy holds the real credential.
        judge = ChatOpenAI(model=judge_model, base_url=f"{LITELLM_URL}/v1",
                           api_key="proxy-holds-the-key", temperature=0)
        evaluator_llm = LangchainLLMWrapper(judge)
        # ResponseRelevancy needs embeddings as well as an LLM.
        evaluator_embeddings = LangchainEmbeddingsWrapper(
            OpenAIEmbeddings(model="embed-openai", base_url=f"{LITELLM_URL}/v1",
                             api_key="proxy-holds-the-key"))

        # FactualCorrectness is deliberately absent. It decomposes `reference`
        # into claims and scores claim overlap, but our ground truth is a bare
        # value ("$67,834,000"), not a reference answer — there is nothing to
        # decompose, and it scored a flat 0.000 for that reason rather than
        # because the answers were wrong. Value correctness here is exact-match
        # arithmetic, so answer_contains_expected() below computes it directly:
        # same principle that keeps recall@k out of RAGAS.
        metrics += [
            Faithfulness(llm=evaluator_llm),
            ResponseRelevancy(llm=evaluator_llm, embeddings=evaluator_embeddings),
        ]

    return metrics, evaluator_llm


def answer_contains_expected(rows: list[dict]) -> tuple[float, int]:
    """Share of answerable questions whose answer states the expected value.

    Deterministic and free. Compares digits only, so "$67,834,000" matches
    "67,834,000" and "$67.83 million" fails — deliberately strict, since the
    point is whether the exact figure from the document reached the user."""
    graded = [r for r in rows
              if r["slice"] not in NEGATIVE_SLICES and r.get("expectedAnswer")]
    if not graded:
        return 0.0, 0
    hits = 0
    for r in graded:
        actual = "".join(ch for ch in r["answer"] if ch.isdigit())
        # cross-document expectations are composite ("OM: 76.80%; Rent_Roll: 99.2%")
        # because the answer *is* the comparison. Digit-flattening the whole string
        # produces one long number that appears in nothing — it scored 0/20 for
        # that reason, not because the answers were wrong. Require every value.
        parts = str(r["expectedAnswer"]).split("; ") if r["slice"] == "cross-document" \
            else [str(r["expectedAnswer"])]
        wanted = ["".join(ch for ch in p.split(": ", 1)[-1] if ch.isdigit()) for p in parts]
        if wanted and all(w and w in actual for w in wanted):
            hits += 1
    return hits / len(graded), len(graded)


# ---------- CLI ----------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--questions", type=Path, default=Path("scripts/eval-questions.json"))
    ap.add_argument("--answers", type=Path, default=Path("scripts/eval-answers.json"),
                    help="cache of generated answers; reused by --repeats")
    # Default output is tier-specific. With one shared filename, a free run
    # silently overwrites the judged results — which is backwards: the cheap,
    # repeatable artifact clobbers the expensive one that cost ~1,300 judge calls
    # to produce. An explicit --out still wins.
    ap.add_argument("--out", type=Path, default=None,
                    help="default: eval-ragas-results.json with --llm-judge, "
                         "eval-ragas-results-free.json without")
    ap.add_argument("--email", default="admin@proptrack.local")
    ap.add_argument("--password", default="ChangeMe123!")
    ap.add_argument("--judge-model", default="judge-default",
                    help="LiteLLM route name. Must not resolve to the generation model's family.")
    ap.add_argument("--limit", type=int, help="only run the first N questions (smoke test)")
    ap.add_argument("--repeats", type=int, default=1,
                    help="score the same answers N times to measure judge variance. "
                         "Only meaningful with --llm-judge: the non-LLM metrics are "
                         "deterministic and measured 0.000 spread across three passes.")
    # The paid tier is opt-in. A default run should never be able to surprise
    # someone with an API bill — the measured full run was 80 samples x 3 metrics
    # x 3 passes, roughly 1,300 judge calls, and nothing in the invocation said so.
    ap.add_argument("--llm-judge", action="store_true",
                    help="ALSO run the LLM-judged metrics (Faithfulness, "
                         "ResponseRelevancy, FactualCorrectness). Costs real API spend.")
    ap.add_argument("--no-llm", action="store_true",
                    help=argparse.SUPPRESS)  # accepted as a no-op; free is now the default
    ap.add_argument("--regenerate", action="store_true", help="ignore the answer cache")
    args = ap.parse_args()

    if not args.questions.exists():
        print(f"No question set at {args.questions}. Run build_eval_set.py first.", file=sys.stderr)
        return 1

    questions = json.loads(args.questions.read_text())
    if args.limit:
        questions = questions[:args.limit]

    # Generation is the expensive half; cache it so --repeats measures judge
    # variance rather than paying for fresh answers on every scoring pass.
    if args.answers.exists() and not args.regenerate:
        rows = json.loads(args.answers.read_text())
        known = {q["id"] for q in questions}
        rows = [r for r in rows if r["id"] in known]
        print(f"Reusing {len(rows)} cached answer(s) from {args.answers} "
              f"(--regenerate to refresh)", file=sys.stderr)
    else:
        try:
            token = login(args.email, args.password)
        except urllib.error.URLError as ex:
            print(f"Could not reach auth-service at {AUTH_URL}: {ex}\n"
                  f"Is the stack up? `docker compose up -d`", file=sys.stderr)
            return 1
        print(f"Asking {len(questions)} question(s) through /ai/v1/deals/.../ask...", file=sys.stderr)
        rows = collect(token, questions)
        args.answers.write_text(json.dumps(rows, indent=2) + "\n")
        print(f"Cached {len(rows)} answer(s) to {args.answers}", file=sys.stderr)

    if not rows:
        print("No answers collected; nothing to score.", file=sys.stderr)
        return 1

    try:
        from ragas import evaluate
    except ImportError:
        print("ragas is not installed. This script needs its own venv:\n"
              "  python3 -m venv scripts/.venv\n"
              "  scripts/.venv/bin/pip install -r scripts/requirements-eval.txt",
              file=sys.stderr)
        return 1

    dataset = to_dataset(rows)
    skipped = len(rows) - len(dataset)
    print(f"\nScoring {len(dataset)} sample(s); {skipped} skipped "
          f"(negative slices and empty contexts — see eval_retrieval.py abstention rates)",
          file=sys.stderr)

    if args.no_llm and args.llm_judge:
        print("--no-llm and --llm-judge contradict each other.", file=sys.stderr)
        return 1

    if args.out is None:
        args.out = Path("scripts/eval-ragas-results.json") if args.llm_judge \
            else Path("scripts/eval-ragas-results-free.json")

    if args.llm_judge:
        # Rough, but the right order of magnitude: each metric makes several calls
        # per sample (Faithfulness decomposes the answer into claims then verifies
        # each; FactualCorrectness decomposes twice). Say it before spending it.
        calls = len(dataset) * 6 * args.repeats
        print(f"\n  --llm-judge: ~{calls:,} judge calls "
              f"({len(dataset)} samples x ~6 x {args.repeats} pass(es)) against "
              f"'{args.judge_model}'.\n  This costs real API spend.", file=sys.stderr)
    else:
        print("\n  Free tier only (non-LLM metrics). Add --llm-judge for "
              "Faithfulness / ResponseRelevancy / FactualCorrectness.", file=sys.stderr)
        if args.repeats > 1:
            # Worth saying rather than silently obeying: the non-LLM metrics moved
            # 0.000 across three passes, so repeats here buy identical numbers.
            print(f"  Note: --repeats {args.repeats} has no effect without "
                  f"--llm-judge; these metrics are deterministic.", file=sys.stderr)
            args.repeats = 1

    metrics, _ = build_metrics(use_llm=args.llm_judge, judge_model=args.judge_model)

    runs = []
    for run in range(1, args.repeats + 1):
        print(f"\n--- scoring pass {run}/{args.repeats} ---", file=sys.stderr)
        result = evaluate(dataset=dataset, metrics=metrics)
        scores = {k: float(v) for k, v in result._repr_dict.items()} if hasattr(result, "_repr_dict") \
            else {k: float(v) for k, v in dict(result).items()}
        runs.append(scores)
        for name, value in scores.items():
            print(f"  {name:<38}{value:.3f}")

    # Judge scores wander between runs, and on this stack they cannot be pinned:
    # the generation model rejects `temperature` outright, and the proxy drops the
    # parameter rather than erroring. Reporting the spread is therefore not
    # optional rigour, it is the only honest way to quote a judged number.
    if args.repeats > 1:
        print(f"\n  === across {args.repeats} passes (mean +/- spread) ===")
        for name in runs[0]:
            values = [r[name] for r in runs]
            spread = max(values) - min(values)
            print(f"  {name:<38}{statistics.mean(values):.3f}  +/- {spread:.3f}")

    exact, graded = answer_contains_expected(rows)
    print(f"\n  answer states the expected value: {exact:.3f}  (n={graded}, deterministic)")

    payload = {"tier": "llm-judged" if args.llm_judge else "free",
               "judgeModel": args.judge_model if args.llm_judge else None,
               "sampleCount": len(dataset),
               "skipped": skipped, "answerContainsExpected": exact,
               "answerContainsExpectedN": graded, "runs": runs}
    args.out.write_text(json.dumps(payload, indent=2) + "\n")
    print(f"\nWrote results to {args.out}", file=sys.stderr)

    print("\nCross-check: NonLLMContextRecall should track eval_retrieval.py's "
          "recall@final closely.\nA large gap means one of the two implementations is "
          "wrong — investigate before\ntrusting any judged metric.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
