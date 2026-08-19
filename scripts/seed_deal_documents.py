#!/usr/bin/env python3
"""Seed deal documents from the on-disk CRE corpus, through the real upload flow.

Nothing here is generated. The corpus at --corpus holds pre-built PDFs named
`<DocType>_<Property_Name>_<hash>.pdf`, one set per property, and every figure in
them was derived from that property's record in listings-service. Documents are
matched to deals on `deal.propertyName`, which is the snapshot of the same
property — so a deal only ever receives documents about its own asset.

That matching is the whole point. The corpus that was in the system before this
script attached documents to deals at random: the Columbus Retail Plaza deal
carried an IC memo for an Austin medical office building. Retrieval worked
perfectly and every answer was about the wrong property, with a correct-looking
page citation. Matching on property name is what makes grounding mean anything.

Each document goes through the same five steps the UI performs — no direct
database writes, so deal.document_uploaded is published and ingestion-service
parses, chunks, and embeds exactly as it would in production:

  1. POST /auth/v1/login                       -> bearer token
  2. POST /documents/v1/upload-url             -> presigned PUT + pending record
  3. PUT  <presigned url>                      -> bytes into MinIO
  4. POST /documents/v1/confirm                -> record goes active
  5. POST /deals/v1/deals/{dealId}/documents   -> links it, publishes the event

A manifest of every (deal, document, file) triple is written to --manifest so
retrieval answers can later be checked against known ground truth rather than
eyeballed.

Usage:
    python3 scripts/seed_deal_documents.py --plan          # show what would be seeded
    python3 scripts/seed_deal_documents.py --wipe          # clear existing docs first
    python3 scripts/seed_deal_documents.py --max-deals 6   # a smaller slice

--wipe is destructive and dev-only: it truncates document_chunks, ingestion_runs,
deal_documents and document_records and empties the storage bucket.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

AUTH_URL = "http://localhost:5300"
DOCS_URL = "http://localhost:5400"
DEALS_URL = "http://localhost:5200"
INGESTION_URL = "http://localhost:5500"

DEFAULT_CORPUS = Path("/Users/fengzhu/Projects/PropTrack/proptrack_documents")

# Filename prefix -> the fileType vocabulary the UI offers (DocumentsPanel.tsx).
# Types with no counterpart in that list ride under "Other" rather than inventing
# vocabulary the UI can't render.
DOC_TYPE_MAP = {
    "Offering_Memorandum": "OfferingMemorandum",
    "IC_Memorandum": "OfferingMemorandum",
    "Rent_Roll": "RentRoll",
    "LOI": "LetterOfIntent",
    "Phase_I_ESA": "PhaseIReport",
    "Appraisal": "Appraisal",
    "Loan_Term_Sheet": "Other",
    "Site_Visit_Report": "Other",
    "Title_Report": "Other",
    "NDA": "Other",
}

# Longest-first so "Offering_Memorandum" wins over any shorter prefix.
DOC_TYPES = sorted(DOC_TYPE_MAP, key=len, reverse=True)

# Deals whose names mark them as leftovers from manual testing. They have no
# property data worth answering questions about.
TEST_DEAL_PATTERN = re.compile(r"guard test|kill test|snapshot probe|collab rename", re.I)


# ---------- HTTP ----------

def _request(method: str, url: str, *, token: str | None = None, payload: dict | None = None,
             raw: bytes | None = None, content_type: str | None = None) -> dict | None:
    body = raw if raw is not None else (json.dumps(payload).encode() if payload is not None else None)
    req = urllib.request.Request(url, data=body, method=method)
    if content_type:
        req.add_header("Content-Type", content_type)
    elif payload is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req) as resp:
        text = resp.read()
        return json.loads(text) if text and resp.headers.get_content_type() == "application/json" else None


def login(email: str, password: str) -> str:
    return _request("POST", f"{AUTH_URL}/auth/v1/login",
                    payload={"email": email, "password": password})["data"]["accessToken"]


# ---------- corpus <-> deals ----------

def index_corpus(corpus: Path) -> dict[str, list[tuple[str, Path]]]:
    """property name -> [(fileType, path)]. Filenames carry a 6-hex suffix per
    property set, which is dropped here."""
    by_property: dict[str, list[tuple[str, Path]]] = {}
    for path in sorted(corpus.glob("*.pdf")):
        stem = re.sub(r"_[0-9a-f]{6}$", "", path.stem)
        for prefix in DOC_TYPES:
            if stem.startswith(prefix + "_"):
                prop = stem[len(prefix) + 1:].replace("_", " ").strip()
                by_property.setdefault(prop, []).append((DOC_TYPE_MAP[prefix], path))
                break
    return by_property


def fetch_deals(token: str) -> list[dict]:
    data = _request("GET", f"{DEALS_URL}/deals/v1/deals?pageSize=200", token=token)["data"]
    return data["items"] if isinstance(data, dict) and "items" in data else data


def build_plan(deals: list[dict], by_property: dict, max_deals: int | None) -> list[dict]:
    """Deals that have a matching document set, spread across stages: the plan takes
    one deal per stage in rotation so a truncated run still covers the pipeline
    rather than filling up on whichever stage sorts first."""
    matched = []
    for deal in deals:
        prop = deal.get("propertyName")
        if not prop or TEST_DEAL_PATTERN.search(deal.get("name", "")):
            continue
        docs = by_property.get(prop)
        if docs:
            matched.append({"deal": deal, "documents": docs})

    by_stage: dict[str, list[dict]] = {}
    for entry in matched:
        by_stage.setdefault(entry["deal"].get("stage", "?"), []).append(entry)
    for entries in by_stage.values():
        entries.sort(key=lambda e: -len(e["documents"]))  # richest set first

    ordered, stages = [], sorted(by_stage)
    while any(by_stage[s] for s in stages):
        for stage in stages:
            if by_stage[stage]:
                ordered.append(by_stage[stage].pop(0))
    return ordered[:max_deals] if max_deals else ordered


# ---------- destructive reset ----------

def wipe() -> None:
    """Clear every trace of previously seeded documents. Dev-only."""
    steps = [
        (["docker", "exec", "proptrackservices-rag-db-1", "psql", "-U", "proptrack", "-d", "proptrack_rag",
          "-c", "TRUNCATE document_chunks, ingestion_runs;"], "rag-db chunks + runs"),
        (["docker", "exec", "proptrackservices-deals-db-1", "psql", "-U", "proptrack", "-d", "proptrack_deals",
          "-c", "TRUNCATE deal_documents;"], "deals-db deal_documents"),
        (["docker", "exec", "proptrackservices-documents-db-1", "psql", "-U", "proptrack", "-d",
          "proptrack_documents", "-c", "TRUNCATE document_records;"], "documents-db document_records"),
    ]
    for cmd, label in steps:
        result = subprocess.run(cmd, capture_output=True, text=True)
        print(f"  wiped {label}: {result.stdout.strip() or result.stderr.strip()}")

    subprocess.run(["docker", "exec", "proptrackservices-minio-1", "mc", "alias", "set", "local",
                    "http://localhost:9000", "minioadmin", "minioadmin"], capture_output=True)
    result = subprocess.run(["docker", "exec", "proptrackservices-minio-1", "mc", "rm", "--recursive",
                             "--force", "local/proptrack-documents"], capture_output=True, text=True)
    removed = len([line for line in result.stdout.splitlines() if line.strip()])
    print(f"  wiped storage bucket: {removed} object(s)")


# ---------- upload ----------

def seed_document(token: str, deal_id: str, file_type: str, path: Path) -> str:
    payload = path.read_bytes()

    upload = _request("POST", f"{DOCS_URL}/documents/v1/upload-url", token=token, payload={
        "fileName": path.name, "contentType": "application/pdf", "sizeBytes": len(payload),
    })["data"]
    document_id = upload["documentId"]

    _request("PUT", upload["uploadUrl"], raw=payload, content_type="application/pdf")
    _request("POST", f"{DOCS_URL}/documents/v1/confirm", token=token, payload={"documentId": document_id})
    _request("POST", f"{DEALS_URL}/deals/v1/deals/{deal_id}/documents", token=token, payload={
        "fileName": path.name, "fileType": file_type,
        "storageUrl": f"/documents/v1/{document_id}",
    })
    return document_id


def wait_for_chunks(token: str, document_ids: list[str], timeout_s: int | None = None) -> dict[str, int]:
    """Poll retrieval until each document has chunks. Ingestion is asynchronous —
    Kafka delivery plus Docling parsing plus an embedding round-trip per document.

    The budget scales with the number of documents rather than being a flat 900s.
    That constant was set when this seeded ~118 documents; a full-corpus run is 428
    and overran it, printing a 288-document "never produced chunks" warning for
    documents that were ingesting correctly and finished minutes later. A timeout
    that doesn't grow with the workload reports a throughput limit as data loss.

    Note this polls once per *pending* document per cycle, so a large backlog is
    also a lot of search calls — each one embeds the probe query. Cheap, but it is
    why the cycle sleeps rather than spinning."""
    if timeout_s is None:
        # ~10s per document, floor 900s. Measured: 428 documents took ~25 minutes.
        timeout_s = max(900, 10 * len(document_ids))
    pending, counts = set(document_ids), {}
    deadline = time.time() + timeout_s
    print(f"Waiting for {len(pending)} document(s) to be ingested", end="", flush=True)
    while pending and time.time() < deadline:
        time.sleep(5)
        print(".", end="", flush=True)
        for document_id in list(pending):
            hits = _request("POST", f"{INGESTION_URL}/ingestion/v1/search", token=token, payload={
                "query": "property valuation summary", "documentId": document_id, "topK": 50,
            })["data"]["chunks"]
            if hits:
                counts[document_id] = len(hits)
                pending.discard(document_id)
    print()
    if pending:
        print(f"  WARNING: {len(pending)} document(s) never produced chunks: {sorted(pending)}")
    return counts


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", type=Path, default=DEFAULT_CORPUS)
    ap.add_argument("--email", default="admin@proptrack.local")
    ap.add_argument("--password", default="ChangeMe123!")
    ap.add_argument("--max-deals", type=int, default=None)
    ap.add_argument("--manifest", type=Path, default=Path("seed-manifest.json"))
    ap.add_argument("--plan", action="store_true", help="print the plan and exit")
    ap.add_argument("--wipe", action="store_true", help="clear existing documents first (destructive)")
    args = ap.parse_args()

    if not args.corpus.is_dir():
        sys.exit(f"Corpus directory not found: {args.corpus}")

    by_property = index_corpus(args.corpus)
    print(f"Corpus: {sum(len(v) for v in by_property.values())} files across "
          f"{len(by_property)} properties\n")

    token = login(args.email, args.password)
    plan = build_plan(fetch_deals(token), by_property, args.max_deals)
    if not plan:
        sys.exit("No deals matched a document set by property name.")

    total = sum(len(e["documents"]) for e in plan)
    print(f"Plan: {len(plan)} deal(s), {total} document(s)")
    for entry in plan:
        deal = entry["deal"]
        kinds = ", ".join(sorted({t for t, _ in entry["documents"]}))
        print(f"  {deal.get('stage','?'):<20} {deal['propertyName']:<52} "
              f"{len(entry['documents']):2d} docs  [{kinds}]")
    if args.plan:
        return

    if args.wipe:
        print("\nWiping existing document data...")
        wipe()

    print()
    manifest, document_ids = [], []
    for entry in plan:
        deal = entry["deal"]
        print(f"{deal['propertyName']} ({deal['id']})")
        for file_type, path in entry["documents"]:
            try:
                document_id = seed_document(token, deal["id"], file_type, path)
            except urllib.error.HTTPError as ex:
                print(f"    FAILED {path.name}: {ex.code} {ex.read().decode(errors='replace')[:200]}")
                continue
            document_ids.append(document_id)
            manifest.append({
                "dealId": deal["id"], "dealName": deal.get("name"),
                "propertyName": deal.get("propertyName"), "stage": deal.get("stage"),
                "documentId": document_id, "fileName": path.name, "fileType": file_type,
            })
            print(f"    {file_type:<20} {path.name}  -> {document_id}")

    counts = wait_for_chunks(token, document_ids)
    for row in manifest:
        row["chunkCount"] = counts.get(row["documentId"], 0)

    args.manifest.write_text(json.dumps(manifest, indent=2))
    ingested = sum(1 for r in manifest if r["chunkCount"])
    print(f"\nSeeded {len(manifest)} document(s); {ingested} ingested "
          f"({sum(r['chunkCount'] for r in manifest)} chunks). Manifest: {args.manifest}")


if __name__ == "__main__":
    main()
