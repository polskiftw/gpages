#!/usr/bin/env python3
"""Build availability badges for generated Terraria Shopping list entries.

Recipe data tells the browser what must be collected. The Official Terraria Wiki
Items Cargo table supplies the authoritative Hardmode-only flag for real items;
small acquisition overrides add useful boss/event/source context for gated
ingredients without mixing prerequisites and drop sources into one vague label.
"""
from __future__ import annotations

import json
import math
import pathlib
import sys
import time
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
ROOT = pathlib.Path(__file__).resolve().parents[1]
RECIPE_DATA = ROOT / "data.generated.js"
OUT = ROOT / "availability.generated.js"
PAGE_SIZE = 500
USER_AGENT = "polskiftw/gpages terraria-shopping availability-badges/1.2 (GitHub Pages data refresh)"
ITEM_FIELDS = "name,hardmode"

# Extra pills for progression-gated leaves that regularly appear in larger
# crafting projects. Each list keeps prerequisites separate from the actual
# acquisition source, e.g. ["After Plantera", "Mothron (Solar Eclipse)"].
# Everything else still receives its broad mode badge from the Wiki's hardmode
# field.
PROGRESSION_OVERRIDES: dict[str, tuple[list[str], int]] = {
    "Starfury": (["Skyware Chest / Sky Crate"], 10),
    "Enchanted Sword": (["Enchanted Sword Shrine"], 10),
    "Bee Keeper": (["Queen Bee (boss)"], 15),
    "Muramasa": (["After Skeletron", "Dungeon Gold Chest"], 30),
    "Hellstone": (["After Eater of Worlds / Brain of Cthulhu", "Underworld (ore)"], 20),
    "Cobalt Ore": (["Tier 1 Hardmode ore"], 40),
    "Palladium Ore": (["Tier 1 Hardmode ore"], 40),
    "Mythril Ore": (["Tier 2 Hardmode ore"], 40),
    "Orichalcum Ore": (["Tier 2 Hardmode ore"], 40),
    "Adamantite Ore": (["Tier 3 Hardmode ore"], 40),
    "Titanium Ore": (["Tier 3 Hardmode ore"], 40),
    "Soul of Light": (["Underground Hallow enemies"], 40),
    "Soul of Night": (["Underground evil enemies"], 40),
    "Soul of Flight": (["Wyvern (enemy)"], 40),
    "Crystal Shard": (["Underground Hallow"], 40),
    "Cursed Flame": (["Corruption enemies"], 40),
    "Ichor": (["Crimson enemies"], 40),
    "Hallowed Bar": (["Any Mechanical Boss"], 50),
    "Soul of Fright": (["Skeletron Prime (boss)"], 50),
    "Soul of Might": (["The Destroyer (boss)"], 50),
    "Soul of Sight": (["The Twins (boss)"], 50),
    "Chlorophyte Ore": (["After all 3 Mechanical Bosses", "Underground Jungle (ore)"], 55),
    "Seedler": (["Plantera (boss)"], 60),
    "The Horseman's Blade": (["After Plantera", "Pumpking (Pumpkin Moon)"], 60),
    "Ectoplasm": (["After Plantera", "Dungeon Spirit (enemy)"], 60),
    "Broken Hero Sword": (["After Plantera", "Mothron (Solar Eclipse)"], 60),
    "Temple Key": (["Plantera (boss)"], 60),
    "Spooky Wood": (["After Plantera", "Pumpkin Moon enemies"], 60),
    "Beetle Husk": (["Golem (boss)"], 70),
    "Influx Waver": (["After Golem", "Martian Saucer (Martian Madness)"], 70),
    "Ancient Manipulator": (["Lunatic Cultist (boss)"], 80),
    "Solar Fragment": (["After Lunatic Cultist", "Solar Pillar"], 80),
    "Vortex Fragment": (["After Lunatic Cultist", "Vortex Pillar"], 80),
    "Nebula Fragment": (["After Lunatic Cultist", "Nebula Pillar"], 80),
    "Stardust Fragment": (["After Lunatic Cultist", "Stardust Pillar"], 80),
    "Luminite": (["Moon Lord (boss)"], 90),
    "Meowmere": (["Moon Lord (boss)"], 90),
    "Star Wrath": (["Moon Lord (boss)"], 90),
}

# Cargo recipe arguments sometimes use recipe-category labels or display names
# that are not literal rows in the Items table. These are real current-PC
# ingredients, so classify the aliases explicitly instead of letting them become
# bare shopping-list rows.
PSEUDO_AVAILABILITY: dict[str, tuple[str, list[str], int]] = {
    "Adamantite/Titanium Bar": ("Hardmode", ["Tier 3 Hardmode bar"], 40),
    "Blue Jellyfish (bait)": ("Pre-Hardmode", ["Underground / Cavern fishing"], 10),
    "Green Jellyfish (bait)": ("Hardmode", ["Underground / Cavern fishing"], 40),
    "Pink Jellyfish (bait)": ("Pre-Hardmode", ["Ocean fishing"], 10),
    "Music Box (Ocean)": ("Hardmode", ["Wizard (NPC)", "Record at Ocean"], 40),
    "Music Box (Space)": ("Hardmode", ["Wizard (NPC)", "Record in Space"], 40),
}

HARDMODE_PSEUDO_HINTS = (
    "Cobalt", "Palladium", "Mythril", "Orichalcum", "Adamantite",
    "Titanium", "Hallowed", "Chlorophyte", "Shroomite", "Spectre",
    "Luminite", "Fragment",
)


def request_json(params: dict[str, object], retries: int = 4) -> dict:
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
    last: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=45) as response:
                return json.loads(response.read().decode("utf-8"))
        except Exception as exc:  # pragma: no cover - network path
            last = exc
            if attempt + 1 < retries:
                time.sleep(2 ** attempt)
    raise RuntimeError(f"Cargo request failed after {retries} attempts: {last}")


def load_recipe_data() -> dict:
    text = RECIPE_DATA.read_text(encoding="utf-8").strip()
    prefix = "window.TERRARIA_RECIPE_DATA="
    if not text.startswith(prefix) or not text.endswith(";"):
        raise RuntimeError("data.generated.js has an unexpected wrapper")
    return json.loads(text[len(prefix):-1])


def cargo_bool(value: object) -> bool:
    return str(value or "").strip().lower() in {"1", "yes", "true", "y"}


def fetch_item_modes() -> dict[str, bool]:
    modes: dict[str, bool] = {}
    offset = 0
    while True:
        payload = request_json({
            "action": "cargoquery",
            "tables": "Items",
            "fields": ITEM_FIELDS,
            "limit": PAGE_SIZE,
            "offset": offset,
            "format": "json",
            "formatversion": "2",
        })
        batch = payload.get("cargoquery") or []
        if not isinstance(batch, list):
            raise RuntimeError("Unexpected Items Cargo response")
        if not batch:
            break
        for entry in batch:
            title = entry.get("title") or {}
            name = str(title.get("name") or "").strip()
            if not name:
                continue
            # Duplicate rows are rare. If any current row marks the name as
            # Hardmode-only, use the later badge rather than understating it.
            modes[name] = modes.get(name, False) or cargo_bool(title.get("hardmode"))
        offset += len(batch)
        print(f"Fetched {offset} Items rows; mapped {len(modes)} names", file=sys.stderr)
        if len(batch) < PAGE_SIZE:
            break
        time.sleep(0.15)
    if len(modes) < 4000:
        raise RuntimeError(f"Only {len(modes)} item names returned by Items Cargo")
    return modes


def merge_leaves(into: dict[str, int], other: dict[str, int]) -> None:
    for name, qty in other.items():
        into[name] = into.get(name, 0) + qty


def canonical_plan(
    name: str,
    qty: int,
    by_result: dict[str, list[dict]],
    stack: frozenset[str] = frozenset(),
    depth: int = 0,
) -> tuple[dict[str, int], int, int]:
    options = by_result.get(name) or []
    if not options or depth > 48:
        return {name: qty}, 0, 0
    if name in stack:
        return {name: qty}, 1, 0
    next_stack = stack | {name}
    candidates = []
    for recipe in options:
        batches = math.ceil(qty / max(1, int(recipe.get("a") or 1)))
        leaves: dict[str, int] = {}
        cycles = 0
        steps = 1
        for ingredient, amount in recipe.get("i") or []:
            child, child_cycles, child_steps = canonical_plan(
                str(ingredient), max(1, int(amount)) * batches, by_result, next_stack, depth + 1
            )
            merge_leaves(leaves, child)
            cycles += child_cycles
            steps += child_steps
        candidates.append((leaves, cycles, steps, recipe))
    clean = [c for c in candidates if c[1] == 0]
    if not clean:
        return {name: qty}, 0, 0
    clean.sort(key=lambda c: (
        len(c[0]), sum(c[0].values()), c[2], str(c[3].get("s") or "").casefold(),
        json.dumps(c[3].get("i") or [], ensure_ascii=False, separators=(",", ":")),
    ))
    return clean[0][0], clean[0][1], clean[0][2]


def collect_leaf_names(data: dict) -> set[str]:
    by_result: dict[str, list[dict]] = {}
    for recipe in data.get("recipes") or []:
        by_result.setdefault(str(recipe["r"]), []).append(recipe)
    names: set[str] = set()
    for index, name in enumerate(sorted(by_result, key=str.casefold), 1):
        leaves, _cycles, _steps = canonical_plan(name, 1, by_result)
        names.update(leaves)
        if index % 500 == 0:
            print(f"Audited {index}/{len(by_result)} craftables", file=sys.stderr)
    return names


def infer_pseudo_mode(name: str) -> str | None:
    if name.startswith("Any "):
        return "Hardmode" if any(hint in name for hint in HARDMODE_PSEUDO_HINTS) else "Pre-Hardmode"
    return None


def main() -> int:
    data = load_recipe_data()
    leaf_names = collect_leaf_names(data)
    item_modes = fetch_item_modes()
    rows: dict[str, list[object]] = {}
    unresolved: list[str] = []
    for name in sorted(leaf_names, key=str.casefold):
        pseudo = PSEUDO_AVAILABILITY.get(name)
        if pseudo:
            rows[name] = list(pseudo)
            continue
        if name in item_modes:
            mode = "Hardmode" if item_modes[name] else "Pre-Hardmode"
        else:
            mode = infer_pseudo_mode(name)
        if mode is None:
            unresolved.append(name)
            continue
        labels, rank = PROGRESSION_OVERRIDES.get(name, ([], 40 if mode == "Hardmode" else 10))
        # Some Wiki item rows omit/misstate the broad Hardmode flag even when a
        # known progression gate is unambiguously post-Wall-of-Flesh. The
        # explicit milestone wins in that case. Pre-Hardmode milestones use
        # ranks below 40 and keep their Pre-Hardmode badge.
        if rank >= 40 and name in PROGRESSION_OVERRIDES:
            mode = "Hardmode"
        rows[name] = [mode, labels, rank]
    if unresolved:
        print("Unresolved shopping-list names:", file=sys.stderr)
        for name in unresolved:
            print(f"  - {name}", file=sys.stderr)
        raise RuntimeError(f"{len(unresolved)} shopping-list leaves have no availability classification")
    OUT.write_text(
        "window.TERRARIA_GENERATED_AVAILABILITY="
        + json.dumps(rows, ensure_ascii=False, separators=(",", ":"))
        + ";\n",
        encoding="utf-8",
    )
    hard = sum(1 for row in rows.values() if row[0] == "Hardmode")
    print(f"Wrote {OUT}: {len(rows)} leaves ({hard} Hardmode, {len(rows)-hard} Pre-Hardmode)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
