#!/usr/bin/env python3
"""Apply direct-acquisition priorities to Terraria Shopping source badges.

Drops Cargo and Itemsource are exhaustive-ish data feeds, not UX ranking systems.
Terraria 1.4.5 in particular added resource slimes and many Shimmer routes that
can make a rare/secondary method displace the ordinary way a player obtains a
shopping-list material. This pass keeps the generated data truthful while making
"where do I go get this?" prefer direct world, harvest, vendor, or mechanic
sources over incidental drops/transmutations.

Keep this table limited to source semantics, never recipe semantics. The recipe
and shopping-list graph remain fully generated and independently audited.
"""
from __future__ import annotations

import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[1]
AVAILABILITY = ROOT / "availability.generated.js"
PREFIX = "window.TERRARIA_GENERATED_AVAILABILITY="

# These are deliberately concise primary acquisition routes. Secondary slime or
# Shimmer methods remain valid Terraria mechanics; they simply are not the most
# useful headline for a shopping-list row.
DIRECT_SOURCE_PRIORITIES: dict[str, str] = {
    # 1.4.5 resource-slime cases where Drops Cargo otherwise wins by accident.
    "Bomb": "Demolitionist / Skeleton Merchant / Pots / Chests",
    "Cloud": "Floating Islands (mine)",
    "Cobweb": "Underground / Spider Nest (harvest)",
    "Confetti": "Party Girl (NPC)",
    "Dart Trap": "Cavern / Dungeon traps (mine)",
    "Desert Fossil": "Underground Desert (mine)",
    "Dirt Block": "World terrain (mine)",
    "Gold Coin": "Enemy drops / selling items / loot",
    "Hive": "Bee Hive (mine)",
    "Honey Block": "Water + honey (contact)",
    "Life Crystal": "Underground / Cavern (mine)",
    "Marble Block": "Marble Cave (mine)",
    "Poo": "Well Fed + Toilet",
    "Rope": "Merchant / Skeleton Merchant / Pots / Chests",
    "Silver Coin": "Enemy drops / selling items / loot",
    "Spike": "Dungeon traps (mine)",
    "Wood": "Trees (chop)",

    # Ordinary world materials for which Itemsource currently promotes Shimmer.
    "Clay Block": "Underground deposits (mine)",
    "Crimsand Block": "Crimson Desert (mine)",
    "Ebonsand Block": "Corruption Desert (mine)",
    "Mushroom": "Forest grass (harvest)",
    "Sand Block": "Desert / Ocean (mine)",
}


def load() -> dict[str, list[object]]:
    text = AVAILABILITY.read_text(encoding="utf-8").strip()
    if not text.startswith(PREFIX) or not text.endswith(";"):
        raise RuntimeError("availability.generated.js has an unexpected wrapper")
    payload = json.loads(text[len(PREFIX):-1])
    if not isinstance(payload, dict):
        raise RuntimeError("availability payload is not an object")
    return payload


def main() -> int:
    rows = load()
    changed = 0
    missing: list[str] = []
    for name, source in DIRECT_SOURCE_PRIORITIES.items():
        row = rows.get(name)
        if not isinstance(row, list) or len(row) < 4:
            missing.append(name)
            continue
        old = str(row[2] or "")
        if old != source:
            row[2] = source
            changed += 1
            print(f"Source priority: {name}: {old!r} -> {source!r}")

    if missing:
        raise RuntimeError(
            "Direct-source priority item(s) disappeared from canonical shopping leaves: "
            + ", ".join(sorted(missing, key=str.casefold))
        )

    AVAILABILITY.write_text(
        PREFIX + json.dumps(rows, ensure_ascii=False, separators=(",", ":")) + ";\n",
        encoding="utf-8",
    )
    print(f"Applied direct acquisition priority to {len(DIRECT_SOURCE_PRIORITIES)} canonical leaves; {changed} changed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
