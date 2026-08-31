#!/usr/bin/env python3
"""Build exhaustive fishing-route metadata for Terraria Shopping.

An item is fishable when fishing can satisfy the shopping-list requirement by
any supported route, not only when the item itself is a direct catch. Routes are
built from Official Terraria Wiki data using:
  * Category:Fished items for direct catches (including crates/containers),
  * Drops Cargo for transitive container loot,
  * Angler quest sources from the generated availability dataset,
  * Blood Moon enemies that are spawned through fishing.

When several equally short fishing paths exist, all immediate producers are kept.
This prevents world/progression counterparts such as Wooden/Pearlwood Crates or
Defiled/Hematic Crates from being silently reduced to whichever queue entry won.
Very large all-crate sets are rendered as an exact crate-type count so badges stay
usable on mobile without discarding the fact that many equivalent routes exist.
"""
from __future__ import annotations

import html
import json
import pathlib
import re
import sys
import time
import urllib.parse
import urllib.request
from collections import defaultdict, deque

API = "https://terraria.wiki.gg/api.php"
ROOT = pathlib.Path(__file__).resolve().parents[1]
AVAILABILITY_DATA = ROOT / "availability.generated.js"
OUT = ROOT / "fishing.generated.js"
PAGE_SIZE = 500
USER_AGENT = "polskiftw/gpages terraria-shopping fishing-routes/1.2 (GitHub Pages data refresh)"

BLOOD_MOON_FISHING_ENEMIES = {
    "Wandering Eye Fish",
    "Zombie Merman",
    "Hemogoblin Shark",
    "Blood Eel",
    "Dreadnautilus",
}

VERSION_PAREN = re.compile(r"\s*\((?:Desktop|Console|Mobile|Old-gen|3DS|Switch)[^)]*versions?\)\s*$", re.I)
TAG_RE = re.compile(r"<[^>]+>")
SPACE_RE = re.compile(r"\s+")


def request_json(params: dict[str, object], retries: int = 4) -> dict:
    query = urllib.parse.urlencode(params)
    req = urllib.request.Request(f"{API}?{query}", headers={"User-Agent": USER_AGENT})
    last: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=45) as response:
                return json.load(response)
        except Exception as exc:  # pragma: no cover - network retry path
            last = exc
            if attempt + 1 < retries:
                time.sleep(0.8 * (attempt + 1))
    raise RuntimeError(f"Official Wiki request failed: {last}")


def clean_name(value: object) -> str:
    text = html.unescape(str(value or ""))
    text = TAG_RE.sub("", text)
    text = text.replace("\u00a0", " ").replace("_", " ")
    text = VERSION_PAREN.sub("", text)
    text = SPACE_RE.sub(" ", text).strip()
    return text


def cargo_bool(value: object) -> bool:
    return str(value or "").strip().casefold() in {"1", "true", "yes"}


def load_availability() -> dict[str, list[object]]:
    raw = AVAILABILITY_DATA.read_text(encoding="utf-8").strip()
    prefix = "window.TERRARIA_GENERATED_AVAILABILITY="
    if not raw.startswith(prefix):
        raise RuntimeError("Unexpected availability.generated.js format")
    payload = raw[len(prefix):].rstrip(";\n ")
    data = json.loads(payload)
    if not isinstance(data, dict):
        raise RuntimeError("Availability data is not an object")
    return data


def fetch_fished_items() -> set[str]:
    out: set[str] = set()
    cont: str | None = None
    while True:
        params: dict[str, object] = {
            "action": "query",
            "list": "categorymembers",
            "cmtitle": "Category:Fished items",
            "cmnamespace": 0,
            "cmlimit": "max",
            "format": "json",
            "formatversion": 2,
        }
        if cont:
            params["cmcontinue"] = cont
        payload = request_json(params)
        members = ((payload.get("query") or {}).get("categorymembers") or [])
        for member in members:
            name = clean_name(member.get("title"))
            if name:
                out.add(name)
        cont = ((payload.get("continue") or {}).get("cmcontinue"))
        if not cont:
            break
    if not out:
        raise RuntimeError("Category:Fished items returned no items")
    return out


def fetch_drop_edges() -> dict[str, set[str]]:
    edges: dict[str, set[str]] = defaultdict(set)
    offset = 0
    while True:
        payload = request_json({
            "action": "cargoquery",
            "tables": "Drops",
            "fields": "nameraw,item,isfromnpc",
            "limit": PAGE_SIZE,
            "offset": offset,
            "format": "json",
            "formatversion": 2,
        })
        batch = payload.get("cargoquery") or []
        if not isinstance(batch, list):
            raise RuntimeError("Unexpected Drops Cargo response")
        if not batch:
            break
        for entry in batch:
            title = entry.get("title") or {}
            producer = clean_name(title.get("nameraw"))
            item = clean_name(title.get("item"))
            is_npc = cargo_bool(title.get("isfromnpc"))
            allowed_producer = not is_npc or producer in BLOOD_MOON_FISHING_ENEMIES
            if allowed_producer and producer and item and producer != item:
                edges[producer].add(item)
        offset += len(batch)
        if len(batch) < PAGE_SIZE:
            break
        time.sleep(0.06)
    print(f"Fetched {offset} Drops rows across {len(edges)} fishing-propagatable producers", file=sys.stderr)
    return edges


RouteDescriptor = tuple[str, str]


def render_graph_route(descriptors: set[RouteDescriptor]) -> str:
    direct = any(kind == "Fishing" and not producer for kind, producer in descriptors)
    fishing_producers = sorted(
        {producer for kind, producer in descriptors if kind == "Fishing" and producer},
        key=str.casefold,
    )
    blood_roots = sorted(
        {producer for kind, producer in descriptors if kind == "Blood Moon fishing" and producer},
        key=str.casefold,
    )
    parts: list[str] = []
    if direct:
        parts.append("Fishing")
    elif fishing_producers:
        if len(fishing_producers) > 4 and all(source.endswith(" Crate") for source in fishing_producers):
            parts.append(f"Fishing → {len(fishing_producers)} crate types")
        else:
            parts.append("Fishing → " + " / ".join(fishing_producers))
    if blood_roots:
        parts.append("Blood Moon fishing → " + " / ".join(blood_roots))
    return " • ".join(parts)


def build_routes(availability: dict[str, list[object]], direct_fished: set[str], edges: dict[str, set[str]]) -> dict[str, str]:
    wanted = set(availability)
    routes: dict[str, str] = {}
    queue: deque[str] = deque()

    distance: dict[str, int] = {}
    descriptors: dict[str, set[RouteDescriptor]] = defaultdict(set)

    def add_seed(name: str, descriptor: RouteDescriptor) -> None:
        if name not in distance or 0 < distance[name]:
            distance[name] = 0
            descriptors[name] = {descriptor}
            queue.append(name)
            return
        if descriptor not in descriptors[name]:
            descriptors[name].add(descriptor)
            queue.append(name)

    for name in sorted(direct_fished, key=str.casefold):
        add_seed(name, ("Fishing", ""))
    for enemy in sorted(BLOOD_MOON_FISHING_ENEMIES, key=str.casefold):
        add_seed(enemy, ("Blood Moon fishing", enemy))

    for name, row in availability.items():
        source = str(row[2] if isinstance(row, list) and len(row) > 2 else "")
        source_cf = source.casefold()
        if "angler quest" in source_cf:
            routes[name] = "Angler quest"
        elif "fishing" in source_cf:
            routes[name] = source or "Fishing"

    while queue:
        producer = queue.popleft()
        parent_distance = distance[producer]
        parent_descriptors = descriptors[producer]
        for item in sorted(edges.get(producer, ()), key=str.casefold):
            next_distance = parent_distance + 1
            candidate_descriptors: set[RouteDescriptor] = set()
            if any(kind == "Fishing" for kind, _source in parent_descriptors):
                candidate_descriptors.add(("Fishing", producer))
            candidate_descriptors.update(
                ("Blood Moon fishing", root)
                for kind, root in parent_descriptors
                if kind == "Blood Moon fishing"
            )
            if not candidate_descriptors:
                continue

            old_distance = distance.get(item)
            if old_distance is None or next_distance < old_distance:
                distance[item] = next_distance
                descriptors[item] = set(candidate_descriptors)
                queue.append(item)
            elif next_distance == old_distance:
                before = len(descriptors[item])
                descriptors[item].update(candidate_descriptors)
                if len(descriptors[item]) != before:
                    queue.append(item)

    for name in wanted:
        if name in descriptors:
            rendered = render_graph_route(descriptors[name])
            if rendered:
                routes.setdefault(name, rendered)

    return dict(sorted(routes.items(), key=lambda pair: pair[0].casefold()))


def validate(routes: dict[str, str], availability: dict[str, list[object]]) -> None:
    probes = {
        "Armored Cavefish": "direct catch",
        "Aglet": "crate-derived item",
        "Black Pearl": "fished-container item",
    }
    missing = [f"{name} ({label})" for name, label in probes.items() if name in availability and name not in routes]
    if missing:
        raise RuntimeError("Fishing-route regression: " + ", ".join(missing))

    if "Enchanted Sword" in routes.get("Nazar", ""):
        raise RuntimeError("Fishing-route regression: Enchanted Sword item/enemy collision leaked Nazar")

    expected_sources = {
        "Soul of Night": {"Defiled Crate", "Hematic Crate"},
        "Aglet": {"Wooden Crate", "Pearlwood Crate"},
        "Radar": {"Wooden Crate", "Pearlwood Crate"},
    }
    for name, expected in expected_sources.items():
        if name not in availability:
            continue
        route = routes.get(name, "")
        absent = sorted(source for source in expected if source not in route)
        if absent:
            raise RuntimeError(f"Fishing-route regression: {name} missing current source(s): {', '.join(absent)}; got {route!r}")


def main() -> int:
    availability = load_availability()
    direct_fished = fetch_fished_items()
    edges = fetch_drop_edges()
    routes = build_routes(availability, direct_fished, edges)
    validate(routes, availability)

    payload = json.dumps(routes, ensure_ascii=False, separators=(",", ":"))
    OUT.write_text(f"window.TERRARIA_GENERATED_FISHING={payload};\n", encoding="utf-8")

    direct_wanted = sum(1 for name in routes if name in direct_fished)
    angler = sum(1 for route in routes.values() if route.startswith("Angler quest"))
    blood = sum(1 for route in routes.values() if route.startswith("Blood Moon fishing"))
    multi = sum(1 for route in routes.values() if " / " in route or "crate types" in route)
    print(
        f"Fishing routes: {len(routes)}/{len(availability)} shopping leaves "
        f"({direct_wanted} direct, {angler} Angler, {blood} Blood Moon/derived, {multi} multi-source)",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())