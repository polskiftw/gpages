#!/usr/bin/env python3
"""Audit hand-curated Terraria Shopping project lists against current Desktop recipes.

The browser deliberately uses curated component frontiers for a small number of
high-value projects instead of always choosing the generator's one canonical
flattened route. A curated list is valid when its exact components can be consumed
by *some* current Desktop recipe path for the target project. This matters for
items with multiple modern recipes, such as Bundle of Horseshoe Balloons.
"""
from __future__ import annotations

import itertools
import json
import math
import pathlib
import re
import sys
from collections import defaultdict

import build_data as bd

ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = ROOT / "data.generated.js"
PROJECTS = ROOT / "enrichment-projects.js"
ITEM_FILES = [ROOT / f"enrichment-items-{n}.js" for n in range(1, 5)]

# Curated UI concepts that intentionally stand for recipe alternatives/categories.
# Each value is a list of current graph names that can satisfy one unit.
NAME_ALTERNATIVES: dict[str, list[str]] = {
    "Gold / Platinum Bar": ["Any Gold Bar", "Gold Bar", "Platinum Bar"],
    "Iron / Lead Bar": ["Any Iron Bar", "Iron Bar", "Lead Bar"],
    "Magic Mirror or Ice Mirror": ["Any Magic Mirror", "Magic Mirror", "Ice Mirror"],
    "Sprint boots (one pair)": ["Hermes Boots", "Flurry Boots", "Sailfish Boots", "Dunerider Boots"],
}


def load_js_json(path: pathlib.Path) -> object:
    text = path.read_text(encoding="utf-8").strip()
    if path.name == "data.generated.js":
        prefix = "window.TERRARIA_RECIPE_DATA="
        if not text.startswith(prefix) or not text.endswith(";"):
            raise RuntimeError(f"unexpected wrapper in {path.name}")
        return json.loads(text[len(prefix):-1])
    if path.name == "enrichment-projects.js":
        marker = "window.TERRARIA_ENRICHMENT.projects="
        start = text.find(marker)
        if start < 0:
            raise RuntimeError(f"could not find projects assignment in {path.name}")
        raw = text[start + len(marker):].strip()
        if raw.endswith(";"):
            raw = raw[:-1]
        return json.loads(raw)
    marker = "Object.assign(window.TERRARIA_ENRICHMENT.items,"
    start = text.find(marker)
    if start < 0:
        match = re.search(r"Object\.assign\([^,]+,\s*(\{.*\})\s*\);?\s*$", text, flags=re.S)
        if not match:
            raise RuntimeError(f"could not find item assignment in {path.name}")
        raw = match.group(1)
    else:
        raw = text[start + len(marker):].strip()
        if raw.endswith(");"):
            raw = raw[:-2]
    return json.loads(raw)


def fmt(mapping: dict[str, int]) -> str:
    return ", ".join(
        f"{name} x{qty}" for name, qty in sorted(mapping.items(), key=lambda pair: pair[0].casefold()) if qty
    ) or "(none)"


def frontier_variants(
    project: dict[str, object], item_defs: dict[str, dict[str, object]]
) -> tuple[list[tuple[dict[str, int], list[tuple[str, str]]]], list[str]]:
    fixed: dict[str, int] = {}
    choices: list[tuple[str, int, list[str]]] = []
    unresolved: list[str] = []
    for item_id, raw_qty in project.get("items") or []:
        qty = max(1, int(raw_qty))
        info = item_defs.get(str(item_id))
        if not info:
            unresolved.append(f"missing item id {item_id!r}")
            continue
        label = str(info.get("name") or item_id)
        alternatives = NAME_ALTERNATIVES.get(label, [label])
        if len(alternatives) == 1:
            fixed[alternatives[0]] = fixed.get(alternatives[0], 0) + qty
        else:
            choices.append((label, qty, alternatives))

    combinations = math.prod(len(alts) for _label, _qty, alts in choices) if choices else 1
    if combinations > 20000:
        unresolved.append(f"too many curated alternative combinations ({combinations})")
        return [], unresolved

    variants: list[tuple[dict[str, int], list[tuple[str, str]]]] = []
    pools = [alts for _label, _qty, alts in choices]
    for selected in itertools.product(*pools) if pools else [()]:
        inventory = dict(fixed)
        selected_labels: list[tuple[str, str]] = []
        for (label, qty, _alts), chosen in zip(choices, selected):
            inventory[chosen] = inventory.get(chosen, 0) + qty
            selected_labels.append((label, chosen))
        variants.append((inventory, selected_labels))
    return variants, unresolved


def consume_current_route(
    name: str,
    qty: int,
    inventory: dict[str, int],
    by_result: dict[str, list[dict[str, object]]],
    use_counts: dict[str, int],
    stack: frozenset[str] = frozenset(),
    depth: int = 0,
) -> dict[str, int] | None:
    """Consume a curated frontier through any current forward recipe route."""
    if qty <= 0:
        return dict(inventory)

    available = inventory.get(name, 0)
    if available >= qty:
        out = dict(inventory)
        left = available - qty
        if left:
            out[name] = left
        else:
            out.pop(name, None)
        return out

    if depth > 48 or name in stack:
        return None
    options = bd.planning_options(name, by_result, use_counts)
    if not options:
        return None

    next_stack = stack | {name}
    for recipe in options:
        batches = math.ceil(qty / max(1, int(recipe.get("a") or 1)))
        states = [dict(inventory)]
        for ingredient, raw_amount in recipe.get("i") or []:
            needed = max(1, int(raw_amount)) * batches
            next_states: list[dict[str, int]] = []
            for state in states:
                resolved = consume_current_route(
                    str(ingredient), needed, state, by_result, use_counts, next_stack, depth + 1
                )
                if resolved is not None:
                    next_states.append(resolved)
            states = next_states
            if not states:
                break
        for state in states:
            return state
    return None


def current_route_matches_frontier(
    project_name: str,
    frontier: dict[str, int],
    by_result: dict[str, list[dict[str, object]]],
    use_counts: dict[str, int],
) -> tuple[bool, dict[str, int] | None]:
    remainder = consume_current_route(project_name, 1, frontier, by_result, use_counts)
    if remainder is None:
        return False, None
    clean = {name: qty for name, qty in remainder.items() if qty}
    return not clean, clean


def main() -> int:
    payload = load_js_json(DATA)
    if not isinstance(payload, dict):
        raise RuntimeError("data.generated.js payload is not an object")
    recipes = payload.get("recipes") or []
    if not isinstance(recipes, list):
        raise RuntimeError("generated recipes is not a list")
    by_result: dict[str, list[dict[str, object]]] = defaultdict(list)
    all_names: set[str] = set()
    for recipe in recipes:
        by_result[str(recipe["r"])].append(recipe)
        all_names.add(str(recipe["r"]))
        all_names.update(str(pair[0]) for pair in recipe.get("i") or [])
    use_counts = bd.ingredient_use_counts(recipes)

    item_defs: dict[str, dict[str, object]] = {}
    for path in ITEM_FILES:
        part = load_js_json(path)
        if not isinstance(part, dict):
            raise RuntimeError(f"{path.name} did not contain an item object")
        item_defs.update(part)
    projects = load_js_json(PROJECTS)
    if not isinstance(projects, list):
        raise RuntimeError("enrichment-projects.js did not contain a project array")

    referenced_ids = {str(item_id) for project in projects for item_id, _qty in project.get("items") or []}
    missing_ids = sorted(referenced_ids - set(item_defs), key=str.casefold)
    if missing_ids:
        raise RuntimeError("curated projects reference missing enrichment item IDs: " + ", ".join(missing_ids))

    errors: list[str] = []
    virtual: list[str] = []
    for project in projects:
        name = str(project.get("name") or "")
        if not name:
            errors.append("curated project with blank name")
            continue

        variants, unresolved = frontier_variants(project, item_defs)
        if unresolved:
            errors.append(f"{name}: {'; '.join(unresolved)}")
            continue
        if name not in by_result:
            virtual.append(name)
            unknown = sorted(
                {
                    item_name
                    for frontier, _choices in variants[:1]
                    for item_name in frontier
                    if item_name not in all_names
                },
                key=str.casefold,
            )
            if unknown:
                errors.append(f"{name}: virtual list contains names absent from current recipe universe: {unknown}")
            print(f"CURATED VIRTUAL: {name} -- {fmt(variants[0][0])}")
            continue

        matched = False
        best_remainder: dict[str, int] | None = None
        winning_choices: list[tuple[str, str]] = []
        winning_frontier: dict[str, int] = {}
        for frontier, choices in variants:
            ok, remainder = current_route_matches_frontier(name, frontier, by_result, use_counts)
            if ok:
                matched = True
                winning_choices = choices
                winning_frontier = frontier
                break
            if remainder is not None and (best_remainder is None or sum(remainder.values()) < sum(best_remainder.values())):
                best_remainder = remainder

        canonical, cycles, _steps, cycle_to = bd.canonical_plan(name, 1, by_result, use_counts)
        print(f"CURATED PROJECT: {name}")
        if winning_choices:
            print("  chosen alternatives: " + "; ".join(f"{label} -> {choice}" for label, choice in winning_choices))
        if matched:
            print(f"  curated frontier : {fmt(winning_frontier)}")
            print("  current route    : VALID")
        else:
            example = variants[0][0] if variants else {}
            print(f"  curated frontier : {fmt(example)}")
            print(f"  unconsumed       : {fmt(best_remainder or {})}")
            print("  current route    : INVALID")
            errors.append(f"{name}: curated components cannot exactly craft the project through any current Desktop route")
        if not cycles and not cycle_to and canonical:
            print(f"  canonical route  : {fmt(canonical)}")

    print(f"Curated project inventory: {len(projects)} total; {len(virtual)} virtual/non-craftable bundles")
    if virtual:
        print("Virtual bundles: " + ", ".join(virtual))
    if errors:
        print("Curated project audit failures:", file=sys.stderr)
        for error in errors:
            print("  - " + error, file=sys.stderr)
        raise RuntimeError(f"{len(errors)} curated project validation failure(s)")
    print("ALL CURATED TERRARIA PROJECT LISTS ARE VALID CURRENT-DESKTOP CRAFTING FRONTIERS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
