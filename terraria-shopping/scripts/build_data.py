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
CURRENT_DESKTOP_VERSION = "1.4.5.8"
USER_AGENT = "polskiftw/gpages terraria-shopping recipe-catalog/2.4 (GitHub Pages data refresh)"
FIELDS = "result,resultid,amount,version,station,args,legacy"
SKIP_STATIONS = {"Shimmer", "Chlorophyte Extractinator"}

# A few old-gen-console / 3DS recipes currently leak through Cargo without a
# useful legacy/version marker. This site is explicitly Desktop 1.4.5.8, so do
# not let Ocram-era craftables enter the PC graph.
SKIP_RESULTS = {
    "Pot o' Gold",
    "Sharanga",
    "Sparkly Wings",
    "Suspicious Looking Skull",
    "Tizona",
    "Tonbogiri",
    "Vulcan Repeater",
}

# Current-name normalization for stale names that still appear inside otherwise
# current Desktop recipe rows.
INGREDIENT_ALIASES = {
    "Fiery Greatsword": "Volcano",
}


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
    return INGREDIENT_ALIASES.get(value, value)


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
            if not result or result in SKIP_RESULTS:
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


def ingredient_use_counts(recipes: list[dict[str, object]]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for recipe in recipes:
        for ingredient, _amount in recipe.get("i") or []:
            name = str(ingredient)
            counts[name] = counts.get(name, 0) + 1
    return counts


def processed_build_form(name: str) -> bool:
    return name.endswith(" Platform") or name.endswith(" Wall")


def reciprocal_ingredient(
    recipe: dict[str, object], by_result: dict[str, list[dict[str, object]]]
) -> str:
    inputs = recipe.get("i") or []
    if len(inputs) != 1:
        return ""
    ingredient = str(inputs[0][0])
    ingredient_qty = max(1, int(inputs[0][1]))
    result_qty = max(1, int(recipe.get("a") or 1))
    result = str(recipe["r"])
    for reverse in by_result.get(ingredient) or []:
        reverse_inputs = reverse.get("i") or []
        if (
            len(reverse_inputs) == 1
            and str(reverse_inputs[0][0]) == result
            and max(1, int(reverse_inputs[0][1])) == result_qty
            and max(1, int(reverse.get("a") or 1)) == ingredient_qty
        ):
            return ingredient
    return ""


def reciprocal_recipe_role(
    recipe: dict[str, object],
    by_result: dict[str, list[dict[str, object]]],
    use_counts: dict[str, int],
) -> str:
    """Classify reversible recipes so shopping follows material -> processed form.

    Terraria exposes many valid recycling recipes such as Bone Platform -> Bone
    and Brick Wall -> Brick. Those are useful in-game, but following them while
    flattening a shopping list turns raw materials into decorative intermediates.
    """
    ingredient = reciprocal_ingredient(recipe, by_result)
    if not ingredient:
        return ""
    result = str(recipe["r"])
    result_processed = processed_build_form(result)
    ingredient_processed = processed_build_form(ingredient)
    if result_processed != ingredient_processed:
        return "forward" if result_processed else "inverse"
    result_uses = use_counts.get(result, 0)
    ingredient_uses = use_counts.get(ingredient, 0)
    if result_uses != ingredient_uses:
        return "forward" if ingredient_uses > result_uses else "inverse"
    return "ambiguous"


def planning_options(
    name: str,
    by_result: dict[str, list[dict[str, object]]],
    use_counts: dict[str, int],
) -> list[dict[str, object]]:
    return [
        recipe
        for recipe in by_result.get(name) or []
        if reciprocal_recipe_role(recipe, by_result, use_counts) not in {"inverse", "ambiguous"}
    ]


def canonical_plan(
    name: str,
    qty: int,
    by_result: dict[str, list[dict[str, object]]],
    use_counts: dict[str, int],
    stack: frozenset[str] = frozenset(),
    depth: int = 0,
) -> tuple[dict[str, int], int, int, str]:
    """Match the browser's generated shopping-list route selection.

    A route is chosen first by the fewest *unique final shopping-list items*.
    Quantity is only a tie-breaker, so 9,000 of one ingredient still counts as
    one item for the default catalog sort. Reversible recycling recipes are not
    allowed to turn a primitive material back into a Platform/Wall intermediary.
    """
    options = planning_options(name, by_result, use_counts)
    if not options or depth > 48:
        return {name: qty}, 0, 0, ""
    if name in stack:
        return {}, 1, 0, name

    next_stack = stack | {name}
    candidates: list[tuple[dict[str, int], int, dict[str, object]]] = []
    propagated_cycle = ""
    cycle_closed_here = False
    for recipe in options:
        batches = math.ceil(qty / max(1, int(recipe["a"])))
        leaves: dict[str, int] = {}
        steps = 1
        cycle_to = ""
        for ingredient, amount in recipe["i"]:
            child_name = str(ingredient)
            child_qty = max(1, int(amount)) * batches
            child_leaves, _child_cycles, child_steps, child_cycle = canonical_plan(
                child_name, child_qty, by_result, use_counts, next_stack, depth + 1
            )
            if child_cycle:
                cycle_to = child_cycle
                break
            merge_leaves(leaves, child_leaves)
            steps += child_steps
        if cycle_to:
            if cycle_to == name:
                cycle_closed_here = True
            elif not propagated_cycle:
                propagated_cycle = cycle_to
            continue
        candidates.append((leaves, steps, recipe))

    if not candidates:
        if propagated_cycle and not cycle_closed_here:
            return {}, 1, 0, propagated_cycle
        return {name: qty}, 0, 0, ""

    candidates.sort(
        key=lambda candidate: (
            len(candidate[0]),
            sum(candidate[0].values()),
            candidate[1],
            str(candidate[2]["s"]).casefold(),
            json.dumps(candidate[2]["i"], ensure_ascii=False, separators=(",", ":")),
        )
    )
    leaves, steps, _recipe = candidates[0]
    return leaves, 0, steps, ""


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
    use_counts = ingredient_use_counts(recipes)

    craftables = set(by_result)
    nodes: list[list[object]] = []
    for index, name in enumerate(sorted(craftables, key=str.casefold), 1):
        leaves, _cycles, _steps, _cycle_to = canonical_plan(name, 1, by_result, use_counts)
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
    leaked = sorted(SKIP_RESULTS & results)
    if leaked:
        raise RuntimeError("Legacy-only craftables leaked into Desktop data: " + ", ".join(leaked))


def main() -> int:
    recipes = fetch_recipes()
    validate(recipes)
    result_names = {str(r["r"]) for r in recipes}
    nodes = build_nodes(recipes)
    endpoints = [node for node in nodes if node[2]]
    payload = {
        "schema": 3,
        "game": f"Terraria Desktop {CURRENT_DESKTOP_VERSION}",
        "generatedAt": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "source": "Official Wiki recipe data via Recipes Cargo",
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