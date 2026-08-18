#!/usr/bin/env python3
"""Build the labeled retrieval-eval question set from the on-disk CRE corpus.

Nothing here is invented. The corpus PDFs are templated: every document carries a
summary block of `label` / `value` line pairs, and this script reads the values
straight out of the PDFs. The *document* is the authority on the right answer —
not listings-service — because Deal Q&A answers from documents, so that is what
"correct" has to mean.

Two stages, deliberately separable:

  1. Parse the corpus into facts        -- offline, needs only the PDFs
  2. Join facts to deals via the manifest -- needs seed_deal_documents.py to have run

Stage 1 runs with --plan and no stack, so the questions can be reviewed before
anything is seeded. Stage 2 attaches the dealId/documentId that the harness
needs to actually query.

Five slices, because they fail differently and one average would hide all of it:

  single-fact     one figure, one document
  table-lookup    a per-tenant row from a rent roll -- dense retrieval on tables
  cross-document  a figure the documents *disagree* about; needs BOTH chunks
  unanswerable    in-domain, genuinely absent -- the service should decline
  off-domain      nonsense -- MinScore should reject it before any model call

Usage:
    python3 scripts/build_eval_set.py --plan            # review questions, no write
    python3 scripts/build_eval_set.py                   # write eval-questions.json
    python3 scripts/build_eval_set.py --total 60        # a smaller set

Requires pypdf (`pip install pypdf`). This is the only eval script with a
dependency — eval_retrieval.py is deliberately stdlib-only so the harness runs
anywhere. This one is a build step whose output is committed, so it runs rarely.
"""
from __future__ import annotations

import argparse
import json
import random
import re
import sys
from collections import defaultdict
from pathlib import Path

DEFAULT_CORPUS = Path("/Users/fengzhu/Projects/PropTrack/proptrack_documents")

# Every corpus filename ends in a 6-hex property id. Documents sharing it describe
# the same asset, which is what lets cross-document questions be built at all.
PROPERTY_KEY = re.compile(r"_([0-9a-f]{6})\.pdf$")

# ── Which labels make usable questions ───────────────────────────────────────
#
# Measured over all 521 PDFs, every label below resolves in 100% of its documents
# and appears exactly once, so extraction is unambiguous.
#
# Labels are excluded when their value barely varies across the corpus — a question
# whose answer is the same for every property can be answered from priors or from
# the wrong property's document, and would score as a retrieval success while
# proving nothing. Distinct-value ratios measured over the corpus:
#
#   Standard (Phase I)        1/43   Client (Phase I)        1/43
#   Policy type (Title)       1/43   Accompanied by (Site)   1/62
#   Exclusivity period (LOI)  1/62   Overall condition (Site) 3/62
#   Title company             4/43   Consulting firm         4/43
#   Appraiser                 4/43   Conducted by (Site)    12/62
#   Property type (OM)       19/85
#
# All of the above are dropped. Everything kept below scores >= 0.37.
#
# Question text deliberately paraphrases the label rather than echoing it ("WALT"
# -> "weighted average lease term"), so the eval isn't just measuring literal
# string overlap between query and chunk.
FACT_LABELS: dict[str, list[tuple[str, str]]] = {
    "Offering_Memorandum": [
        ("Cap rate", "What capitalization rate is this asset being offered at?"),
        ("NOI", "What is the net operating income?"),
        ("Asking price", "How much is the seller asking for this property?"),
        ("Total SF", "What is the total square footage of the building?"),
        ("Leasable SF", "How much leasable area does the property have?"),
        ("Price per SF", "What does the asking price work out to per square foot?"),
        ("Market benchmark", "What is the prevailing market cap rate for comparable assets?"),
        ("Year built", "When was this building constructed?"),
    ],
    "Appraisal": [
        ("Concluded Market Value", "What market value did the appraiser conclude?"),
        ("Effective gross income", "What effective gross income did the appraisal use?"),
        ("Operating expenses", "What operating expense figure appears in the appraisal?"),
        ("Potential gross income", "What is the potential gross income before vacancy?"),
        ("Capitalization rate used", "What cap rate did the appraiser apply in the income approach?"),
        ("Effective date", "As of what date was the appraisal effective?"),
    ],
    "Rent_Roll": [
        ("Occupied SF", "How much space is currently leased?"),
        ("Vacant SF", "How much vacant space is there?"),
        ("Total annual base rent", "What is the total annual base rent across all tenants?"),
        ("Avg rent per SF", "What is the average rent per square foot?"),
        ("WALT", "What is the weighted average lease term remaining?"),
    ],
    "LOI": [
        ("Purchase price", "What price is the buyer offering in the letter of intent?"),
        ("Implied cap rate", "What cap rate does the buyer's offer imply?"),
        ("Earnest money deposit", "How much earnest money is the buyer putting up?"),
        ("Due diligence period", "How long is the due diligence period?"),
        ("Closing period", "How long after the PSA is closing scheduled?"),
    ],
    "Phase_I_ESA": [
        ("Report date", "What date does the environmental report carry?"),
    ],
    "Title_Report": [
        ("Order number", "What is the title order number?"),
        ("APN", "What is the assessor's parcel number?"),
        ("Vesting", "In whose name is title currently vested?"),
    ],
    "Site_Visit_Report": [
        ("Visit date", "When was the site visit conducted?"),
    ],
}

# ── Cross-document pairs ─────────────────────────────────────────────────────
#
# Each pair is a figure that two documents report *differently*. Measured over the
# corpus: OM-vs-rent-roll occupancy differs in 85/85 properties, OM-vs-LOI price in
# 62/62, OM-vs-LOI cap rate in 61/62. Pairs that always agree (OM vs appraisal NOI,
# OM vs rent roll leasable SF) are excluded — either chunk alone would answer them,
# so they'd test nothing that single-fact doesn't.
#
# These are the questions that need BOTH chunks in context, which makes them the
# slice most exposed to RelativeFloor dropping the weaker of two good hits.
CROSS_DOC_PAIRS = [
    (
        ("Offering_Memorandum", "Occupancy"),
        ("Rent_Roll", "Occupancy rate"),
        "What occupancy does this deal report, and do the documents agree?",
    ),
    (
        ("Offering_Memorandum", "Asking price"),
        ("LOI", "Purchase price"),
        "How does the buyer's offer compare to the asking price?",
    ),
    (
        ("Offering_Memorandum", "Cap rate"),
        ("LOI", "Implied cap rate"),
        "How does the cap rate implied by the offer compare to the one in the offering memorandum?",
    ),
]

# ── Hand-written negative slices ─────────────────────────────────────────────
#
# In-domain and plausible, but genuinely absent from the corpus. Correct behavior
# is to decline. These exist because no score threshold can catch them: RetrievalOptions
# documents that this band scores 0.29-0.32, inside the on-topic range, so the floor
# cannot separate them from answerable questions. Only the prompt can.
UNANSWERABLE = [
    "What is the tenant's dog policy for the rooftop pool?",
    "How many electric vehicle charging stations are in the parking garage?",
    "What is the seismic retrofit schedule for this building?",
    "Which broker earned the highest commission on this deal last quarter?",
    "What did the fire marshal note in the most recent inspection?",
    "How much did the property pay in stormwater utility fees last year?",
    "What is the current status of the elevator modernization permit?",
    "Which insurance carrier writes the property's flood coverage?",
    "What is the union labor agreement for on-site janitorial staff?",
    "How many parking spaces are reserved for handicapped access?",
]

# Nothing to do with commercial real estate. MinScore should reject these outright,
# so nothing reaches the model. Measured off-domain top hits were 0.07-0.08 against
# a floor of 0.15.
OFF_DOMAIN = [
    "What is the best recipe for sourdough bread?",
    "How do I change the oil in a 2015 Honda Civic?",
    "Who won the World Cup in 1998?",
    "What is the capital of Mongolia?",
    "How do I train a puppy to stop barking?",
    "What are the side effects of ibuprofen?",
    "Explain how a transformer neural network works.",
    "What is the best hiking trail near Portland?",
    "How long should I boil an egg for a soft yolk?",
    "What year did the Berlin Wall fall?",
]

SLICE_SHARE = {"single-fact": 0.50, "table-lookup": 0.25, "cross-document": 0.25}


# ── Stage 1: parse the corpus ────────────────────────────────────────────────

def read_pages(path: Path) -> list[list[str]]:
    """Text of each page, as stripped lines. Page index is 1-based to match citations."""
    import pypdf

    pages = []
    for page in pypdf.PdfReader(str(path)).pages:
        text = page.extract_text() or ""
        pages.append([line.strip() for line in text.split("\n")])
    return pages


def doc_type_of(name: str) -> str | None:
    # Longest-first so "Offering_Memorandum" wins over any shorter prefix that
    # happens to be a substring of it.
    for doc_type in sorted(set(FACT_LABELS) | {"NDA", "IC_Memorandum", "Loan_Term_Sheet"}, key=len, reverse=True):
        if name.startswith(doc_type + "_"):
            return doc_type
    return None


def parse_corpus(corpus: Path) -> tuple[dict, dict]:
    """Extract every labeled fact and every rent-roll tenant row from the PDFs.

    Returns (facts, tenant_rows):
      facts[(property_key, doc_type)][label] = (value, page_no, passage)
      tenant_rows[property_key] = [(suite, sqft, annual_rent, page_no, passage), ...]

    `passage` is a window of surrounding page text, not just the label. Two
    consumers need two different things from a gold reference and both are
    recorded: eval_retrieval.py matches by *substring* (does the retrieved chunk
    contain "Cap rate"?), while RAGAS's NonLLM context metrics score by *edit
    distance* against a reference passage. Give the latter a bare label and every
    score is ~0 — an 8-character label compared against a 300-token chunk is
    almost entirely dissimilar no matter how right the retrieval was.
    """
    facts: dict = defaultdict(dict)
    tenant_rows: dict = defaultdict(list)

    for path in sorted(corpus.glob("*.pdf")):
        match = PROPERTY_KEY.search(path.name)
        doc_type = doc_type_of(path.name)
        if not match or doc_type is None:
            continue
        key = match.group(1)

        wanted = {label for label, _ in FACT_LABELS.get(doc_type, [])}
        # Cross-document labels aren't all in FACT_LABELS (Occupancy is a cross-doc
        # label only), so collect those too.
        for (a_type, a_label), (b_type, b_label), _ in CROSS_DOC_PAIRS:
            if doc_type == a_type:
                wanted.add(a_label)
            if doc_type == b_type:
                wanted.add(b_label)
        if not wanted and doc_type != "Rent_Roll":
            continue

        for page_no, lines in enumerate(read_pages(path), start=1):
            for i, line in enumerate(lines):
                if line in wanted and i + 1 < len(lines):
                    facts[(key, doc_type)].setdefault(
                        line, (lines[i + 1], page_no, passage_around(lines, i)))
            if doc_type == "Rent_Roll":
                tenant_rows[key].extend(parse_tenant_rows(lines, page_no))

    return facts, tenant_rows


# Sized to a real chunk, ~300 tokens of page text (chunking.py), which lands
# around 900 characters.
#
# The length has to match, not just the content. Normalized Levenshtein
# similarity is bounded above by the ratio of the two string lengths, so a
# 300-character reference scored against an 870-character chunk cannot exceed
# ~0.34 — below the metric's threshold even when the chunk contains the answer
# verbatim. Sizing the reference to the citation snippet instead of to the chunk
# is what made every NonLLM score read 0.000.
PASSAGE_CHARS = 900


def passage_around(lines: list[str], index: int) -> str:
    """The label plus its surrounding block, approximating the chunk it lives in.

    Newline-joined because chunks are split from page text and keep its line
    structure; starts two lines early so the passage carries the section heading
    where there is one, which the real chunk also contains."""
    window = lines[max(0, index - 2): index + 20]
    return "\n".join(line for line in window if line)[:PASSAGE_CHARS]


def parse_tenant_rows(lines: list[str], page_no: int) -> list[tuple[str, str, str, int]]:
    """Pull (suite, sqft, annual rent) triples out of a rent-roll table.

    The PDF text layer flattens table columns into one line each, so a row arrives as
    `Suite / 120 / 43,289 / NNN / $862,173 / ...`. Anchor on the literal "Suite"
    followed by a 3-digit number, then take the first size-like and first money-like
    value after it. Rows that don't yield both are skipped rather than guessed at —
    62 of 85 rent rolls parse cleanly, which is ample for this slice.
    """
    rows = []
    for i, line in enumerate(lines):
        if line != "Suite" or i + 1 >= len(lines) or not re.fullmatch(r"\d{3}", lines[i + 1]):
            continue
        sqft = rent = None
        for j in range(i + 2, min(i + 9, len(lines))):
            if sqft is None and re.fullmatch(r"[\d,]{3,}", lines[j]):
                sqft = lines[j]
            elif sqft is not None and re.fullmatch(r"\$[\d,]+", lines[j]):
                rent = lines[j]
                break
        if sqft and rent:
            rows.append((lines[i + 1], sqft, rent, page_no, passage_around(lines, i)))
    return rows


# ── Stage 2: turn facts into questions ───────────────────────────────────────

def load_manifest(path: Path) -> dict:
    """fileName -> manifest row, from seed_deal_documents.py output."""
    if not path.exists():
        return {}
    return {row["fileName"]: row for row in json.loads(path.read_text())}


def find_document(manifest: dict, key: str, doc_type: str) -> dict | None:
    """The seeded document record for one property's document of a given type."""
    for name, row in manifest.items():
        if name.startswith(doc_type + "_") and PROPERTY_KEY.search(name) and PROPERTY_KEY.search(name).group(1) == key:
            return row
    return None


def build_questions(facts, tenant_rows, manifest, total, rng) -> list[dict]:
    """One question per property where possible, so no property dominates the score."""
    questions: list[dict] = []
    auto_budget = total - len(UNANSWERABLE) - len(OFF_DOMAIN)

    # Candidate pools, each already shuffled so the sample is spread across properties.
    single, table, cross = [], [], []

    for (key, doc_type), labels in facts.items():
        for label, question in FACT_LABELS.get(doc_type, []):
            if label in labels:
                value, page, passage = labels[label]
                single.append((key, doc_type, label, question, value, page, passage))

    for key, rows in tenant_rows.items():
        for suite, sqft, rent, page, passage in rows:
            table.append((key, suite, sqft, rent, page, passage))

    for (a_type, a_label), (b_type, b_label), question in CROSS_DOC_PAIRS:
        for (key, doc_type), labels in facts.items():
            if doc_type != a_type or a_label not in labels:
                continue
            other = facts.get((key, b_type), {})
            if b_label not in other:
                continue
            a_val, a_page, a_passage = labels[a_label]
            b_val, b_page, b_passage = other[b_label]
            if _norm(a_val) == _norm(b_val):
                continue  # documents agree; either chunk alone would answer it
            cross.append((key, question, (a_type, a_label, a_val, a_page, a_passage),
                         (b_type, b_label, b_val, b_page, b_passage)))

    for pool in (single, table, cross):
        rng.shuffle(pool)

    # Spread the budget across slices, then across properties within each slice.
    #
    # Round-robin rather than a hard one-question-per-property cap: only properties
    # whose documents were actually seeded can be used, and that pool is much smaller
    # than the corpus (17 of 84 when this was written). A hard cap would silently
    # truncate the set to the number of seeded properties. Cycling takes a first
    # question from every property before any property gets a second, so the set stays
    # balanced at whatever size the seeded pool allows.
    def take(pool, slice_name, n, make):
        by_property: dict[str, list] = defaultdict(list)
        for item in pool:
            by_property[item[0]].append(item)

        keys = sorted(by_property)
        rng.shuffle(keys)
        made = 0
        while made < n:
            progressed = False
            for key in keys:
                if made >= n:
                    break
                if not by_property[key]:
                    continue
                progressed = True
                record = make(by_property[key].pop(0))
                if record is None:
                    continue  # property not seeded, or document missing from manifest
                questions.append(record)
                made += 1
            if not progressed:
                break  # every pool exhausted
        return made

    take(single, "single-fact", round(auto_budget * SLICE_SHARE["single-fact"]),
         lambda it: _single_record(manifest, *it))
    take(table, "table-lookup", round(auto_budget * SLICE_SHARE["table-lookup"]),
         lambda it: _table_record(manifest, *it))
    take(cross, "cross-document", round(auto_budget * SLICE_SHARE["cross-document"]),
         lambda it: _cross_record(manifest, *it))

    # Negatives need a real deal to query against, but their content is deal-independent.
    deal_ids = sorted({row["dealId"] for row in manifest.values()}) if manifest else []
    for i, text in enumerate(UNANSWERABLE):
        questions.append(_negative_record("unanswerable", i, text, deal_ids, rng))
    for i, text in enumerate(OFF_DOMAIN):
        questions.append(_negative_record("off-domain", i, text, deal_ids, rng))

    return questions


def _norm(value: str) -> str:
    """Compare figures ignoring trailing-zero and percent formatting (63.90% vs 63.9%)."""
    return value.replace(",", "").rstrip("%").rstrip("0").rstrip(".")


def _single_record(manifest, key, doc_type, label, question, value, page, passage):
    doc = find_document(manifest, key, doc_type) if manifest else None
    if manifest and doc is None:
        return None
    return {
        "id": f"single-{doc_type.lower()}-{label.lower().replace(' ', '-')}-{key}",
        "slice": "single-fact",
        "propertyKey": key,
        "dealId": doc["dealId"] if doc else None,
        "question": question,
        "expectedAnswer": value,
        "gold": [{
            "documentId": doc["documentId"] if doc else None,
            "fileName": doc["fileName"] if doc else f"{doc_type}_*_{key}.pdf",
            "page": page,
            # Anchor for substring matching (eval_retrieval.py).
            "snippet": label,
            # Reference passage for edit-distance scoring (eval_ragas.py).
            "passage": passage,
        }],
    }


def _table_record(manifest, key, suite, sqft, rent, page, passage):
    doc = find_document(manifest, key, "Rent_Roll") if manifest else None
    if manifest and doc is None:
        return None
    return {
        "id": f"table-suite{suite}-{key}",
        "slice": "table-lookup",
        "propertyKey": key,
        "dealId": doc["dealId"] if doc else None,
        "question": f"What is the annual base rent for the tenant in suite {suite}?",
        "expectedAnswer": rent,
        "gold": [{
            "documentId": doc["documentId"] if doc else None,
            "fileName": doc["fileName"] if doc else f"Rent_Roll_*_{key}.pdf",
            "page": page,
            "snippet": suite,
            "passage": passage,
        }],
    }


def _cross_record(manifest, key, question, a, b):
    a_type, a_label, a_val, a_page, a_passage = a
    b_type, b_label, b_val, b_page, b_passage = b
    a_doc = find_document(manifest, key, a_type) if manifest else None
    b_doc = find_document(manifest, key, b_type) if manifest else None
    if manifest and (a_doc is None or b_doc is None):
        return None
    return {
        "id": f"cross-{a_label.lower().replace(' ', '-')}-{key}",
        "slice": "cross-document",
        "propertyKey": key,
        "dealId": a_doc["dealId"] if a_doc else None,
        "question": question,
        "expectedAnswer": f"{a_type}: {a_val}; {b_type}: {b_val}",
        # Both are required. The harness scores this slice with strict set-recall:
        # retrieving one of the two is a failure, because the answer is the comparison.
        "requiresAll": True,
        "gold": [
            {
                "documentId": a_doc["documentId"] if a_doc else None,
                "fileName": a_doc["fileName"] if a_doc else f"{a_type}_*_{key}.pdf",
                "page": a_page,
                "snippet": a_label,
                "passage": a_passage,
            },
            {
                "documentId": b_doc["documentId"] if b_doc else None,
                "fileName": b_doc["fileName"] if b_doc else f"{b_type}_*_{key}.pdf",
                "page": b_page,
                "snippet": b_label,
                "passage": b_passage,
            },
        ],
    }


def _negative_record(slice_name, i, text, deal_ids, rng):
    return {
        "id": f"{slice_name}-{i:02d}",
        "slice": slice_name,
        "propertyKey": None,
        "dealId": rng.choice(deal_ids) if deal_ids else None,
        "question": text,
        "expectedAnswer": None,
        # No gold chunks: the correct outcome is that nothing survives retrieval.
        "gold": [],
    }


# ── CLI ──────────────────────────────────────────────────────────────────────

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--corpus", type=Path, default=DEFAULT_CORPUS)
    ap.add_argument("--manifest", type=Path, default=Path("seed-manifest.json"))
    ap.add_argument("--out", type=Path, default=Path("scripts/eval-questions.json"))
    ap.add_argument("--total", type=int, default=100, help="target question count")
    ap.add_argument("--seed", type=int, default=20260816, help="fixed so regeneration is stable")
    ap.add_argument("--plan", action="store_true", help="print questions, write nothing")
    args = ap.parse_args()

    if not args.corpus.is_dir():
        print(f"Corpus not found: {args.corpus}", file=sys.stderr)
        return 1

    manifest = load_manifest(args.manifest)
    if not manifest:
        if not args.plan:
            print(f"No manifest at {args.manifest}. Run seed_deal_documents.py first, "
                  f"or use --plan to preview questions without deal/document ids.", file=sys.stderr)
            return 1
        print(f"[--plan] No manifest at {args.manifest}; dealId/documentId will be null.\n")

    print(f"Parsing {args.corpus}...", file=sys.stderr)
    facts, tenant_rows = parse_corpus(args.corpus)
    print(f"  {len(facts)} (property, document) pairs; "
          f"{sum(len(v) for v in tenant_rows.values())} tenant rows\n", file=sys.stderr)

    questions = build_questions(facts, tenant_rows, manifest, args.total, random.Random(args.seed))

    by_slice: dict[str, int] = defaultdict(int)
    for q in questions:
        by_slice[q["slice"]] += 1

    if args.plan:
        for q in questions:
            gold = ", ".join(f"{g['fileName']} p{g['page']} ~{g['snippet']!r}" for g in q["gold"]) or "(none expected)"
            print(f"[{q['slice']:<14}] {q['question']}")
            print(f"{'':17}  answer: {q['expectedAnswer']}")
            print(f"{'':17}  gold:   {gold}\n")

    print("Slice counts:", file=sys.stderr)
    for name in ("single-fact", "table-lookup", "cross-document", "unanswerable", "off-domain"):
        print(f"  {name:<16} {by_slice.get(name, 0)}", file=sys.stderr)
    print(f"  {'TOTAL':<16} {len(questions)}", file=sys.stderr)

    if not args.plan:
        args.out.write_text(json.dumps(questions, indent=2) + "\n")
        print(f"\nWrote {len(questions)} questions to {args.out}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
