#!/usr/bin/env python3
"""Find suspicious blank-version recipe rows that compete with explicit Desktop rows.

Cargo uses a blank version for normal shared/current recipes, but historical
platform overrides can coexist for the same result. A blank row plus a different
explicit Desktop row is the dangerous shape: both pass the normal Desktop filter,
so this audit requires us to account for every such result explicitly.
"""
from __future__ import annotations

import collections

import audit_data as audit


def main() -> int:
    raw = audit.fetch_raw_cargo()
    by_result: dict[str, list[dict[str, object]]] = collections.defaultdict(list)
    for row in raw:
        recipe = row["recipe"]
        assert isinstance(recipe, dict)
        by_result[str(recipe["r"])].append(row)

    suspicious: list[tuple[str, set[tuple[object, ...]], set[tuple[object, ...]]]] = []
    for result, rows in by_result.items():
        blank_ids = {
            audit.identity(row["recipe"])
            for row in rows
            if not str(row.get("version") or "").strip() and audit.eligible_raw(row)
        }
        desktop_ids = {
            audit.identity(row["recipe"])
            for row in rows
            if "desktop" in str(row.get("version") or "").casefold() and audit.eligible_raw(row)
        }
        if blank_ids and desktop_ids and blank_ids != desktop_ids:
            suspicious.append((result, blank_ids, desktop_ids))

    if suspicious:
        print("Blank-version rows competing with different explicit Desktop rows:")
        for result, blank_ids, desktop_ids in sorted(suspicious, key=lambda x: x[0].casefold()):
            def compact(ids: set[tuple[object, ...]]) -> list[str]:
                out = []
                for _r, amount, station, ingredients in sorted(ids, key=str):
                    ing = ", ".join(f"{name} x{qty}" for name, qty in ingredients)
                    out.append(f"x{amount} @ {station}: {ing}")
                return out
            print(f"  {result}")
            for text in compact(blank_ids):
                print(f"    shared?:  {text}")
            for text in compact(desktop_ids):
                print(f"    Desktop:  {text}")
        raise RuntimeError(
            f"{len(suspicious)} result(s) have ambiguous blank/Desktop recipe overrides; "
            "classify them before publishing"
        )

    print("Platform override audit clean: 0 ambiguous blank/Desktop recipe competitions")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
