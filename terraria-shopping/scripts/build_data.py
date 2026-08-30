#!/usr/bin/env python3
"""Build the Terraria Shopping recipe graph from the Official Terraria Wiki Cargo API.

The generated file stays data-only. The browser uses it for the complete craftable
catalog, while curated acquisition notes remain a separate enrichment layer.
"""
from __future__ import annotations

import datetime as dt
import json
import math
import pathlib
import sys
import time
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
OUT = pathlib.Path(__file__).resolve().parents[1] / "data.generated.js"
PAGE_SIZE = 500
USER_AGENT = "polskiftw/gpages terraria-shopping recipe-catalog/2.1 (GitHub Pages data refresh)"
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

    unique: dict[str, dict[str, object]] = {}
    for row in rows:
        key = json.dumps(
            [row["r"], row["a"], row["s"], row["i"]],
            ensure_ascii=False,
            separators=(",", ":"),
        )
        unique[key] = row
    return sorted(
        unique.values(),
        key=lambda r: (
            str(r["r"]).casefold(),
            str(r["s"]).casefold(),
            json.dumps(r["i"], ensure_ascii=False),
        ),
    )


def merge_leaves(into: dict[str, int], other: dict[str, int]) -> None:
    for name, qty in other.items():
        into[name] = into.get(name, 0) + qty


def canonical_plan(
    name: str,
    qty: int,
    by_result: dict[str, list[dict[str, object]]],
    stack: frozenset[str] = frozenset(),
    depth: int = 0,
) -> tuple[dict[str, int], int, int]:
    """Match the browser's generated shopping-list route selection.

    A route is chosen first by the fewest *unique final shopping-list items*.
    Quantity is only a tie-breaker, so 9,000 of one ingredient still counts as
    one item for the default catalog sort.
    """
    options = by_result.get(name) or []
    if not options or depth > 48:
        return {name: qty}, 0, 0
    if name in stack:
        return {name: qty}, 1, 0

    next_stack = stack | {name}
    candidates: list[tuple[dict[str, int], int, int, dict[str, object]]] = []
    for recipe in options:
        batches = math.ceil(qty / max(1, int(recipe["a"])))
        leaves: dict[str, int] = {}
        cycles = 0
        steps = 1
        for ingredient, amount in recipe["i"]:
            child_name = str(ingredient)
            child_qty = max(1, int(amount)) * batches
            child_leaves, child_cycles, child_steps = canonical_plan(
                child_name, child_qty, by_result, next_stack, depth + 1
            )
            merge_leaves(leaves, child_leaves)
            cycles += child_cycles
            steps += child_steps
        candidates.append((leaves, cycles, steps, recipe))

    clean = [candidate for candidate in candidates if candidate[1] == 0]
    if not clean:
        # A reversible/circular recipe should stop expansion at this component,
        # not poison every parent above it and collapse the whole project to one
        # giant self-reference.
        return {name: qty}, 0, 0

    clean.sort(
        key=lambda candidate: (
            len(candidate[0]),
            sum(candidate[0].values()),
            candidate[2],
            str(candidate[3]["s"]).casefold(),
            json.dumps(candidate[3]["i"], ensure_ascii=False, separators=(",", ":")),
        )
    )
    leaves, cycles, steps, _recipe = clean[0]
    return leaves, cycles, steps


def build_nodes(recipes: list[dict[str, object]]) -> list[list[object]]:
    """Return compact per-result metrics used for client-side sorting.

    Node slot 1 is the number the UI calls "items needed": distinct items in the
    generated flattened shopping list. Raw stack quantity never inflates it.
    """
    by_result: dict[str, list[dict[str, object]]] = {}
    ingredient_names: set[str] = set()
    for recipe in recipes:
        result = str(recipe["r"])
        by_result.setdefault(result, []).append(recipe)
        ingredient_names.update(str(item[0]) for item in recipe["i"])

    craftables = set(by_result)
    nodes: list[list[object]] = []
    for index, name in enumerate(sorted(craftables, key=str.casefold), 1):
        leaves, _cycles, _steps = canonical_plan(name, 1, by_result)
        direct_qty = min(
            sum(int(i[1]) for i in recipe["i"]) / max(1, int(recipe["a"]))
            for recipe in by_result[name]
        )
        nodes.append([
            name,
            len(leaves),
            1 if name not in ingredient_names else 0,
            round(direct_qty, 4),
            len(by_result[name]),
        ])
        if index % 250 == 0:
            print(f"Planned {index}/{len(craftables)} craftables", file=sys.stderr)
    return nodes


def validate(recipes: list[dict[str, object]]) -> None:
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
        "schema": 3,
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
