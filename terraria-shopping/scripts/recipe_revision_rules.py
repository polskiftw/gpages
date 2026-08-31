#!/usr/bin/env python3
"""Shared rules for resolving Terraria Cargo recipe revisions.

An empty Recipes.version usually means a shared/current recipe. Occasionally the
Wiki retains an older unversioned recipe beside a newer explicit Desktop override.
For Desktop data, an explicit Desktop row wins over blank rows for the same result
and the same ingredient-name signature. Different ingredient-name signatures are
kept as legitimate alternate recipes.
"""
from __future__ import annotations

from collections.abc import Callable


def ingredient_signature(recipe: dict[str, object]) -> tuple[str, ...]:
    return tuple(sorted((str(name) for name, _qty in recipe.get("i") or []), key=str.casefold))


def revision_key(recipe: dict[str, object]) -> tuple[str, tuple[str, ...]]:
    return str(recipe.get("r") or ""), ingredient_signature(recipe)


def identity(recipe: dict[str, object]) -> tuple[object, ...]:
    return (
        str(recipe.get("r") or ""),
        int(recipe.get("a") or 1),
        str(recipe.get("s") or "By hand"),
        tuple((str(name), int(qty)) for name, qty in recipe.get("i") or []),
    )


def explicit_desktop(version: str) -> bool:
    return bool(str(version or "").strip()) and "desktop" in str(version).casefold()


def prefer_generated_recipes(
    recipes: list[dict[str, object]],
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    desktop_keys = {
        revision_key(recipe)
        for recipe in recipes
        if explicit_desktop(str(recipe.get("v") or ""))
    }
    kept: list[dict[str, object]] = []
    suppressed: list[dict[str, object]] = []
    for recipe in recipes:
        if not str(recipe.get("v") or "").strip() and revision_key(recipe) in desktop_keys:
            suppressed.append(recipe)
        else:
            kept.append(recipe)
    return kept, suppressed


def prefer_raw_rows(
    rows: list[dict[str, object]],
    *,
    eligible: Callable[[dict[str, object]], bool] | None = None,
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    eligible = eligible or (lambda _row: True)
    desktop_keys = {
        revision_key(row["recipe"])
        for row in rows
        if eligible(row) and explicit_desktop(str(row.get("version") or ""))
    }
    kept: list[dict[str, object]] = []
    suppressed: list[dict[str, object]] = []
    for row in rows:
        if (
            eligible(row)
            and not str(row.get("version") or "").strip()
            and revision_key(row["recipe"]) in desktop_keys
        ):
            suppressed.append(row)
        else:
            kept.append(row)
    return kept, suppressed
