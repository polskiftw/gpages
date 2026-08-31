#!/usr/bin/env python3
"""Audit Terraria Shopping against the live Official Terraria Wiki recipe table.

This is intentionally stricter than the generator. It verifies the generated
recipe graph against a fresh raw Cargo read, checks Desktop/platform metadata,
recomputes every flattened shopping list, and keeps explicit current-version
canaries for recently changed recipes so stale platform rows cannot silently win.
"""
from __future__ import annotations

import collections
import json
import math
import pathlib
import sys
import time

import build_data as bd

ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = ROOT / "data.generated.js"
AVAILABILITY = ROOT / "availability.generated.js"
CURRENT_DESKTOP = "1.4.5.8"
PAGE_SIZE = 500

BAD_SOURCE_WORDS = {"", "or", "and", "crafting", "by hand", "plundering", "looting", "drop", "drops", "source", "sources"}
BAD_TEXT_MARKERS = ("{{", "}}", "¦", "^", "#i:", "#n:")


def load_js(path: pathlib.Path, prefix: str) -> object:
    text = path.read_text(encoding="utf-8").strip()
    if not text.startswith(prefix) or not text.endswith(";"):
        raise RuntimeError(f"{path.name} has an unexpected JavaScript wrapper")
    return json.loads(text[len(prefix):-1])


def identity(recipe: dict[str, object]) -> tuple[object, ...]:
    return (
        str(recipe.get("r") or ""),
        int(recipe.get("a") or 1),
        str(recipe.get("s") or "By hand"),
        tuple((str(name), int(qty)) for name, qty in recipe.get("i") or []),
    )


def ingredients_key(recipe: dict[str, object]) -> tuple[tuple[str, int], ...]:
    return tuple(sorted((str(name), int(qty)) for name, qty in recipe.get("i") or []))


def raw_recipe(title: dict[str, object]) -> dict[str, object] | None:
    result = str(title.get("result") or "").strip()
    if not result:
        return None
    station = str(title.get("station") or "By hand").strip() or "By hand"
    try:
        amount = max(1, int(float(str(title.get("amount") or "1"))))
    except ValueError:
        amount = 1
    ingredients = bd.parse_args(str(title.get("args") or ""))
    if not ingredients:
        return None
    return {"r": result, "a": amount, "s": station, "i": ingredients}


def fetch_raw_cargo() -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    offset = 0
    while True:
        payload = bd.request_json({
            "action": "cargoquery",
            "tables": "Recipes",
            "fields": bd.FIELDS,
            "limit": PAGE_SIZE,
            "offset": offset,
            "format": "json",
            "formatversion": "2",
        })
        batch = payload.get("cargoquery") or []
        if not isinstance(batch, list):
            raise RuntimeError("Unexpected Cargo response during audit")
        if not batch:
            break
        for entry in batch:
            title = entry.get("title") or {}
            recipe = raw_recipe(title)
            if recipe is None:
                continue
            rows.append({
                "recipe": recipe,
                "version": str(title.get("version") or "").strip(),
                "legacy": str(title.get("legacy") or "").strip(),
            })
        offset += len(batch)
        print(f"Audit fetched {offset} raw Cargo rows", file=sys.stderr)
        if len(batch) < PAGE_SIZE:
            break
        time.sleep(0.08)
    return rows


def eligible_raw(row: dict[str, object]) -> bool:
    recipe = row["recipe"]
    assert isinstance(recipe, dict)
    legacy = str(row.get("legacy") or "").strip().casefold()
    if legacy == "yes":
        return False
    if str(recipe["s"]) in bd.SKIP_STATIONS:
        return False
    if str(recipe["r"]) in bd.SKIP_RESULTS:
        return False
    return bd.desktop_compatible(str(row.get("version") or ""))


def validate_structure(payload: dict[str, object]) -> list[dict[str, object]]:
    recipes = payload.get("recipes") or []
    if not isinstance(recipes, list):
        raise RuntimeError("recipes is not a list")
    if int(payload.get("recipeCount") or -1) != len(recipes):
        raise RuntimeError("recipeCount does not match the generated recipe array")
    if str(payload.get("game") or "") != f"Terraria Desktop {CURRENT_DESKTOP}":
        raise RuntimeError(
            f"generated game scope is {payload.get('game')!r}; expected Terraria Desktop {CURRENT_DESKTOP}"
        )

    seen: set[tuple[object, ...]] = set()
    for index, recipe in enumerate(recipes):
        if not isinstance(recipe, dict):
            raise RuntimeError(f"recipe #{index} is not an object")
        result = str(recipe.get("r") or "")
        station = str(recipe.get("s") or "")
        if not result or not station:
            raise RuntimeError(f"recipe #{index} has a blank result/station")
        if result in bd.SKIP_RESULTS:
            raise RuntimeError(f"legacy-only result leaked into generated data: {result}")
        if any(marker in result or marker in station for marker in BAD_TEXT_MARKERS):
            raise RuntimeError(f"unparsed markup leaked into {result!r} / {station!r}")
        amount = recipe.get("a")
        if not isinstance(amount, int) or isinstance(amount, bool) or amount < 1:
            raise RuntimeError(f"invalid result amount for {result}: {amount!r}")
        ingredients = recipe.get("i") or []
        if not isinstance(ingredients, list) or not ingredients:
            raise RuntimeError(f"recipe has no ingredients: {result}")
        for pair in ingredients:
            if not isinstance(pair, list) or len(pair) != 2:
                raise RuntimeError(f"malformed ingredient pair in {result}: {pair!r}")
            name, qty = pair
            if not str(name).strip() or not isinstance(qty, int) or isinstance(qty, bool) or qty < 1:
                raise RuntimeError(f"invalid ingredient in {result}: {pair!r}")
            if any(marker in str(name) for marker in BAD_TEXT_MARKERS):
                raise RuntimeError(f"unparsed ingredient markup in {result}: {name!r}")
            if str(name) in bd.INGREDIENT_ALIASES:
                raise RuntimeError(f"stale ingredient name leaked into {result}: {name}")
        key = identity(recipe)
        if key in seen:
            raise RuntimeError(f"duplicate generated recipe: {result} @ {station}")
        seen.add(key)
        version = str(recipe.get("v") or "")
        if version and "desktop" not in version.casefold():
            raise RuntimeError(f"non-Desktop explicit version leaked into {result}: {version!r}")
    return recipes


def validate_live_cargo(recipes: list[dict[str, object]], raw_rows: list[dict[str, object]]) -> None:
    generated = {identity(recipe) for recipe in recipes}
    raw_by_identity: dict[tuple[object, ...], list[dict[str, object]]] = collections.defaultdict(list)
    version_counts: collections.Counter[str] = collections.Counter()
    for row in raw_rows:
        raw_by_identity[identity(row["recipe"])].append(row)
        version_counts[str(row.get("version") or "<blank>")] += 1

    orphaned = []
    wrong_platform = []
    for recipe in recipes:
        key = identity(recipe)
        candidates = raw_by_identity.get(key, [])
        if not candidates:
            orphaned.append(recipe)
        elif not any(eligible_raw(row) for row in candidates):
            wrong_platform.append((recipe, candidates))
    if orphaned:
        names = ", ".join(str(r["r"]) for r in orphaned[:20])
        raise RuntimeError(f"{len(orphaned)} generated recipes no longer exist in live Cargo: {names}")
    if wrong_platform:
        names = ", ".join(str(r[0]["r"]) for r in wrong_platform[:20])
        raise RuntimeError(f"{len(wrong_platform)} generated recipes are backed only by legacy/non-Desktop rows: {names}")

    missing = []
    for row in raw_rows:
        if eligible_raw(row) and identity(row["recipe"]) not in generated:
            missing.append(row)
    if missing:
        details = "; ".join(
            f"{row['recipe']['r']} [{row.get('version') or 'shared'}]" for row in missing[:25]
        )
        raise RuntimeError(f"{len(missing)} current Desktop/shared Cargo recipes are missing from generated data: {details}")

    explicit_non_desktop = sum(
        1 for row in raw_rows
        if str(row.get("version") or "").strip() and not bd.desktop_compatible(str(row.get("version") or ""))
    )
    legacy = sum(1 for row in raw_rows if str(row.get("legacy") or "").strip().casefold() == "yes")
    print(
        f"Live Cargo parity clean: {len(generated)} generated identities; "
        f"{explicit_non_desktop} explicit non-Desktop rows rejected; {legacy} legacy rows rejected",
        file=sys.stderr,
    )
    print("Raw Cargo version labels:", file=sys.stderr)
    for label, count in version_counts.most_common():
        print(f"  {count:4d}  {label}", file=sys.stderr)


def find_recipes(recipes: list[dict[str, object]], result: str) -> list[dict[str, object]]:
    return [recipe for recipe in recipes if str(recipe["r"]) == result]


def require_recipe(
    recipes: list[dict[str, object]], result: str, amount: int,
    ingredients: dict[str, int], *, station_contains: str | None = None,
) -> None:
    wanted = tuple(sorted(ingredients.items()))
    matches = [
        recipe for recipe in find_recipes(recipes, result)
        if int(recipe["a"]) == amount and ingredients_key(recipe) == wanted
        and (station_contains is None or station_contains.casefold() in str(recipe["s"]).casefold())
    ]
    if not matches:
        actual = [
            (r["a"], r["s"], dict(ingredients_key(r)), r.get("v", "shared"))
            for r in find_recipes(recipes, result)
        ]
        raise RuntimeError(f"current-version sentinel failed for {result}; expected x{amount} {ingredients}, got {actual}")


def forbid_ingredient(recipes: list[dict[str, object]], result: str, ingredient: str) -> None:
    offenders = [r for r in find_recipes(recipes, result) if any(str(p[0]) == ingredient for p in r["i"])]
    if offenders:
        raise RuntimeError(f"stale recipe leaked for {result}: still uses {ingredient}")


def validate_current_version_canaries(recipes: list[dict[str, object]]) -> None:
    # Desktop 1.4.5.7: formula replaced entirely; Console/Mobile still expose the old row on Wiki pages.
    require_recipe(
        recipes, "Super Mana Potion", 8,
        {"Greater Mana Potion": 8, "Fallen Star": 2, "Ectoplasm": 1},
    )
    forbid_ingredient(recipes, "Super Mana Potion", "Unicorn Horn")
    forbid_ingredient(recipes, "Super Mana Potion", "Crystal Shard")

    # Desktop 1.4.5.7: Band of Starpower replaced Mana Regeneration Band here.
    require_recipe(recipes, "Magic Cuffs", 1, {"Band of Starpower": 1, "Shackle": 1})
    forbid_ingredient(recipes, "Magic Cuffs", "Mana Regeneration Band")

    # Desktop 1.4.5.0 recipe changes.
    require_recipe(recipes, "Endless Quiver", 1, {"Wooden Arrow": 9999}, station_contains="Crystal Ball")
    require_recipe(recipes, "Blue Phaseblade", 1, {"Meteorite Bar": 15, "Sapphire": 15})
    print(f"Current-version canaries clean for Desktop {CURRENT_DESKTOP}", file=sys.stderr)


def validate_shopping_lists(payload: dict[str, object], recipes: list[dict[str, object]]) -> None:
    availability = load_js(AVAILABILITY, "window.TERRARIA_GENERATED_AVAILABILITY=")
    if not isinstance(availability, dict):
        raise RuntimeError("availability.generated.js is not an object")

    by_result: dict[str, list[dict[str, object]]] = collections.defaultdict(list)
    for recipe in recipes:
        by_result[str(recipe["r"])].append(recipe)
    use_counts = bd.ingredient_use_counts(recipes)

    nodes = payload.get("nodes") or []
    if not isinstance(nodes, list):
        raise RuntimeError("nodes is not a list")
    if int(payload.get("craftableCount") or -1) != len(nodes):
        raise RuntimeError("craftableCount does not match node count")

    node_map = {str(node[0]): node for node in nodes}
    if set(node_map) != set(by_result):
        missing = sorted(set(by_result) - set(node_map), key=str.casefold)
        extra = sorted(set(node_map) - set(by_result), key=str.casefold)
        raise RuntimeError(f"node/result mismatch; missing={missing[:10]}, extra={extra[:10]}")

    all_leaves: set[str] = set()
    for index, name in enumerate(sorted(by_result, key=str.casefold), 1):
        leaves, cycles, _steps, cycle_to = bd.canonical_plan(name, 1, by_result, use_counts)
        if cycles or cycle_to or not leaves:
            raise RuntimeError(f"shopping planner did not terminate cleanly for {name}: cycles={cycles}, cycle_to={cycle_to!r}")
        if any(not isinstance(qty, int) or qty < 1 for qty in leaves.values()):
            raise RuntimeError(f"shopping planner produced invalid quantity for {name}: {leaves}")
        node = node_map[name]
        if int(node[1]) != len(leaves):
            raise RuntimeError(f"stored shopping-list count disagrees for {name}: node={node[1]}, recomputed={len(leaves)}")
        if int(node[4]) != len(by_result[name]):
            raise RuntimeError(f"stored recipe count disagrees for {name}: node={node[4]}, recomputed={len(by_result[name])}")
        all_leaves.update(leaves)
        if index % 500 == 0:
            print(f"Audited {index}/{len(by_result)} flattened shopping lists", file=sys.stderr)

    missing_availability = sorted(all_leaves - set(availability), key=str.casefold)
    if missing_availability:
        raise RuntimeError(
            f"{len(missing_availability)} shopping leaves lack availability/source rows: "
            + ", ".join(missing_availability[:30])
        )
    unused_availability = sorted(set(availability) - all_leaves, key=str.casefold)
    if unused_availability:
        raise RuntimeError(
            f"availability contains {len(unused_availability)} rows that no current shopping list can reach: "
            + ", ".join(unused_availability[:30])
        )
    bad_sources = []
    for name in sorted(all_leaves, key=str.casefold):
        row = availability[name]
        source = str((row or [None, None, ""])[2] or "").strip()
        if source.casefold() in BAD_SOURCE_WORDS:
            bad_sources.append((name, source))
    if bad_sources:
        raise RuntimeError(f"non-actionable shopping sources remain: {bad_sources[:30]}")

    expected_endpoints = {name for name in by_result if not any(name == str(pair[0]) for r in recipes for pair in r["i"])}
    stored_endpoints = {str(node[0]) for node in nodes if bool(node[2])}
    if stored_endpoints != expected_endpoints:
        raise RuntimeError("endpoint flags disagree with the current generated recipe graph")
    if int(payload.get("endpointCount") or -1) != len(stored_endpoints):
        raise RuntimeError("endpointCount does not match endpoint flags")

    print(
        f"Shopping-list audit clean: {len(by_result)} craftables fully flattened; "
        f"{len(all_leaves)} canonical acquisition leaves; {len(stored_endpoints)} endpoints",
        file=sys.stderr,
    )


def validate_reciprocals(recipes: list[dict[str, object]]) -> None:
    by_result: dict[str, list[dict[str, object]]] = collections.defaultdict(list)
    for recipe in recipes:
        by_result[str(recipe["r"])].append(recipe)
    use_counts = bd.ingredient_use_counts(recipes)
    ambiguous: set[tuple[str, str]] = set()
    for recipe in recipes:
        if bd.reciprocal_recipe_role(recipe, by_result, use_counts) == "ambiguous":
            other = bd.reciprocal_ingredient(recipe, by_result)
            if other:
                ambiguous.add(tuple(sorted((str(recipe["r"]), other), key=str.casefold)))
    allowed = {
        tuple(sorted(pair, key=str.casefold))
        for pair in (
            ("Conveyor Belt (Clockwise)", "Conveyor Belt (Counter Clockwise)"),
            ("Gold Coin", "Silver Coin"),
            ("Pine Tree Block", "Pine Wood"),
        )
    }
    if ambiguous != allowed:
        unexpected = sorted(ambiguous - allowed)
        vanished = sorted(allowed - ambiguous)
        raise RuntimeError(f"reciprocal ambiguity set changed; unexpected={unexpected}, vanished={vanished}")
    print(f"Reciprocal audit clean: {len(ambiguous)} known ambiguous pairs, no new pairs", file=sys.stderr)


def main() -> int:
    payload = load_js(DATA, "window.TERRARIA_RECIPE_DATA=")
    if not isinstance(payload, dict):
        raise RuntimeError("data.generated.js payload is not an object")
    recipes = validate_structure(payload)
    raw_rows = fetch_raw_cargo()
    validate_live_cargo(recipes, raw_rows)
    validate_current_version_canaries(recipes)
    validate_reciprocals(recipes)
    validate_shopping_lists(payload, recipes)
    print("ALL TERRARIA RECIPE + SHOPPING DATA AUDITS PASSED", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
