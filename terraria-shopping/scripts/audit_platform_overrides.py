#!/usr/bin/env python3
"""Verify blank-version recipe revisions are resolved by Desktop precedence."""
from __future__ import annotations

import audit_data as audit
import recipe_revision_rules as revisions


def compact(recipe: dict[str, object]) -> str:
    ing = ", ".join(f"{name} x{qty}" for name, qty in recipe.get("i") or [])
    return f"{recipe.get('r')} x{recipe.get('a')} @ {recipe.get('s')}: {ing}"


def main() -> int:
    raw = audit.fetch_raw_cargo()
    preferred, suppressed = revisions.prefer_raw_rows(raw, eligible=audit.eligible_raw)
    payload = audit.load_js(audit.DATA, "window.TERRARIA_RECIPE_DATA=")
    generated = payload.get("recipes") or []
    generated_ids = {revisions.identity(recipe) for recipe in generated}

    stale_survivors = [
        row for row in suppressed
        if revisions.identity(row["recipe"]) in generated_ids
    ]
    preferred_ids = {
        revisions.identity(row["recipe"])
        for row in preferred
        if audit.eligible_raw(row)
    }
    missing_preferred = [identity for identity in preferred_ids if identity not in generated_ids]

    if stale_survivors:
        print("Stale blank-version recipe revisions survived generation:")
        for row in stale_survivors:
            print(f"  {compact(row['recipe'])}")
        raise RuntimeError(f"{len(stale_survivors)} stale recipe revision(s) survived Desktop precedence")
    if missing_preferred:
        raise RuntimeError(f"{len(missing_preferred)} preferred Desktop/shared recipe(s) are missing from generated data")

    print(f"Platform override audit clean: resolved {len(suppressed)} stale blank-version revision(s)")
    for row in suppressed:
        print(f"  resolved: {compact(row['recipe'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
