#!/usr/bin/env python3
"""Audit hand-curated Terraria Shopping project lists against current Desktop recipes.

The browser deliberately uses curated project frontiers for a small number of
high-value projects instead of flattening them all the way to raw acquisition
leaves. This audit expands those curated components through the current resolved
Desktop graph and checks that they are still equivalent to a valid/current route.
"""
from __future__ import annotations

import itertools
import json
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
        # Older split files may use a compact alias but remain data-only.
        match = re.search(r"Object\.assign\([^,]+,\s*(\{.*\})\s*\);?\s*$", text, flags=re.S)
        if not match:
            raise RuntimeError(f"could not find item assignment in {path.name}")
        raw = match.group(1)
    else:
        raw = text[start + len(marker):].strip()
        if raw.endswith(");"):
            raw = raw[:-2]
    return json.loads(raw)


def add_map(into: dict[str, int], source: dict[str, int]) -> None:
    for name, qty in source.items():
        into[name] = into.get(name, 0) + qty


def scaled(source: dict[str, int], qty: int) -> dict[str, int]:
    return {name: amount * qty for name, amount in source.items()}


def score(candidate: dict[str, int], target: dict[str, int]) -> tuple[int, int, int]:
    names = set(candidate) | set(target)
    missing_or_extra_names = sum(1 for name in names if bool(candidate.get(name)) != bool(target.get(name)))
    quantity_distance = sum(abs(candidate.get(name, 0) - target.get(name, 0)) for name in names)
    total_distance = abs(sum(candidate.values()) - sum(target.values()))
    return missing_or_extra_names, quantity_distance, total_distance


def best_curated_expansion(
    project: dict[str, object],
    item_defs: dict[str, dict[str, object]],
    by_result: dict[str, list[dict[str, object]]],
    use_counts: dict[str, int],
    target: dict[str, int],
) -> tuple[dict[str, int], list[tuple[str, str]], list[str]]:
    fixed: dict[str, int] = {}
    choice_groups: list[tuple[str, int, list[tuple[str, dict[str, int]]]]] = []
    unresolved: list[str] = []

    for item_id, raw_qty in project.get("items") or []:
        qty = max(1, int(raw_qty))
        info = item_defs.get(str(item_id))
        if not info:
            unresolved.append(f"missing item id {item_id!r}")
            continue
        label = str(info.get("name") or item_id)
        alternatives = NAME_ALTERNATIVES.get(label, [label])
        viable: list[tuple[str, dict[str, int]]] = []
        for name in alternatives:
            if name not in by_result:
                # A non-craftable current acquisition item is itself a valid leaf.
                viable.append((name, {name: qty}))
                continue
            leaves, cycles, _steps, cycle_to = bd.canonical_plan(name, qty, by_result, use_counts)
            if not cycles and not cycle_to and leaves:
                viable.append((name, leaves))
        if not viable:
            unresolved.append(f"{item_id}={label!r} has no current graph/leaf interpretation")
        elif len(viable) == 1:
            add_map(fixed, viable[0][1])
        else:
            choice_groups.append((label, qty, viable))

    # Curated lists only have a handful of true alternatives. Brute force their
    # combinations and choose the expansion closest to the target route.
    combinations = 1
    for _label, _qty, viable in choice_groups:
        combinations *= len(viable)
    if combinations > 20000:
        unresolved.append(f"too many curated alternative combinations ({combinations})")
        return fixed, [], unresolved

    best_map = fixed
    best_choices: list[tuple[str, str]] = []
    best_score = score(fixed, target)
    pools = [group[2] for group in choice_groups]
    for selected in itertools.product(*pools) if pools else [()]:
        merged = dict(fixed)
        choices: list[tuple[str, str]] = []
        for (label, _qty, _viable), (chosen_name, leaves) in zip(choice_groups, selected):
            add_map(merged, leaves)
            choices.append((label, chosen_name))
        current = score(merged, target)
        if current < best_score:
            best_map, best_choices, best_score = merged, choices, current
    return best_map, best_choices, unresolved


def fmt(mapping: dict[str, int]) -> str:
    return ", ".join(f"{name} x{qty}" for name, qty in sorted(mapping.items(), key=lambda x: x[0].casefold())) or "(none)"


def main() -> int:
    payload = load_js_json(DATA)
    assert isinstance(payload, dict)
    recipes = payload.get("recipes") or []
    assert isinstance(recipes, list)
    by_result: dict[str, list[dict[str, object]]] = defaultdict(list)
    for recipe in recipes:
        by_result[str(recipe["r"])].append(recipe)
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

    mismatches: list[str] = []
    virtual: list[str] = []
    for project in projects:
        name = str(project.get("name") or "")
        if not name:
            mismatches.append("curated project with blank name")
            continue
        if name not in by_result:
            # Deliberate non-craftable shopping bundles are allowed, but every
            # listed item still has to resolve to a current item/concept.
            virtual.append(name)
            bad = []
            for item_id, _qty in project.get("items") or []:
                label = str(item_defs[str(item_id)].get("name") or item_id)
                alternatives = NAME_ALTERNATIVES.get(label, [label])
                if not any(alt in by_result or alt for alt in alternatives):
                    bad.append(label)
            if bad:
                mismatches.append(f"{name}: virtual list has unresolved items {bad}")
            print(f"CURATED VIRTUAL: {name} ({len(project.get('items') or [])} entries)")
            continue

        target, cycles, _steps, cycle_to = bd.canonical_plan(name, 1, by_result, use_counts)
        if cycles or cycle_to or not target:
            mismatches.append(f"{name}: current generated target route did not resolve cleanly")
            continue
        curated, choices, unresolved = best_curated_expansion(project, item_defs, by_result, use_counts, target)
        current_score = score(curated, target)
        print(f"CURATED PROJECT: {name}")
        if choices:
            print("  chosen alternatives: " + "; ".join(f"{label} -> {choice}" for label, choice in choices))
        print(f"  target expansion : {fmt(target)}")
        print(f"  curated expansion: {fmt(curated)}")
        print(f"  diff score       : {current_score}")
        if unresolved:
            for problem in unresolved:
                print(f"  unresolved       : {problem}")
        if unresolved or current_score != (0, 0, 0):
            mismatches.append(
                f"{name}: score={current_score}; unresolved={unresolved}; target=[{fmt(target)}]; curated=[{fmt(curated)}]"
            )

    print(f"Curated project inventory: {len(projects)} total; {len(virtual)} virtual/non-craftable bundles")
    if virtual:
        print("Virtual bundles: " + ", ".join(virtual))
    if mismatches:
        print("Curated project mismatches needing review:", file=sys.stderr)
        for mismatch in mismatches:
            print("  - " + mismatch, file=sys.stderr)
        raise RuntimeError(f"{len(mismatches)} curated project list(s) differ from current Desktop expansion")
    print("ALL CURATED TERRARIA PROJECT LISTS MATCH CURRENT DESKTOP RECIPE EXPANSIONS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
