#!/usr/bin/env python3
"""Run the full Terraria Shopping audit against the resolved Desktop recipe set."""
from __future__ import annotations

import collections

import audit_data as audit
import build_data as bd
import recipe_revision_rules as revisions


def validate_revision_canaries(recipes: list[dict[str, object]]) -> None:
    # Desktop 1.4.5.0 changed Stink Potion from 1 output / 1 water to
    # 2 output / 2 water. Cargo currently retains the old row unversioned.
    audit.require_recipe(
        recipes,
        "Stink Potion",
        2,
        {"Bottled Water": 2, "Stinkfish": 1, "Deathweed": 1},
        station_contains="Placed Bottle",
    )
    stale_stink = [
        recipe for recipe in audit.find_recipes(recipes, "Stink Potion")
        if revisions.ingredient_signature(recipe) == ("Bottled Water", "Deathweed", "Stinkfish")
        and (
            int(recipe.get("a") or 1) != 2
            or dict(audit.ingredients_key(recipe)).get("Bottled Water") != 2
        )
    ]
    if stale_stink:
        raise RuntimeError(f"stale pre-1.4.5 Stink Potion recipe survived Desktop precedence: {stale_stink}")

    # Additional 1.4.5.0 canaries independently documented on the current Wiki.
    audit.require_recipe(
        recipes, "Unholy Arrow", 20,
        {"Wooden Arrow": 20, "Worm Tooth": 1},
        station_contains="Anvil",
    )
    audit.require_recipe(
        recipes, "Unholy Arrow", 10,
        {"Wooden Arrow": 10, "Vertebra": 1},
        station_contains="Anvil",
    )
    audit.require_recipe(
        recipes, "Gold Watch", 1,
        {"Gold Bar": 10, "Chain": 1},
        station_contains="Work Bench",
    )
    print("Desktop 1.4.5 revision canaries clean (Stink Potion, Unholy Arrow, Gold Watch)")


def validate_recipe_chain_canaries(recipes: list[dict[str, object]]) -> None:
    """Catch parser/planner bugs that recipe-table parity alone cannot detect."""
    # Reef Piano is a compact test of three things at once:
    #   * a normal multi-ingredient parent recipe;
    #   * an intermediate whose useful recipe outputs a 15-item batch; and
    #   * competing reversible Platform/Wall recipes that must not be followed
    #     backward when flattening a shopping list.
    audit.require_recipe(
        recipes,
        "Reef Piano",
        1,
        {"Reef Block": 15, "Bone": 4, "Book": 1},
        station_contains="Sawmill",
    )
    audit.require_recipe(
        recipes,
        "Reef Block",
        15,
        {"Stone Block": 15, "Coral": 1, "Any Seashell or Starfish": 1},
    )

    by_result: dict[str, list[dict[str, object]]] = collections.defaultdict(list)
    for recipe in recipes:
        by_result[str(recipe["r"])].append(recipe)
    leaves, cycles, _steps, cycle_to = bd.canonical_plan(
        "Reef Piano", 1, by_result, bd.ingredient_use_counts(recipes)
    )
    expected = {
        "Stone Block": 15,
        "Coral": 1,
        "Any Seashell or Starfish": 1,
        "Bone": 4,
        "Book": 1,
    }
    if cycles or cycle_to or leaves != expected:
        raise RuntimeError(
            "Reef Piano flattened shopping-list sentinel failed; "
            f"expected {expected}, got {leaves}, cycles={cycles}, cycle_to={cycle_to!r}"
        )
    print("Recipe-chain canary clean (Reef Piano -> Reef Block batch -> acquisition leaves)")


def main() -> int:
    payload = audit.load_js(audit.DATA, "window.TERRARIA_RECIPE_DATA=")
    if not isinstance(payload, dict):
        raise RuntimeError("data.generated.js payload is not an object")
    recipes = audit.validate_structure(payload)

    raw_rows = audit.fetch_raw_cargo()
    preferred_raw, suppressed_raw = revisions.prefer_raw_rows(raw_rows, eligible=audit.eligible_raw)
    if suppressed_raw:
        print(
            f"Desktop precedence removed {len(suppressed_raw)} stale blank-version raw recipe revision(s) from parity expectations"
        )
        for row in suppressed_raw:
            recipe = row["recipe"]
            print(f"  suppressed raw: {recipe['r']} x{recipe['a']} @ {recipe['s']} -- {recipe['i']}")

    audit.validate_live_cargo(recipes, preferred_raw)
    audit.validate_current_version_canaries(recipes)
    validate_revision_canaries(recipes)
    validate_recipe_chain_canaries(recipes)
    audit.validate_reciprocals(recipes)
    audit.validate_shopping_lists(payload, recipes)
    print("ALL RESOLVED DESKTOP TERRARIA RECIPE + SHOPPING DATA AUDITS PASSED")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
