#!/usr/bin/env python3
"""Apply Desktop-specific recipe revision precedence to generated Terraria data."""
from __future__ import annotations

import json
import pathlib
import sys

import build_data as bd
import recipe_revision_rules as revisions

ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = ROOT / "data.generated.js"
PREFIX = "window.TERRARIA_RECIPE_DATA="


def load() -> dict[str, object]:
    text = DATA.read_text(encoding="utf-8").strip()
    if not text.startswith(PREFIX) or not text.endswith(";"):
        raise RuntimeError("data.generated.js has an unexpected wrapper")
    payload = json.loads(text[len(PREFIX):-1])
    if not isinstance(payload, dict):
        raise RuntimeError("data.generated.js payload is not an object")
    return payload


def main() -> int:
    payload = load()
    recipes = payload.get("recipes") or []
    if not isinstance(recipes, list):
        raise RuntimeError("generated recipes is not a list")

    kept, suppressed = revisions.prefer_generated_recipes(recipes)
    for recipe in suppressed:
        print(
            "Suppressed stale blank-version recipe in favor of explicit Desktop revision: "
            f"{recipe['r']} x{recipe['a']} @ {recipe['s']} -- {recipe['i']}",
            file=sys.stderr,
        )

    # Recompute every derived graph metric after filtering, rather than trying to
    # patch counts by hand. This guarantees browser shopping-list metadata matches
    # the exact recipe set written below.
    nodes = bd.build_nodes(kept)
    payload["recipes"] = kept
    payload["recipeCount"] = len(kept)
    payload["craftableCount"] = len({str(recipe["r"]) for recipe in kept})
    payload["endpointCount"] = sum(1 for node in nodes if node[2])
    payload["nodes"] = nodes

    DATA.write_text(
        PREFIX + json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + ";\n",
        encoding="utf-8",
    )
    print(
        f"Desktop recipe revision precedence: suppressed {len(suppressed)} stale/shared row(s); "
        f"kept {len(kept)} recipes",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
