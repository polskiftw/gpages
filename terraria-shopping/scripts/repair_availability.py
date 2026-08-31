#!/usr/bin/env python3
"""Repair and validate generated Terraria shopping acquisition badges.

The Wiki's Itemsource template sometimes renders conjunctions (notably ``or``),
version fragments, or reversible craft inputs as the first text fragment. Those
are technically present in the source table but useless in a shopping list.
This pass applies small semantic rules for acquisition mechanics the generic
renderer cannot express, re-queries only remaining suspect rows, and fails the
data refresh if anything still lacks an actionable acquisition route.
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
USER_AGENT = "polskiftw/gpages terraria-shopping source-repair/1.1 (GitHub Pages data refresh)"

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

# These are not recipe substitutions. They are concise acquisition semantics for
# world generation, harvesting, NPC conditions, or other Itemsource renderings
# whose first plain-text fragment is ambiguous/useless to a shopping-list user.
SEMANTIC_SOURCES: dict[str, str] = {
    "Ash Wood": "Ash trees (chop)",
    "Boreal Wood": "Boreal trees (chop)",
    "Cactus": "Desert cactus (harvest)",
    "Cog": "Steampunker (NPC)",
    "Conveyor Belt (Counter Clockwise)": "Conveyor Slime → Clockwise → convert / Steampunker",
    "Crimstone Block": "Crimson terrain (mine)",
    "Dry Bomb": "Lava fishing → Wet Bomb → Dry Bomb",
    "Dry Rocket": "Cyborg (NPC)",
    "Dynasty Wood": "Traveling Merchant (NPC)",
    "Easter Block": "Dye Trader (NPC)",
    "Ebonstone Block": "Corruption terrain (mine)",
    "Ebonwood": "Ebonwood trees (chop)",
    "Echo Block": "Cyborg (NPC) • Graveyard",
    "Glowing Mushroom": "Glowing Mushroom biome (harvest)",
    "Grass Seeds": "Dryad (NPC)",
    "Ice Block": "Snow / Ice biome (mine)",
    "Jellyfish Block": "Pirate (NPC) • Graveyard",
    "Jungle Grass Seeds": "Jungle plants (harvest)",
    "Moonplate Block": "Shimmer transmutation: Sunplate Block",
    "Palm Wood": "Palm trees (chop)",
    "Pearlstone Block": "Hallow terrain (mine)",
    "Pearlwood": "Pearlwood trees (chop)",
    "Pine Wood": "Christmas Presents → Pine Tree Block",
    "Pumpkin": "Pumpkin plants (harvest)",
    "Rich Mahogany": "Mahogany trees (chop)",
    "Shadewood": "Shadewood trees (chop)",
}

VERSION_PAREN = re.compile(r"\((?:Desktop|Console|Mobile|Old-gen|3DS|Switch)[^)]*versions?\)", re.I)
COIN_PAREN = re.compile(r"\([^)]*(?:Platinum|Gold|Silver|Copper|\bPC\b|\bGC\b|\bSC\b|\bCC\b)[^)]*\)", re.I)
PLATFORM_FRAGMENT = re.compile(r"\b(?:Desktop|Console|Mobile|Old-gen|3DS|Switch)\b", re.I)


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


def collapse_duplicate_phrase(value: str) -> str:
    words = value.split()
    if len(words) >= 2 and len(words) % 2 == 0:
        half = len(words) // 2
        if [word.casefold() for word in words[:half]] == [word.casefold() for word in words[half:]]:
            return " ".join(words[:half])
    return value


def normalize(value: str) -> str:
    value = VERSION_PAREN.sub("", value)
    # Itemsource can leave a truncated platform-version parenthetical after HTML
    # flattening. Source badges never need platform labels, so discard that tail.
    value = re.sub(r"\s*\((?:Desktop|Console|Mobile|Old-gen|3DS|Switch)\b.*$", "", value, flags=re.I)
    value = COIN_PAREN.sub("", value)
    value = re.sub(r"\s*/\s*(?:Plundering|Looting)\b", "", value, flags=re.I)
    value = re.sub(r"^Shimmer\s+Shimmer transmutation\s*:\s*", "Shimmer transmutation: ", value, flags=re.I)
    value = re.sub(r"\s+", " ", value).strip(" /:,-")
    return collapse_duplicate_phrase(value)


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
    return (
        source.casefold() in BAD_WORDS
        or bool(PLATFORM_FRAGMENT.search(source))
        or recipe_like(source, inputs)
    )


def split_candidates(value: str) -> list[str]:
    # Itemsource commonly emits literal conjunctions between alternatives.
    value = re.sub(r"\s+\bor\b\s+", " / ", value, flags=re.I)
    value = re.sub(r"\s+\band\b\s+", " / ", value, flags=re.I)
    return [normalize(part) for part in re.split(r"\s+/\s+", value) if normalize(part)]


def clean_candidate(candidate: str, inputs: set[str]) -> str:
    candidate = normalize(candidate)
    if bad_source(candidate, inputs):
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


def ambiguous_reciprocal_pairs(data: dict) -> list[tuple[str, str]]:
    """List exact reversible pairs the planner cannot orient from shape/usage alone."""
    recipes = data.get("recipes") or []
    by_result: dict[str, list[dict]] = {}
    uses: dict[str, int] = {}
    for recipe in recipes:
        by_result.setdefault(str(recipe.get("r") or ""), []).append(recipe)
        for ingredient, _qty in recipe.get("i") or []:
            name = str(ingredient)
            uses[name] = uses.get(name, 0) + 1
    pairs: set[tuple[str, str]] = set()
    for recipe in recipes:
        inputs = recipe.get("i") or []
        if len(inputs) != 1:
            continue
        result = str(recipe.get("r") or "")
        ingredient = str(inputs[0][0])
        ingredient_qty = max(1, int(inputs[0][1]))
        result_qty = max(1, int(recipe.get("a") or 1))
        reverse = any(
            len(other.get("i") or []) == 1
            and str(other["i"][0][0]) == result
            and max(1, int(other["i"][0][1])) == result_qty
            and max(1, int(other.get("a") or 1)) == ingredient_qty
            for other in by_result.get(ingredient) or []
        )
        if not reverse:
            continue
        result_processed = result.endswith((" Platform", " Wall"))
        ingredient_processed = ingredient.endswith((" Platform", " Wall"))
        if result_processed != ingredient_processed or uses.get(result, 0) != uses.get(ingredient, 0):
            continue
        pairs.add(tuple(sorted((result, ingredient), key=str.casefold)))
    return sorted(pairs, key=lambda pair: (pair[0].casefold(), pair[1].casefold()))


def main() -> int:
    data = load_js(RECIPE_DATA, "window.TERRARIA_RECIPE_DATA=")
    rows = load_js(AVAIL_DATA, "window.TERRARIA_GENERATED_AVAILABILITY=")
    inputs_by_result = recipe_inputs(data)

    applied = 0
    for name, source in SEMANTIC_SOURCES.items():
        if name in rows and rows[name][2] != source:
            rows[name][2] = source
            applied += 1
    print(f"Applied {applied} semantic acquisition-source rules", file=sys.stderr)

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

    ambiguous = ambiguous_reciprocal_pairs(data)
    print(f"Ambiguous reversible recipe pairs: {len(ambiguous)}", file=sys.stderr)
    for left, right in ambiguous:
        print(f"  reciprocal-audit: {left} <-> {right}", file=sys.stderr)

    AVAIL_DATA.write_text(
        "window.TERRARIA_GENERATED_AVAILABILITY=" + json.dumps(rows, ensure_ascii=False, separators=(",", ":")) + ";\n",
        encoding="utf-8",
    )
    print(f"Availability source repair clean: {len(rows)} rows; 0 connector-only or recipe-input sources", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
