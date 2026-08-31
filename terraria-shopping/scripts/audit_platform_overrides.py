#!/usr/bin/env python3
"""Find blank-version recipe revisions superseded by explicit Desktop rows.

Blank Cargo versions usually mean a shared/current recipe. They are unsafe when
an explicit Desktop row exists for the same result with the same ingredient-name
signature but different quantities, output, or crafting station: that is the
shape produced by a platform/version recipe revision (for example Stink Potion
in Desktop 1.4.5.0), not merely a legitimate alternative recipe.
"""
from __future__ import annotations

import collections

import audit_data as audit


def ingredient_signature(recipe: dict[str, object]) -> tuple[str, ...]:
    return tuple(sorted((str(name) for name, _qty in recipe.get("i") or []), key=str.casefold))


def compact(identity: tuple[object, ...]) -> str:
    _result, amount, station, ingredients = identity
    ing = ", ".join(f"{name} x{qty}" for name, qty in ingredients)
    return f"x{amount} @ {station}: {ing}"


def main() -> int:
    raw = audit.fetch_raw_cargo()
    groups: dict[tuple[str, tuple[str, ...]], list[dict[str, object]]] = collections.defaultdict(list)
    for row in raw:
        if not audit.eligible_raw(row):
            continue
        recipe = row["recipe"]
        assert isinstance(recipe, dict)
        groups[(str(recipe["r"]), ingredient_signature(recipe))].append(row)

    suspicious: list[tuple[str, tuple[str, ...], set[tuple[object, ...]], set[tuple[object, ...]]]] = []
    for (result, signature), rows in groups.items():
        blank_ids = {
            audit.identity(row["recipe"])
            for row in rows
            if not str(row.get("version") or "").strip()
        }
        desktop_ids = {
            audit.identity(row["recipe"])
            for row in rows
            if "desktop" in str(row.get("version") or "").casefold()
        }
        # Identical rows are harmless duplicates. Different identities with the
        # same ingredients indicate a changed quantity/output/station revision.
        if blank_ids and desktop_ids and blank_ids != desktop_ids:
            suspicious.append((result, signature, blank_ids, desktop_ids))

    if suspicious:
        print("Blank-version recipe revisions competing with explicit Desktop rows:")
        for result, signature, blank_ids, desktop_ids in sorted(suspicious, key=lambda x: x[0].casefold()):
            print(f"  {result} -- ingredients: {', '.join(signature)}")
            for identity in sorted(blank_ids, key=str):
                print(f"    stale/shared?: {compact(identity)}")
            for identity in sorted(desktop_ids, key=str):
                print(f"    Desktop:      {compact(identity)}")
        raise RuntimeError(
            f"{len(suspicious)} recipe revision conflict(s) need Desktop precedence before publishing"
        )

    print("Platform override audit clean: 0 blank/Desktop recipe revision conflicts")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
