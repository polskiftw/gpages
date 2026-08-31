#!/usr/bin/env python3
"""Build vendor purchase metadata for Terraria Shopping.

The availability generator deliberately keeps acquisition-source labels concise.
This companion pass finds shopping leaves whose primary source is a vendor-only
purchase and asks the Official Terraria Wiki's Itemsource template for the
seller and base shop price. The UI can then render purchase badges as, e.g.,
"Goblin Tinkerer • 5 GC" without conflating shop prices with progression data.
"""
from __future__ import annotations

import html
import json
import pathlib
import re
import sys
import time
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
ROOT = pathlib.Path(__file__).resolve().parents[1]
AVAILABILITY_DATA = ROOT / "availability.generated.js"
OUT = ROOT / "vendors.generated.js"
ITEMSOURCE_BATCH = 12
USER_AGENT = "polskiftw/gpages terraria-shopping vendor-prices/1.0 (GitHub Pages data refresh)"

VENDORS = {
    "Merchant", "Traveling Merchant", "Skeleton Merchant", "Clothier", "Wizard",
    "Mechanic", "Dryad", "Goblin Tinkerer", "Demolitionist", "Arms Dealer",
    "Painter", "Dye Trader", "Witch Doctor", "Steampunker", "Cyborg", "Truffle",
    "Party Girl", "Pirate", "Santa Claus", "Tavernkeep", "Zoologist", "Golfer",
    "Princess",
}

# Recipe-category aliases have no literal item page for Itemsource to render.
VENDOR_OVERRIDES: dict[str, tuple[str, str]] = {
    "Any Guide to Critter Companionship": ("Zoologist", "5 GC"),
    "Any Guide to Environmental Preservation": ("Dryad", "5 GC"),
}

VERSION_PAREN = re.compile(r"\((?:Desktop|Console|Mobile|Old-gen|3DS|Switch)[^)]*versions?\)", re.I)


def request_json(params: dict[str, object], retries: int = 4) -> dict:
    encoded = urllib.parse.urlencode(params).encode("utf-8")
    req = urllib.request.Request(API, data=encoded, headers={
        "User-Agent": USER_AGENT,
        "Accept": "application/json",
        "Content-Type": "application/x-www-form-urlencoded",
    })
    last: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=60) as response:
                return json.loads(response.read().decode("utf-8"))
        except Exception as exc:  # pragma: no cover - network retry path
            last = exc
            if attempt + 1 < retries:
                time.sleep(2 ** attempt)
    raise RuntimeError(f"Wiki request failed after {retries} attempts: {last}")


def load_availability() -> dict[str, list[object]]:
    raw = AVAILABILITY_DATA.read_text(encoding="utf-8").strip()
    prefix = "window.TERRARIA_GENERATED_AVAILABILITY="
    if not raw.startswith(prefix):
        raise RuntimeError("Unexpected availability.generated.js format")
    payload = raw[len(prefix):].rstrip(";\n ")
    data = json.loads(payload)
    if not isinstance(data, dict):
        raise RuntimeError("Availability data is not an object")
    return data


def html_to_plain(value: str) -> str:
    value = re.sub(
        r"<img\b[^>]*\balt=(?:\"([^\"]*)\"|'([^']*)')[^>]*>",
        lambda m: " " + html.unescape(m.group(1) or m.group(2) or "") + " ",
        value,
        flags=re.I,
    )
    value = re.sub(r"</li\s*>", " / ", value, flags=re.I)
    value = re.sub(r"<br\s*/?>", " / ", value, flags=re.I)
    value = re.sub(r"<style\b[^>]*>.*?</style>", " ", value, flags=re.I | re.S)
    value = re.sub(r"<script\b[^>]*>.*?</script>", " ", value, flags=re.I | re.S)
    value = re.sub(r"<[^>]+>", " ", value)
    return re.sub(r"\s+", " ", html.unescape(value).replace("\u00a0", " ")).strip()


def vendor_only_seller(source: object) -> str:
    text = re.sub(r"\s*\(NPC\)\s*$", "", str(source or "").strip(), flags=re.I)
    for vendor in sorted(VENDORS, key=len, reverse=True):
        if text.casefold() == vendor.casefold():
            return vendor
    return ""


def compact_price(value: str) -> str:
    text = VERSION_PAREN.sub("", html.unescape(value).replace("\u00a0", " "))
    replacements = (
        (r"\bPlatinum\s+Coins?\b", "PC"), (r"\bPlatinum\b", "PC"),
        (r"\bGold\s+Coins?\b", "GC"), (r"\bGold\b", "GC"),
        (r"\bSilver\s+Coins?\b", "SC"), (r"\bSilver\b", "SC"),
        (r"\bCopper\s+Coins?\b", "CC"), (r"\bCopper\b", "CC"),
    )
    for pattern, repl in replacements:
        text = re.sub(pattern, repl, text, flags=re.I)
    text = re.sub(r"\s+", " ", text).strip()
    coins = re.findall(r"(\d+(?:\.\d+)?)\s*(PC|GC|SC|CC)\b", text, flags=re.I)
    if coins:
        return " ".join(f"{amount} {unit.upper()}" for amount, unit in coins)
    medal = re.search(r"(\d+)\s+Defender\s+Medals?\b", text, flags=re.I)
    if medal:
        count = int(medal.group(1))
        return f"{count} Defender Medal{'s' if count != 1 else ''}"
    return ""


def extract_vendor_price(value: str, seller: str) -> str:
    clean = VERSION_PAREN.sub("", value)
    pattern = re.compile(re.escape(seller) + r"\s*\(([^()]*)\)", flags=re.I)
    for match in pattern.finditer(clean):
        price = compact_price(match.group(1))
        if price:
            return price
    seller_match = re.search(re.escape(seller), clean, flags=re.I)
    if seller_match:
        return compact_price(clean[seller_match.end():seller_match.end() + 120])
    return ""


def fetch_vendor_prices(candidates: dict[str, str]) -> dict[str, tuple[str, str]]:
    out: dict[str, tuple[str, str]] = {}
    names = sorted(candidates, key=str.casefold)
    for start in range(0, len(names), ITEMSOURCE_BATCH):
        batch = names[start:start + ITEMSOURCE_BATCH]
        chunks: list[str] = []
        for index, name in enumerate(batch):
            marker = f"TVND{index:02d}"
            chunks.append(f"@@{marker}START@@{{{{itemsource|{name}|sep= / }}}}@@{marker}END@@")
        payload = request_json({
            "action": "parse", "contentmodel": "wikitext", "prop": "text",
            "title": "Terraria Shopping vendor audit", "text": "\n".join(chunks),
            "format": "json", "formatversion": "2",
        })
        rendered = str((payload.get("parse") or {}).get("text") or "")
        plain = html_to_plain(rendered)
        for index, name in enumerate(batch):
            marker = f"TVND{index:02d}"
            match = re.search(rf"@@{marker}START@@(.*?)@@{marker}END@@", plain, flags=re.S)
            if not match:
                continue
            seller = candidates[name]
            segment = match.group(1)
            if name in {"Bug Net", "Empty Bullet", "Spelunker Glowstick"}:
                print(f"Vendor raw {name}: {segment!r}", file=sys.stderr)
            price = extract_vendor_price(segment, seller)
            if price:
                out[name] = (seller, price)
        print(
            f"Vendor prices {min(start + len(batch), len(names))}/{len(names)}; resolved {len(out)}",
            file=sys.stderr,
        )
        time.sleep(0.12)
    return out


def validate(vendors: dict[str, tuple[str, str]], candidates: dict[str, str]) -> None:
    unresolved = [name for name in candidates if name not in vendors and name not in VENDOR_OVERRIDES]
    if unresolved:
        print("Vendor-only shopping leaves with no parsed price:", file=sys.stderr)
        for name in sorted(unresolved, key=str.casefold):
            print(f"  - {name}: {candidates[name]}", file=sys.stderr)
        raise RuntimeError(f"{len(unresolved)} vendor-only shopping leaves have no seller/price metadata")

    probes = {
        "Rocket Boots": "Goblin Tinkerer",
        "Wire": "Mechanic",
        "Bug Net": "Merchant",
    }
    failures: list[str] = []
    for item, expected_seller in probes.items():
        if item not in candidates:
            continue
        row = vendors.get(item)
        if not row or row[0] != expected_seller or not row[1]:
            failures.append(item)
    if failures:
        raise RuntimeError("Vendor-price regression: " + ", ".join(failures))


def main() -> int:
    availability = load_availability()
    candidates: dict[str, str] = {}
    for name, row in availability.items():
        source = row[2] if isinstance(row, list) and len(row) > 2 else ""
        seller = vendor_only_seller(source)
        if seller:
            candidates[name] = seller

    if len(candidates) < 10:
        raise RuntimeError(f"Suspiciously few vendor-only sources found: {len(candidates)}")

    vendors = fetch_vendor_prices(candidates)
    vendors.update(VENDOR_OVERRIDES)
    validate(vendors, candidates)

    payload = {name: [seller, price] for name, (seller, price) in sorted(vendors.items(), key=lambda p: p[0].casefold())}
    OUT.write_text(
        "window.TERRARIA_GENERATED_VENDORS=" + json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + ";\n",
        encoding="utf-8",
    )
    print(f"Vendor purchase badges: {len(payload)} seller/price rows", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
