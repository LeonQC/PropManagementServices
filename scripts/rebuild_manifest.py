#!/usr/bin/env python3
"""Rebuild seed-manifest.json from the running services.

seed_deal_documents.py writes a manifest as a side effect of seeding, but that file
isn't committed, so a corpus seeded in an earlier session leaves no record on disk.
Rather than re-seed 521 PDFs to recover a mapping the services already know, read it
back from deals-service.

The subtlety worth knowing: the id that retrieval reports for a chunk is NOT the
deal_documents row id. deal_documents rows carry a pointer, and the documents-service
id is the tail of storageUrl — see DealDocumentsClient.cs, which does the same
extraction. Getting this wrong produces a manifest whose documentIds never match any
retrieved chunk, and every eval question would score as a miss for the wrong reason.

Usage:
    python3 scripts/rebuild_manifest.py
    python3 scripts/rebuild_manifest.py --out seed-manifest.json
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

AUTH_URL = "http://localhost:5300"
DEALS_URL = "http://localhost:5200"

STORAGE_PREFIX = "/documents/v1/"


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


def fetch_deals(token: str) -> list[dict]:
    data = _request("GET", f"{DEALS_URL}/deals/v1/deals?pageSize=200", token=token)["data"]
    return data["items"] if isinstance(data, dict) and "items" in data else data


def documents_for(token: str, deal_id: str) -> list[dict]:
    try:
        return _request("GET", f"{DEALS_URL}/deals/v1/deals/{deal_id}/documents", token=token)["data"] or []
    except urllib.error.HTTPError as ex:
        if ex.code == 404:
            return []
        raise


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=Path("seed-manifest.json"))
    ap.add_argument("--email", default="admin@proptrack.local")
    ap.add_argument("--password", default="ChangeMe123!")
    args = ap.parse_args()

    try:
        token = login(args.email, args.password)
    except urllib.error.URLError as ex:
        print(f"Could not reach auth-service at {AUTH_URL}: {ex}\nIs the stack up?", file=sys.stderr)
        return 1

    deals = fetch_deals(token)
    print(f"{len(deals)} deal(s)", file=sys.stderr)

    manifest, skipped = [], 0
    for deal in deals:
        for doc in documents_for(token, deal["id"]):
            storage_url = doc.get("storageUrl") or ""
            if not storage_url.startswith(STORAGE_PREFIX):
                skipped += 1  # never uploaded through the storage flow; nothing ingested
                continue
            document_id = storage_url[len(STORAGE_PREFIX):].rstrip("/")
            if not document_id:
                skipped += 1
                continue
            manifest.append({
                "dealId": deal["id"],
                "dealName": deal.get("name"),
                "propertyName": deal.get("propertyName"),
                "stage": deal.get("stage"),
                "documentId": document_id,
                "fileName": doc.get("fileName"),
                "fileType": doc.get("fileType"),
            })

    args.out.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Wrote {len(manifest)} document row(s) to {args.out}"
          f"{f' ({skipped} skipped: no storage pointer)' if skipped else ''}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
