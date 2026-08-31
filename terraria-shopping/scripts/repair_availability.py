#!/usr/bin/env python3
"""Repair and validate generated Terraria shopping acquisition badges.

The Wiki's Itemsource template sometimes renders conjunctions (notably ``or``)
or reversible craft inputs as the first text fragment. Those are technically
present in the source table but useless in a shopping list. This pass re-queries
only suspect rows, skips recipe/decraft fragments, and fails the data refresh if
anything still lacks an actionable acquisition route.
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
RECIPE_DATA = ROOT / "data.generated.js"
AVAIL_DATA = ROOT / "availability.generated.js"
BATCH = 10
USER_AGENT = "polskiftw/gpages terraria-shopping source-repair/1.0 (GitHub Pages data refresh)"

BAD_WORDS = {
    "", "or", "and", "crafting", "by hand", "plundering", "looting",
    "drop", "drops", "source", "sources",
}
NPCS = {
    "Merchant", "Traveling Merchant", "Skeleton Merchant", "Wizard", "Mechanic",
    "Dryad", "Goblin Tinkerer", "Demolitionist", "Arms Dealer", "Painter",
    "Dye Trader", "Witch Doctor", "Steampunker", "Cyborg", "Truffle",
    "Party Girl", "Pirate", "Santa Claus", "Tavernkeep", "Zoologist",
    "Golfer", "Princess", "Clothier",
}
VERSION_PAREN = re.compile(r"\((?:Desktop|Console|Mobile|Old-gen|3DS|Switch)[^)]*versions?\)", re.I)
COIN_PAREN = re.compile(r"\([^)]*(?:Platinum|Gold|Silver|Copper|\bPC\b|\bGC\b|\bSC\b|\bCC\b)[^)]*\)", re.I)


def load_js(path: pathlib.Path, prefix: str) -> dict:
    text = path.read_text(encoding="utf-8").strip()
    if not text.startswith(prefix) or not text.endswith(";"):
        raise RuntimeError(f"{path.name} has an unexpected wrapper")
    return json.loads(text[len(prefix):-1])


def request_json(params: dict[str, object], retries: int = 4) -> dict:
    encoded = urllib.parse.urlencode(params).encode("utf-8")
    req = urllib.request.Request(
        API,
        data=encoded,
        headers={
            "User-Agent": USER_AGENT,
            "Accept": "application/json",
            "Content-Type": "application/x-www-form-urlencoded",
        },
    )
    last: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=60) as response:
                return json.loads(response.read().decode("utf-8"))
        except Exception as exc:
            last = exc
            if attempt + 1 < retries:
                time.sleep(2 ** attempt)
    raise RuntimeError(f"Wiki request failed after {retries} attempts: {last}")


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
    return re.sub(r"\s+", " ", html.unescape(value)).strip()


def recipe_inputs(data: dict) -> dict[str, set[str]]:
    out: dict[str, set[str]] = {}
    for recipe in data.get("recipes") or []:
        result = str(recipe.get("r") or "")
        out.setdefault(result, set()).update(str(pair[0]) for pair in recipe.get("i") or [])
    return out


def normalize(value: str) -> str:
    value = VERSION_PAREN.sub("", value)
    value = COIN_PAREN.sub("", value)
    value = re.sub(r"\s*/\s*(?:Plundering|Looting)\b", "", value, flags=re.I)
    return re.sub(r"\s+", " ", value).strip(" /:,-")


def recipe_like(source: str, inputs: set[str]) -> bool:
    core = normalize(source)
    if not core:
        return False
    if re.search(r"\(\s*@\s*[^)]*\)", core) or " @ " in core:
        return True
    no_paren = re.sub(r"\s*\([^)]*\)\s*$", "", core).strip()
    for ingredient in inputs:
        if no_paren.casefold() == ingredient.casefold():
            return True
        if re.match(rf"^\d+(?:\.\d+)?\s+{re.escape(ingredient)}(?:\s|$)", no_paren, flags=re.I):
            return True
    return bool(re.match(r"^\d+(?:\.\d+)?\s+.*\b(?:Platform|Wall)\b", no_paren, flags=re.I))


def bad_source(source: str, inputs: set[str]) -> bool:
    source = normalize(source)
    return source.casefold() in BAD_WORDS or recipe_like(source, inputs)


def split_candidates(value: str) -> list[str]:
    # Itemsource commonly emits literal "or" between otherwise valid alternatives.
    value = re.sub(r"\s+\bor\b\s+", " / ", value, flags=re.I)
    value = re.sub(r"\s+\band\b\s+", " / ", value, flags=re.I)
    return [normalize(part) for part in re.split(r"\s+/\s+", value) if normalize(part)]


def clean_candidate(candidate: str, inputs: set[str]) -> str:
    candidate = normalize(candidate)
    candidate = re.sub(
        r"^Shimmer\s+Shimmer transmutation\s*:\s*",
        "Shimmer transmutation: ",
        candidate,
        flags=re.I,
    )
    if candidate.casefold() in BAD_WORDS or recipe_like(candidate, inputs):
        return ""
    if candidate in NPCS:
        candidate += " (NPC)"
    return candidate[:120]


def query_sources(names: list[str], inputs_by_result: dict[str, set[str]]) -> dict[str, str]:
    out: dict[str, str] = {}
    for start in range(0, len(names), BATCH):
        batch = names[start:start + BATCH]
        chunks = []
        for index, name in enumerate(batch):
            marker = f"TSFIX{index:02d}"
            chunks.append(f"@@{marker}START@@{{{{itemsource|{name}|sep= / }}}}@@{marker}END@@")
        payload = request_json({
            "action": "parse",
            "contentmodel": "wikitext",
            "prop": "text",
            "title": "Terraria Shopping source repair",
            "text": "\n".join(chunks),
            "format": "json",
            "formatversion": "2",
        })
        plain = html_to_plain(str((payload.get("parse") or {}).get("text") or ""))
        for index, name in enumerate(batch):
            marker = f"TSFIX{index:02d}"
            match = re.search(rf"@@{marker}START@@(.*?)@@{marker}END@@", plain, flags=re.S)
            if not match:
                continue
            inputs = inputs_by_result.get(name, set())
            for candidate in split_candidates(match.group(1)):
                source = clean_candidate(candidate, inputs)
                if source:
                    out[name] = source
                    break
        print(f"Source repair queried {min(start + len(batch), len(names))}/{len(names)}; resolved {len(out)}", file=sys.stderr)
        time.sleep(0.12)
    return out


def main() -> int:
    data = load_js(RECIPE_DATA, "window.TERRARIA_RECIPE_DATA=")
    rows = load_js(AVAIL_DATA, "window.TERRARIA_GENERATED_AVAILABILITY=")
    inputs_by_result = recipe_inputs(data)

    suspects = [
        name for name, row in rows.items()
        if bad_source(str((row or [None, None, ""])[2] or ""), inputs_by_result.get(name, set()))
    ]
    if suspects:
        repaired = query_sources(sorted(suspects, key=str.casefold), inputs_by_result)
        for name in suspects:
            source = repaired.get(name, "")
            if source:
                old = str(rows[name][2] or "")
                rows[name][2] = source
                print(f"Repaired source: {name}: {old!r} -> {source!r}", file=sys.stderr)

    remaining = [
        name for name, row in rows.items()
        if bad_source(str((row or [None, None, ""])[2] or ""), inputs_by_result.get(name, set()))
    ]
    if remaining:
        print("Non-actionable shopping source badges remain:", file=sys.stderr)
        for name in remaining:
            print(f"  - {name}: {rows[name][2]!r}", file=sys.stderr)
        raise RuntimeError(f"{len(remaining)} non-actionable source badges remain")

    AVAIL_DATA.write_text(
        "window.TERRARIA_GENERATED_AVAILABILITY=" + json.dumps(rows, ensure_ascii=False, separators=(",", ":")) + ";\n",
        encoding="utf-8",
    )
    print(f"Availability source repair clean: {len(rows)} rows; 0 connector-only or recipe-input sources", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
