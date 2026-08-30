#!/usr/bin/env python3
"""Build the Terraria Shopping recipe graph from the Official Terraria Wiki Cargo API.

The generated file is intentionally data-only so the GitHub Pages app can derive
terminal craftables, dependency counts, canonical shopping lists, and recipe trees
without hand-maintaining thousands of recipes.
"""
from __future__ import annotations

import datetime as dt
import json
import pathlib
import sys
import time
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
OUT = pathlib.Path(__file__).resolve().parents[1] / "data.generated.js"
PAGE_SIZE = 500
USER_AGENT = "polskiftw/gpages terraria-shopping recipe-catalog/2.0 (GitHub Pages data refresh)"
FIELDS = "result,resultid,amount,version,station,args,legacy"
SKIP_STATIONS = {"Shimmer", "Chlorophyte Extractinator"}


def request_json(params: dict[str, object], retries: int = 4) -> dict:
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(
        url,
        headers={
            "User-Agent": USER_AGENT,
            "Accept": "application/json",
        },
    )
    last: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=45) as response:
                raw = response.read()
                encoding = response.headers.get("Content-Encoding", "").lower()
                if encoding == "gzip":
                    import gzip
                    raw = gzip.decompress(raw)
                return json.loads(raw.decode("utf-8"))
        except Exception as exc:  # pragma: no cover - network-only path
            last = exc
            if attempt + 1 < retries:
                time.sleep(2 ** attempt)
    raise RuntimeError(f"Cargo request failed after {retries} attempts: {last}")


def clean_ingredient_name(value: str) -> str:
    # Recipe registration supports image/note suffixes such as #i:old / #n:note.
    # They are presentation metadata, not part of the item/group name.
    value = value.strip()
    for marker in ("#i:", "#n:"):
        if marker in value:
            value = value.split(marker, 1)[0].strip()
    return value


def parse_args(value: str) -> list[list[object]]:
    ingredients: list[list[object]] = []
    if not value:
        return ingredients
    for part in value.split("^"):
        part = part.strip()
        if not part:
            continue
        # Cargo's recipe args use BROKEN BAR (U+00A6) between name and amount.
        if "¦" in part:
            name, amount = part.rsplit("¦", 1)
        else:
            name, amount = part, "1"
        name = clean_ingredient_name(name)
        if not name:
            continue
        try:
            qty = int(float(amount.strip() or "1"))
        except ValueError:
            qty = 1
        ingredients.append([name, max(1, qty)])
    return ingredients


def desktop_compatible(version: str) -> bool:
    version = (version or "").strip().lower()
    if not version:
        return True
    # Current Cargo version strings are platform sets (for example
    # "console-desktop"). Keep anything explicitly including desktop.
    return "desktop" in version


def fetch_recipes() -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    offset = 0
    while True:
        payload = request_json(
            {
                "action": "cargoquery",
                "tables": "Recipes",
                "fields": FIELDS,
                "limit": PAGE_SIZE,
                "offset": offset,
                "format": "json",
                "formatversion": "2",
            }
        )
        batch = payload.get("cargoquery") or []
        if not isinstance(batch, list):
            raise RuntimeError("Unexpected Cargo response: cargoquery is not a list")
        if not batch:
            break
        for entry in batch:
            title = entry.get("title") or {}
            if str(title.get("legacy", "no")).lower() == "yes":
                continue
            station = str(title.get("station") or "By hand").strip() or "By hand"
            if station in SKIP_STATIONS:
                continue
            version = str(title.get("version") or "").strip()
            if not desktop_compatible(version):
                continue
            result = str(title.get("result") or "").strip()
            if not result:
                continue
            try:
                amount = int(float(str(title.get("amount") or "1")))
            except ValueError:
                amount = 1
            ingredients = parse_args(str(title.get("args") or ""))
            if not ingredients:
                # Normal crafting recipes should have ingredients. Ignore special
                # or malformed rows rather than creating zero-cost graph nodes.
                continue
            rows.append(
                {
                    "r": result,
                    "a": max(1, amount),
                    "s": station,
                    "i": ingredients,
                    **({"v": version} if version else {}),
                }
            )
        offset += len(batch)
        print(f"Fetched {offset} Cargo rows; kept {len(rows)} desktop recipes", file=sys.stderr)
        if len(batch) < PAGE_SIZE:
            break
        time.sleep(0.15)

    # Deduplicate platform/registration duplicates while preserving genuinely
    # different alternate recipes.
    unique: dict[str, dict[str, object]] = {}
    for row in rows:
        key = json.dumps(
            [row["r"], row["a"], row["s"], row["i"]],
            ensure_ascii=False,
            separators=(",", ":"),
        )
        unique[key] = row
    recipes = sorted(
        unique.values(),
        key=lambda r: (
            str(r["r"]).casefold(),
            str(r["s"]).casefold(),
            json.dumps(r["i"], ensure_ascii=False),
        ),
    )
    return recipes


def build_nodes(recipes: list[dict[str, object]]) -> list[list[object]]:
    """Return compact per-result metrics used for fast client-side sorting.

    complexity counts every distinct dependency reachable through any normal
    crafting route. That deliberately measures graph breadth rather than raw
    stack sizes, so bulk brick/ammo recipes do not dominate the default sort.
    """
    by_result: dict[str, list[dict[str, object]]] = {}
    ingredient_names: set[str] = set()
    for recipe in recipes:
        result = str(recipe["r"])
        by_result.setdefault(result, []).append(recipe)
        ingredient_names.update(str(item[0]) for item in recipe["i"])

    craftables = set(by_result)
    nodes: list[list[object]] = []
    for name in sorted(craftables, key=str.casefold):
        seen: set[str] = set()
        queue = [name]
        while queue:
            current = queue.pop()
            for recipe in by_result.get(current, []):
                for ingredient, _qty in recipe["i"]:
                    ingredient = str(ingredient)
                    if ingredient == name or ingredient in seen:
                        continue
                    seen.add(ingredient)
                    if ingredient in craftables:
                        queue.append(ingredient)

        # Small optional tie-breakers let the browser sort without flattening
        # every shopping list on page load. direct_qty is normalized by recipe
        # output amount and chooses the lightest direct recipe.
        direct_qty = min(
            sum(int(i[1]) for i in recipe["i"]) / max(1, int(recipe["a"]))
            for recipe in by_result[name]
        )
        direct_qty = round(direct_qty, 4)
        nodes.append([
            name,
            len(seen),
            1 if name not in ingredient_names else 0,
            direct_qty,
            len(by_result[name]),
        ])
    return nodes


def validate(recipes: list[dict[str, object]]) -> None:
    # 1.4.5 currently has ~3.6k normal recipes. Wide guardrails catch API
    # breakage without requiring an update for small Terraria patches.
    if not 3000 <= len(recipes) <= 5000:
        raise RuntimeError(f"Recipe count {len(recipes)} is outside expected range 3000..5000")
    results = {str(r["r"]) for r in recipes}
    if len(results) < 2000:
        raise RuntimeError(f"Only {len(results)} unique craftable results; data is probably incomplete")
    must_exist = {"Shellphone", "Ankh Shield", "Terraspark Boots", "Zenith"}
    missing = sorted(must_exist - results)
    if missing:
        raise RuntimeError("Missing sentinel recipes: " + ", ".join(missing))


def main() -> int:
    recipes = fetch_recipes()
    validate(recipes)
    result_names = {str(r["r"]) for r in recipes}
    nodes = build_nodes(recipes)
    endpoints = [node for node in nodes if node[2]]
    payload = {
        "schema": 2,
        "game": "Terraria Desktop 1.4.5.x",
        "generatedAt": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "source": "Official Terraria Wiki Cargo: Recipes",
        "recipeCount": len(recipes),
        "craftableCount": len(result_names),
        "endpointCount": len(endpoints),
        "nodes": nodes,
        "recipes": recipes,
    }
    text = "window.TERRARIA_RECIPE_DATA=" + json.dumps(
        payload, ensure_ascii=False, separators=(",", ":")
    ) + ";\n"
    OUT.write_text(text, encoding="utf-8")
    print(
        f"Wrote {OUT}: {len(recipes)} recipes, {len(result_names)} craftables, {len(endpoints)} endpoints",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
