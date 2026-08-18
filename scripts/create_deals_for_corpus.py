#!/usr/bin/env python3
"""Create deals for corpus properties that don't have one, so more can be seeded.

The retrieval eval covers only the properties whose documents are seeded, and
that count is capped by *deals*, not by documents or by the corpus:
seed_deal_documents.py matches a deal to a document set on `deal.propertyName`,
so a corpus property with no deal can never receive its documents. Measured
before this script existed: 83 corpus properties, 33 deals, 17 matches — and
exactly 17 properties seeded, with 408 documents belonging to the other 66
sitting unused on disk.

This closes that gap by creating the missing deals from listings-service, which
is where the corpus figures came from in the first place. Run it, then re-run
seed_deal_documents.py to attach the documents.

Name matching is exact first, then a single unambiguous prefix extension. The
prefix pass exists because corpus filenames truncate long property names —
`Crossroads_Arts_District_Office_Tow` is a filesystem artifact of
`Crossroads Arts District Office Tower`, not a different asset. A prefix that
matches more than one listing is skipped rather than guessed at: "Central Jersey
Industrial Corridor" extends to both an Apartments and a Shopping Center, and
picking one would attach a building's documents to the wrong asset — the exact
failure seed_deal_documents.py's header warns about, where retrieval works
perfectly and every answer is about the wrong property.

Usage:
    python3 scripts/create_deals_for_corpus.py --plan   # show what would be created
    python3 scripts/create_deals_for_corpus.py          # create them
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import seed_deal_documents as seed  # reuse its login, corpus index, and deal fetch

LISTINGS_URL = "http://localhost:5100"
DEALS_URL = "http://localhost:5200"


def fetch_properties(token: str) -> list[dict]:
    """All listings properties. Note this service returns a bare envelope
    ({items, totalCount, ...}), not the {data, meta} shape the other services
    use — reading `data` here raises KeyError."""
    req = urllib.request.Request(f"{LISTINGS_URL}/listings/v1/properties?pageSize=500")
    req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req) as resp:
        body = json.loads(resp.read())
    return body.get("items") or body.get("data", {}).get("items", [])


def resolve(corpus_names: set[str], properties: list[dict]) -> tuple[dict, list, list]:
    """corpus name -> listing. Exact match first, then unambiguous prefix extension.

    Exact matches are resolved in a separate first pass and their listings are
    then off-limits to the prefix pass. Without that, `Austin Business Park`
    prefix-extends onto `Austin Business Park - Building 3` — which is a distinct
    corpus property with its own 10 documents and its own exact match to that
    same listing. Two corpus properties would share one listing, and one
    building's documents would end up attached to the other's deal.

    A truncation artifact always has *no* exact match of its own, so requiring
    the target listing to be unclaimed separates the two cases cleanly."""
    by_title = {p["title"]: p for p in properties}
    resolved: dict[str, dict] = {}
    ambiguous: list[tuple[str, list[str]]] = []
    unmatched: list[str] = []

    # Pass 1: exact. These are unambiguous by construction.
    for name in sorted(corpus_names):
        if name in by_title:
            resolved[name] = by_title[name]

    claimed = {p["id"] for p in resolved.values()}

    # Pass 2: prefix, over listings nothing has claimed yet.
    for name in sorted(corpus_names - set(resolved)):
        candidates = [p for t, p in by_title.items()
                      if t.startswith(name) and p["id"] not in claimed]
        if len(candidates) == 1:
            resolved[name] = candidates[0]
            claimed.add(candidates[0]["id"])
        elif len(candidates) > 1:
            ambiguous.append((name, [p["title"] for p in candidates]))
        else:
            unmatched.append(name)
    return resolved, ambiguous, unmatched


def create_deal(token: str, listing: dict, property_name: str) -> str:
    """One deal for one property. propertyName must be the *corpus* spelling —
    that is the key seed_deal_documents.py matches on, so writing the listing's
    (possibly longer) title here would leave the deal unseedable."""
    payload = {
        "propertyId": listing["id"],
        "propertyName": property_name,
        "propertyType": listing.get("propertyType"),
        "metroArea": (listing.get("address") or {}).get("metro"),
        "occupancyRate": listing.get("occupancyRate"),
        "marketCapRateBenchmark": listing.get("marketCapRateBenchmark"),
        "priority": "Medium",
        "offerPrice": listing.get("askingPrice"),
    }
    body = json.dumps({k: v for k, v in payload.items() if v is not None}).encode()
    req = urllib.request.Request(f"{DEALS_URL}/deals/v1/deals", data=body, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read())["data"]["id"]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--corpus", type=Path, default=seed.DEFAULT_CORPUS)
    ap.add_argument("--email", default="admin@proptrack.local")
    ap.add_argument("--password", default="ChangeMe123!")
    ap.add_argument("--plan", action="store_true", help="print what would be created, create nothing")
    ap.add_argument("--limit", type=int, help="create at most N deals")
    args = ap.parse_args()

    try:
        token = seed.login(args.email, args.password)
    except urllib.error.URLError as ex:
        print(f"Could not reach auth-service: {ex}\nIs the stack up?", file=sys.stderr)
        return 1

    documents = seed.index_corpus(args.corpus)
    properties = fetch_properties(token)
    existing = {d.get("propertyName") for d in seed.fetch_deals(token)}

    resolved, ambiguous, unmatched = resolve(set(documents), properties)
    missing = {n: p for n, p in resolved.items() if n not in existing}
    if args.limit:
        missing = dict(list(missing.items())[:args.limit])

    doc_count = sum(len(documents[n]) for n in missing)
    print(f"corpus properties:          {len(documents)}")
    print(f"  resolved to a listing:    {len(resolved)}")
    print(f"  already have a deal:      {len(resolved) - len([n for n in resolved if n not in existing])}")
    print(f"  to create:                {len(missing)}")
    print(f"  documents they unlock:    {doc_count}")
    if ambiguous:
        print(f"\n  skipped, ambiguous prefix ({len(ambiguous)}):")
        for name, cands in ambiguous:
            print(f"    {name!r} -> {cands}")
    if unmatched:
        print(f"\n  skipped, no listing ({len(unmatched)}): "
              f"{sum(len(documents[n]) for n in unmatched)} documents stay unseeded")
        for name in unmatched:
            print(f"    {name!r}")

    if args.plan:
        print("\n[--plan] nothing created. Deals that would be created:")
        for name, listing in missing.items():
            print(f"  {name:<50} -> listing {listing['id']}  ({len(documents[name])} docs)")
        return 0

    if not missing:
        print("\nNothing to create.")
        return 0

    print()
    created, failed = 0, 0
    for name, listing in missing.items():
        try:
            deal_id = create_deal(token, listing, name)
            created += 1
            print(f"  created {name:<50} -> {deal_id}")
        except urllib.error.HTTPError as ex:
            failed += 1
            print(f"  FAILED  {name:<50} {ex.code} {ex.read().decode(errors='replace')[:120]}",
                  file=sys.stderr)

    print(f"\nCreated {created} deal(s){f', {failed} failed' if failed else ''}.")
    print("Next: python3 scripts/seed_deal_documents.py   (attaches and ingests their documents)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
