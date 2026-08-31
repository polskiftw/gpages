#!/usr/bin/env python3
"""Build availability and acquisition-source badges for Terraria Shopping.

Every canonical shopping-list leaf must have two distinct concepts available to
the UI:
  * availability: mode / progression / event conditions
  * source: the thing, place, NPC, activity, or interaction that produces it

The Official Terraria Wiki supplies the broad Hardmode flag, Drops Cargo supplies
mob/container sources, and Template:Itemsource fills shop/fishing/plunder routes.
Small explicit overrides and category rules cover world harvests, critters, quest
rewards, and unusual interactions. The build fails if any canonical shopping
leaf would otherwise ship without a meaningful acquisition source.
"""
from __future__ import annotations

import html
import json
import math
import pathlib
import re
import sys
import time
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
ROOT = pathlib.Path(__file__).resolve().parents[1]
RECIPE_DATA = ROOT / "data.generated.js"
OUT = ROOT / "availability.generated.js"
PAGE_SIZE = 500
ITEMSOURCE_BATCH = 12
USER_AGENT = "polskiftw/gpages terraria-shopping availability-badges/1.8 (GitHub Pages data refresh)"
ITEM_FIELDS = "name,hardmode"

# item -> (availability conditions, acquisition source, progression rank)
PROGRESSION_OVERRIDES: dict[str, tuple[list[str], str, int]] = {
    "Starfury": ([], "Skyware Chest / Sky Crate", 10),
    "Enchanted Sword": ([], "Enchanted Sword Shrine", 10),
    "Bee Keeper": ([], "Queen Bee (boss)", 15),
    "Muramasa": (["After Skeletron"], "Dungeon Gold Chest", 30),
    "Hellstone": (["After Eater of Worlds / Brain of Cthulhu"], "Underworld (ore)", 20),
    "Cobalt Ore": ([], "Tier 1 Hardmode ore (mine)", 40),
    "Palladium Ore": ([], "Tier 1 Hardmode ore (mine)", 40),
    "Mythril Ore": ([], "Tier 2 Hardmode ore (mine)", 40),
    "Orichalcum Ore": ([], "Tier 2 Hardmode ore (mine)", 40),
    "Adamantite Ore": ([], "Tier 3 Hardmode ore (mine)", 40),
    "Titanium Ore": ([], "Tier 3 Hardmode ore (mine)", 40),
    "Soul of Light": ([], "Underground Hallow mobs", 40),
    "Soul of Night": ([], "Underground evil mobs", 40),
    "Soul of Flight": ([], "Wyvern (mob)", 40),
    "Crystal Shard": ([], "Underground Hallow (harvest)", 40),
    "Cursed Flame": ([], "Corruption mobs", 40),
    "Ichor": ([], "Crimson mobs", 40),
    "Hallowed Bar": ([], "Mechanical Boss (boss)", 50),
    "Soul of Fright": ([], "Skeletron Prime (boss)", 50),
    "Soul of Might": ([], "The Destroyer (boss)", 50),
    "Soul of Sight": ([], "The Twins (boss)", 50),
    "Life Fruit": (["After any Mechanical Boss"], "Underground Jungle (harvest)", 50),
    "Chlorophyte Ore": (["After all 3 Mechanical Bosses"], "Underground Jungle (ore)", 55),
    "Seedler": ([], "Plantera (boss)", 60),
    "Prismatic Lacewing": (["After Plantera"], "Surface Hallow at night (catch)", 60),
    "The Horseman's Blade": (["After Plantera", "Pumpkin Moon (EVENT)"], "Pumpking (boss)", 60),
    "Ectoplasm": (["After Plantera"], "Dungeon Spirit (mob)", 60),
    "Broken Hero Sword": (["After Plantera", "Solar Eclipse (EVENT)"], "Mothron (mob)", 60),
    "Temple Key": ([], "Plantera (boss)", 60),
    "Spooky Wood": (["After Plantera", "Pumpkin Moon (EVENT)"], "Splinterling / Mourning Wood (mob / mini-boss)", 60),
    "Beetle Husk": ([], "Golem (boss)", 70),
    "Influx Waver": (["After Golem", "Martian Madness (EVENT)"], "Martian Saucer (boss)", 70),
    "Ancient Manipulator": ([], "Lunatic Cultist (boss)", 80),
    "Solar Fragment": (["After Lunatic Cultist"], "Solar Pillar", 80),
    "Vortex Fragment": (["After Lunatic Cultist"], "Vortex Pillar", 80),
    "Nebula Fragment": (["After Lunatic Cultist"], "Nebula Pillar", 80),
    "Stardust Fragment": (["After Lunatic Cultist"], "Stardust Pillar", 80),
    "Luminite": ([], "Moon Lord (boss)", 90),
    "Meowmere": ([], "Moon Lord (boss)", 90),
    "Star Wrath": ([], "Moon Lord (boss)", 90),
}

# Exact sources for world harvests and mechanics that are clearer than a generic
# itemsource rendering. These are still concise because the shopping-list row is
# meant to answer "where/how do I get this?", not reproduce the Wiki article.
SOURCE_OVERRIDES: dict[str, str] = {
    "Aetherium Block": "Shimmer + any other liquid (contact)",
    "Stone Block": "World stone (mine)",
    "Hay": "Grass + Sickle (harvest)",
    "Obsidian": "Water + lava (contact)",
    "Crimsandstone Block": "Underground Desert terrain (mine)",
    "Ebonsandstone Block": "Underground Desert terrain (mine)",
    "Pearlsandstone Block": "Underground Desert terrain (mine)",
    "Hardened Crimsand Block": "Underground Desert terrain (mine)",
    "Hardened Ebonsand Block": "Underground Desert terrain (mine)",
    "Hardened Pearlsand Block": "Underground Desert terrain (mine)",
    "Giant Shelly Banner": "50 Giant Shelly kills",
    "Salamander Banner": "50 Salamander kills",
    "Copper Ore": "Surface / Underground / Cavern (ore)",
    "Tin Ore": "Surface / Underground / Cavern (ore)",
    "Iron Ore": "Surface / Underground / Cavern (ore)",
    "Lead Ore": "Surface / Underground / Cavern (ore)",
    "Silver Ore": "Underground / Cavern / Floating Islands (ore)",
    "Tungsten Ore": "Underground / Cavern / Floating Islands (ore)",
    "Gold Ore": "Underground / Cavern / Floating Islands (ore)",
    "Platinum Ore": "Underground / Cavern / Floating Islands (ore)",
    "Demonite Ore": "Corruption world / Eye or Eater drops (ore)",
    "Crimtane Ore": "Crimson world / Eye or Brain drops (ore)",
    "Meteorite": "Meteorite crash site (mine)",
    "Jungle Spores": "Underground Jungle (harvest)",
    "Stinger": "Hornet / Spiked Jungle Slime (mob)",
    "Vine": "Man Eater (mob)",
    "Fallen Star": "Surface at night (collect)",
    "Gel": "Slimes (mob)",
    "Lens": "Demon Eye (mob)",
    "Daybloom": "Forest grass (harvest)",
    "Blinkroot": "Dirt / Mud (harvest)",
    "Moonglow": "Jungle grass (harvest)",
    "Deathweed": "Corruption / Crimson grass (harvest)",
    "Waterleaf": "Desert sand (harvest)",
    "Fireblossom": "Underworld ash (harvest)",
    "Shiverthorn": "Snow / Ice (harvest)",
    "Truffle Worm": "Glowing Mushroom biome (catch)",
    "Blue Berries": "Surface grass / Jungle grass (harvest)",
    "Coral": "Ocean (harvest)",
    "Green Mushroom": "Underground / Cavern (harvest)",
    "Teal Mushroom": "Underground / Cavern (harvest)",
    "Orange Bloodroot": "Underground / Cavern dirt (harvest)",
    "Lime Kelp": "Underground / Cavern water (harvest)",
    "Pink Prickly Pear": "Desert cactus (harvest)",
    "Sky Blue Flower": "Jungle grass (harvest)",
    "Yellow Marigold": "Surface grass (harvest)",
    "Vile Mushroom": "Corruption grass (harvest)",
    "Vicious Mushroom": "Crimson grass (harvest)",
    "Nature's Gift": "Underground Jungle (harvest)",
    "Pink Ice Block": "Hallow ice (mine)",
    "Purple Ice Block": "Corruption ice (mine)",
    "Red Ice Block": "Crimson ice (mine)",
    "Silt Block": "Underground / Cavern (mine)",
    "Vine Rope": "Vines + Plant Fiber Cordage (harvest)",
    "Living Fire Block": "Hardmode Underworld mobs",
    "Gold Chest": "Underground / Cavern chest (mine after empty)",
    "Golden Chest": "Flying Dutchman / Pirate mobs",
    "Ivy Chest": "Underground Jungle chest (mine after empty)",
    "Shadow Chest": "Underworld chest (mine after empty)",
    "Web Covered Chest": "Spider Nest chest (mine after empty)",
    "Fishing Bobber": "Angler quest",
    "Gel Dye": "Dye Trader (Strange Plant reward)",
    "Shifting Sands Dye": "Dye Trader (Strange Plant reward)",
    "Gentleman's Magnificent Beard": "Gentleman's Beard (grow while equipped)",
    "Lava Absorbant Sponge": "Lava fishing",
    "Honey Absorbant Sponge": "Angler quest (Bumblebee Tuna)",
    "Heroicis' Wings (Inactive)": "Platinum Coin in Oasis water",
    "Cattiva": "Rescue distressed Cattiva (surface daytime)",
    "Foxparks": "Rescue distressed Foxparks (surface daytime)",
    "Digtoise": "Sleeping Digtoise (Underground Desert)",
    "Faeling": "Aether (catch)",
    "Pupfish": "Desert water (catch)",
    "Pufferfish": "Ocean (catch)",
    # Angler quest rewards are not consistently represented by Itemsource.
    "Angler Earring": "Angler quest",
    "Tackle Box": "Angler quest",
    "High Test Fishing Line": "Angler quest",
    "Fisherman's Pocket Guide": "Angler quest",
    "Weather Radio": "Angler quest",
    "Sextant": "Angler quest",
    "Angler Hat": "Angler quest",
    "Angler Vest": "Angler quest",
    "Angler Pants": "Angler quest",
    "Bottomless Water Bucket": "Angler quest",
    "Super Absorbant Sponge": "Angler quest",
}

# Recipe-category aliases are not literal Items rows and therefore have no Wiki
# item page to query. Give each category a real acquisition route.
PSEUDO_SOURCES: dict[str, str] = {
    "Any Adamantite Bar": "Adamantite / Titanium Ore (smelt)",
    "Any Balloon": "Skyware Chest / Sky Crate",
    "Any Bird": "Bird critter (catch)",
    "Any Blizzard Balloon": "Blizzard in a Balloon variant (craft)",
    "Any Butterfly": "Butterfly critter (catch)",
    "Any Cobalt Bar": "Cobalt / Palladium Ore (smelt)",
    "Any Cockatiel": "Cockatiel critter (catch)",
    "Any Dragonfly": "Dragonfly critter (catch)",
    "Any Duck": "Duck critter (catch)",
    "Any Firefly": "Firefly critter (catch)",
    "Any Fruit": "Trees (shake / chop)",
    "Any Gem Critter": "Gem critter (catch)",
    "Any Gold Bar": "Gold / Platinum Ore (smelt)",
    "Any Guide to Critter Companionship": "Zoologist (NPC)",
    "Any Guide to Environmental Preservation": "Dryad (NPC)",
    "Any Iron Bar": "Iron / Lead Ore (smelt)",
    "Any Jungle Bug": "Jungle bug critter (catch)",
    "Any Macaw": "Macaw critter (catch)",
    "Any Magic Mirror": "Underground / Frozen Chest",
    "Any Mythril Bar": "Mythril / Orichalcum Ore (smelt)",
    "Any Pressure Plate": "World traps / Mechanic (NPC)",
    "Any Sand Block": "Sand biomes (mine)",
    "Any Sandstorm Balloon": "Sandstorm in a Balloon variant (craft)",
    "Any Scorpion": "Scorpion critter (catch)",
    "Any Seashell or Starfish": "Ocean beach / underwater (collect)",
    "Any Silver Bar": "Silver / Tungsten Ore (smelt)",
    "Any Snail": "Snail critter (catch)",
    "Any Squirrel": "Squirrel critter (catch)",
    "Any Stone Block": "Stone (mine)",
    "Any Turtle": "Turtle critter (catch)",
    "Any Wood": "Trees (chop)",
}

PSEUDO_AVAILABILITY: dict[str, tuple[str, list[str], str, int]] = {
    "Adamantite/Titanium Bar": ("Hardmode", [], "Tier 3 Hardmode bar (craft)", 40),
    "Blue Jellyfish (bait)": ("Pre-Hardmode", [], "Underground / Cavern fishing", 10),
    "Green Jellyfish (bait)": ("Hardmode", [], "Underground / Cavern fishing", 40),
    "Pink Jellyfish (bait)": ("Pre-Hardmode", [], "Ocean fishing", 10),
    "Music Box (Ocean)": ("Hardmode", [], "Wizard (NPC) + record at Ocean", 40),
    "Music Box (Space)": ("Hardmode", [], "Wizard (NPC) + record in Space", 40),
}

HARDMODE_PSEUDO_HINTS = (
    "Cobalt", "Palladium", "Mythril", "Orichalcum", "Adamantite",
    "Titanium", "Hallowed", "Chlorophyte", "Shroomite", "Spectre",
    "Luminite", "Fragment",
)

BOSS_NAMES = {
    "King Slime", "Eye of Cthulhu", "Eater of Worlds", "Brain of Cthulhu",
    "Queen Bee", "Skeletron", "Deerclops", "Wall of Flesh", "Queen Slime",
    "The Destroyer", "Skeletron Prime", "The Twins", "Plantera", "Golem",
    "Duke Fishron", "Empress of Light", "Lunatic Cultist", "Moon Lord",
    "Dark Mage", "Ogre", "Betsy", "Flying Dutchman", "Mourning Wood",
    "Pumpking", "Everscream", "Santa-NK1", "Ice Queen", "Martian Saucer",
}

NPC_VENDORS = {
    "Merchant", "Traveling Merchant", "Skeleton Merchant", "Wizard", "Mechanic",
    "Dryad", "Goblin Tinkerer", "Demolitionist", "Arms Dealer", "Painter",
    "Dye Trader", "Witch Doctor", "Steampunker", "Cyborg", "Truffle",
    "Party Girl", "Pirate", "Santa Claus", "Tavernkeep", "Zoologist",
    "Golfer", "Princess",
}

GEM_CRITTER_RE = re.compile(r"^(Amber|Amethyst|Diamond|Emerald|Ruby|Sapphire|Topaz) (Bunny|Squirrel)$")
GOLD_CRITTERS = {
    "Gold Bird", "Gold Bunny", "Gold Butterfly", "Gold Dragonfly", "Gold Frog",
    "Gold Goldfish", "Gold Grasshopper", "Gold Ladybug", "Gold Mouse",
    "Gold Seahorse", "Gold Squirrel", "Gold Water Strider",
}
BUTTERFLY_CRITTERS = {
    "Julia Butterfly", "Monarch Butterfly", "Purple Emperor Butterfly",
    "Red Admiral Butterfly", "Sulphur Butterfly", "Tree Nymph Butterfly",
    "Ulysses Butterfly", "Zebra Swallowtail Butterfly",
}
DRAGONFLY_CRITTERS = {
    "Black Dragonfly", "Blue Dragonfly", "Green Dragonfly", "Orange Dragonfly",
    "Red Dragonfly", "Yellow Dragonfly",
}
FAIRY_CRITTERS = {"Blue Fairy", "Green Fairy", "Pink Fairy"}
LAVA_CRITTERS = {"Hell Butterfly", "Lavafly", "Magma Snail"}
GENERIC_CRITTERS = {
    "Bird", "Black Scorpion", "Blue Jay", "Blue Macaw", "Buggy", "Bunny",
    "Cardinal", "Duck", "Firefly", "Frog", "Glowing Snail", "Goldfish",
    "Grasshopper", "Gray Cockatiel", "Grebe", "Grubby", "Jungle Turtle",
    "Ladybug", "Lightning Bug", "Maggot", "Mallard Duck", "Mouse", "Owl",
    "Penguin", "Rat", "Red Squirrel", "Scarlet Macaw", "Scorpion", "Seagull",
    "Seahorse", "Sluggy", "Snail", "Squirrel", "Stinkbug", "Toucan", "Turtle",
    "Water Strider", "Yellow Cockatiel",
}

GENERIC_ITEMSOURCE = {"Plundering", "Looting", "Drop", "Drops", "Crafting", "By hand", "Shimmer"}
VERSION_PAREN = re.compile(r"\((?:Desktop|Console|Mobile|Old-gen|3DS|Switch)[^)]*versions?\)", re.I)
COIN_PAREN = re.compile(r"\([^)]*(?:Platinum|Gold|Silver|Copper|\bPC\b|\bGC\b|\bSC\b|\bCC\b)[^)]*\)", re.I)


def request_json(params: dict[str, object], retries: int = 4, post: bool = False) -> dict:
    encoded = urllib.parse.urlencode(params).encode("utf-8")
    if post:
        req = urllib.request.Request(API, data=encoded, headers={
            "User-Agent": USER_AGENT,
            "Accept": "application/json",
            "Content-Type": "application/x-www-form-urlencoded",
        })
    else:
        url = API + "?" + encoded.decode("ascii")
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
    last: Exception | None = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=60) as response:
                return json.loads(response.read().decode("utf-8"))
        except Exception as exc:
            last = exc
            if attempt + 1 < retries:
                time.sleep(2 ** attempt)
    raise RuntimeError(f"Wiki request failed after {retries} attempts: {last}")


def load_recipe_data() -> dict:
    text = RECIPE_DATA.read_text(encoding="utf-8").strip()
    prefix = "window.TERRARIA_RECIPE_DATA="
    if not text.startswith(prefix) or not text.endswith(";"):
        raise RuntimeError("data.generated.js has an unexpected wrapper")
    return json.loads(text[len(prefix):-1])


def cargo_bool(value: object) -> bool:
    return str(value or "").strip().lower() in {"1", "yes", "true", "y"}


def fetch_item_modes() -> dict[str, bool]:
    modes: dict[str, bool] = {}
    offset = 0
    while True:
        payload = request_json({
            "action": "cargoquery", "tables": "Items", "fields": ITEM_FIELDS,
            "limit": PAGE_SIZE, "offset": offset, "format": "json", "formatversion": "2",
        })
        batch = payload.get("cargoquery") or []
        if not isinstance(batch, list):
            raise RuntimeError("Unexpected Items Cargo response")
        if not batch:
            break
        for entry in batch:
            title = entry.get("title") or {}
            name = str(title.get("name") or "").strip()
            if name:
                modes[name] = modes.get(name, False) or cargo_bool(title.get("hardmode"))
        offset += len(batch)
        print(f"Fetched {offset} Items rows; mapped {len(modes)} names", file=sys.stderr)
        if len(batch) < PAGE_SIZE:
            break
        time.sleep(0.1)
    if len(modes) < 4000:
        raise RuntimeError(f"Only {len(modes)} item names returned by Items Cargo")
    return modes


def fetch_drop_sources(wanted: set[str]) -> dict[str, str]:
    rows: dict[str, list[tuple[str, bool]]] = {}
    offset = 0
    while True:
        payload = request_json({
            "action": "cargoquery", "tables": "Drops", "fields": "nameraw,item,isfromnpc",
            "limit": PAGE_SIZE, "offset": offset, "format": "json", "formatversion": "2",
        })
        batch = payload.get("cargoquery") or []
        if not isinstance(batch, list):
            raise RuntimeError("Unexpected Drops Cargo response")
        if not batch:
            break
        for entry in batch:
            title = entry.get("title") or {}
            item = str(title.get("item") or "").strip()
            producer = str(title.get("nameraw") or "").strip()
            if item in wanted and producer:
                rows.setdefault(item, []).append((producer, cargo_bool(title.get("isfromnpc"))))
        offset += len(batch)
        if len(batch) < PAGE_SIZE:
            break
        time.sleep(0.08)
    print(f"Fetched {offset} Drops rows; matched sources for {len(rows)} shopping leaves", file=sys.stderr)
    out: dict[str, str] = {}
    for item, producers in rows.items():
        seen: set[str] = set()
        labels: list[str] = []
        for producer, is_npc in producers:
            key = producer.casefold()
            if key in seen:
                continue
            seen.add(key)
            if is_npc:
                labels.append(f"{producer} ({'boss' if producer in BOSS_NAMES else 'mob'})")
            else:
                labels.append(producer)
            if len(labels) >= 2:
                break
        if labels:
            out[item] = " / ".join(labels)
    return out


def html_to_plain(value: str) -> str:
    value = re.sub(
        r"<img\b[^>]*\balt=(?:\"([^\"]*)\"|'([^']*)')[^>]*>",
        lambda m: " " + html.unescape(m.group(1) or m.group(2) or "") + " ",
        value,
        flags=re.I,
    )
    value = re.sub(r"</li\s*>", " / ", value, flags=re.I)
    value = re.sub(r"<br\s*/?>", " / ", value, flags=re.I)
    value = re.sub(r"<style\b[^>]*>.*?</style>", " ", value, flags=re.I | re.S)
    value = re.sub(r"<script\b[^>]*>.*?</script>", " ", value, flags=re.I | re.S)
    value = re.sub(r"<[^>]+>", " ", value)
    return re.sub(r"\s+", " ", html.unescape(value)).strip()


def dedupe_double_phrase(value: str) -> str:
    words = value.split()
    if len(words) >= 2 and len(words) % 2 == 0:
        half = len(words) // 2
        if [w.casefold() for w in words[:half]] == [w.casefold() for w in words[half:]]:
            return " ".join(words[:half])
    return value


def source_is_recipe_like(value: str, recipe_inputs: set[str]) -> bool:
    """Return True when an Itemsource fragment is merely a recipe/decraft route."""
    value = re.sub(r"\s+", " ", value).strip()
    if not value:
        return False
    if re.search(r"\(\s*@\s*[^)]*\)", value) or " @ " in value:
        return True
    core = re.sub(r"\s*\([^)]*\)\s*$", "", value).strip()
    folded = core.casefold()
    for ingredient in recipe_inputs:
        ingredient_folded = ingredient.casefold()
        if folded == ingredient_folded:
            return True
        if re.match(rf"^\d+(?:\.\d+)?\s+{re.escape(ingredient)}(?:\s|$)", core, flags=re.I):
            return True
    if re.match(r"^\d+(?:\.\d+)?\s+.*\b(?:Platform|Wall)\b", core, flags=re.I):
        return True
    return False


def clean_itemsource_primary(value: str, recipe_inputs: set[str] | None = None) -> str:
    recipe_inputs = recipe_inputs or set()
    value = VERSION_PAREN.sub("", value)
    value = COIN_PAREN.sub("", value)
    value = re.sub(r"\s+", " ", value).strip(" /:")
    if not value:
        return ""
    parts = [re.sub(r"\s+", " ", part).strip(" /:") for part in re.split(r"\s+/\s+", value)]
    for part in parts:
        primary = dedupe_double_phrase(part)
        primary = re.sub(r"^Shimmer\s+Shimmer transmutation\s*:\s*", "Shimmer transmutation: ", primary, flags=re.I)
        if not primary or primary in GENERIC_ITEMSOURCE:
            continue
        if source_is_recipe_like(primary, recipe_inputs):
            continue
        if primary in NPC_VENDORS:
            primary += " (NPC)"
        return primary[:120]
    return ""


def fetch_itemsource_sources(names: list[str], recipe_inputs_by_result: dict[str, set[str]]) -> dict[str, str]:
    out: dict[str, str] = {}
    for start in range(0, len(names), ITEMSOURCE_BATCH):
        batch = names[start:start + ITEMSOURCE_BATCH]
        chunks = []
        for index, name in enumerate(batch):
            marker = f"TSRC{index:02d}"
            chunks.append(f"@@{marker}START@@{{{{itemsource|{name}|sep= / }}}}@@{marker}END@@")
        payload = request_json({
            "action": "parse", "contentmodel": "wikitext", "prop": "text",
            "title": "Terraria Shopping source audit", "text": "\n".join(chunks),
            "format": "json", "formatversion": "2",
        }, post=True)
        rendered = str((payload.get("parse") or {}).get("text") or "")
        plain = html_to_plain(rendered)
        for index, name in enumerate(batch):
            marker = f"TSRC{index:02d}"
            match = re.search(rf"@@{marker}START@@(.*?)@@{marker}END@@", plain, flags=re.S)
            if match:
                source = clean_itemsource_primary(match.group(1), recipe_inputs_by_result.get(name, set()))
                if source:
                    out[name] = source
        print(
            f"Itemsource acquisition fallback {min(start + len(batch), len(names))}/{len(names)}; resolved {len(out)}",
            file=sys.stderr,
        )
        time.sleep(0.12)
    return out


def merge_leaves(into: dict[str, int], other: dict[str, int]) -> None:
    for name, qty in other.items():
        into[name] = into.get(name, 0) + qty


def ingredient_use_counts(recipes: list[dict]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for recipe in recipes:
        for ingredient, _amount in recipe.get("i") or []:
            name = str(ingredient)
            counts[name] = counts.get(name, 0) + 1
    return counts


def processed_build_form(name: str) -> bool:
    return name.endswith(" Platform") or name.endswith(" Wall")


def reciprocal_ingredient(recipe: dict, by_result: dict[str, list[dict]]) -> str:
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


def reciprocal_recipe_role(recipe: dict, by_result: dict[str, list[dict]], use_counts: dict[str, int]) -> str:
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


def planning_options(name: str, by_result: dict[str, list[dict]], use_counts: dict[str, int]) -> list[dict]:
    return [
        recipe
        for recipe in by_result.get(name) or []
        if reciprocal_recipe_role(recipe, by_result, use_counts) not in {"inverse", "ambiguous"}
    ]


def canonical_plan(
    name: str,
    qty: int,
    by_result: dict[str, list[dict]],
    use_counts: dict[str, int],
    stack: frozenset[str] = frozenset(),
    depth: int = 0,
) -> tuple[dict[str, int], int, int, str]:
    options = planning_options(name, by_result, use_counts)
    if not options or depth > 48:
        return {name: qty}, 0, 0, ""
    if name in stack:
        return {}, 1, 0, name
    next_stack = stack | {name}
    candidates = []
    propagated_cycle = ""
    cycle_closed_here = False
    for recipe in options:
        batches = math.ceil(qty / max(1, int(recipe.get("a") or 1)))
        leaves: dict[str, int] = {}
        steps = 1
        cycle_to = ""
        for ingredient, amount in recipe.get("i") or []:
            child, _child_cycles, child_steps, child_cycle = canonical_plan(
                str(ingredient), max(1, int(amount)) * batches, by_result, use_counts, next_stack, depth + 1
            )
            if child_cycle:
                cycle_to = child_cycle
                break
            merge_leaves(leaves, child)
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
    candidates.sort(key=lambda c: (
        len(c[0]), sum(c[0].values()), c[1], str(c[2].get("s") or "").casefold(),
        json.dumps(c[2].get("i") or [], ensure_ascii=False, separators=(",", ":")),
    ))
    return candidates[0][0], 0, candidates[0][1], ""


def collect_leaf_names(data: dict) -> set[str]:
    recipes = data.get("recipes") or []
    by_result: dict[str, list[dict]] = {}
    for recipe in recipes:
        by_result.setdefault(str(recipe["r"]), []).append(recipe)
    use_counts = ingredient_use_counts(recipes)
    names: set[str] = set()
    for index, name in enumerate(sorted(by_result, key=str.casefold), 1):
        leaves, _cycles, _steps, _cycle_to = canonical_plan(name, 1, by_result, use_counts)
        names.update(leaves)
        if index % 500 == 0:
            print(f"Audited {index}/{len(by_result)} craftables", file=sys.stderr)
    return names


def infer_pseudo_mode(name: str) -> str | None:
    if name.startswith("Any "):
        return "Hardmode" if any(hint in name for hint in HARDMODE_PSEUDO_HINTS) else "Pre-Hardmode"
    return None


def patterned_source(name: str) -> str:
    if name == "Music Box":
        return "Wizard (NPC)"
    if name.startswith("Music Box ("):
        return "Record matching music track"
    if name in PSEUDO_SOURCES:
        return PSEUDO_SOURCES[name]
    if GEM_CRITTER_RE.match(name):
        return "Underground gem critter (catch)"
    if name in GOLD_CRITTERS:
        return "Gold critter (catch)"
    if name in LAVA_CRITTERS:
        return "Underworld critter (catch)"
    if name in BUTTERFLY_CRITTERS:
        return "Butterfly critter (catch)"
    if name in DRAGONFLY_CRITTERS:
        return "Dragonfly critter (catch)"
    if name in FAIRY_CRITTERS:
        return "Fairy critter (catch)"
    if name in GENERIC_CRITTERS:
        return "World critter (catch)"
    return ""


def main() -> int:
    data = load_recipe_data()
    recipes = data.get("recipes") or []
    recipe_inputs_by_result: dict[str, set[str]] = {}
    for recipe in recipes:
        recipe_inputs_by_result.setdefault(str(recipe["r"]), set()).update(str(pair[0]) for pair in recipe.get("i") or [])
    leaf_names = collect_leaf_names(data)
    item_modes = fetch_item_modes()
    drop_sources = fetch_drop_sources(leaf_names)
    rows: dict[str, list[object]] = {}
    unresolved: list[str] = []
    for name in sorted(leaf_names, key=str.casefold):
        pseudo = PSEUDO_AVAILABILITY.get(name)
        if pseudo:
            rows[name] = list(pseudo)
            continue
        if name in item_modes:
            mode = "Hardmode" if item_modes[name] else "Pre-Hardmode"
        else:
            mode = infer_pseudo_mode(name)
        if mode is None:
            unresolved.append(name)
            continue
        default_rank = 40 if mode == "Hardmode" else 10
        if name in PROGRESSION_OVERRIDES:
            conditions, source, rank = PROGRESSION_OVERRIDES[name]
        else:
            conditions, rank = [], default_rank
            source = SOURCE_OVERRIDES.get(name) or patterned_source(name) or drop_sources.get(name, "")
        if rank >= 40 and name in PROGRESSION_OVERRIDES:
            mode = "Hardmode"
        rows[name] = [mode, conditions, source, rank]
    if unresolved:
        print("Unresolved shopping-list names:", file=sys.stderr)
        for name in unresolved:
            print(f"  - {name}", file=sys.stderr)
        raise RuntimeError(f"{len(unresolved)} shopping-list leaves have no availability classification")

    # A leaf can still have reverse/recycling recipes in Cargo (for example
    # Stone Wall -> Stone Block). Being technically craftable must not exempt it
    # from answering the shopping-list question: "where do I actually get it?"
    source_gaps = [name for name, row in rows.items() if not str(row[2] or "").strip() and name in item_modes]
    if source_gaps:
        template_sources = fetch_itemsource_sources(sorted(source_gaps, key=str.casefold), recipe_inputs_by_result)
        for name, source in template_sources.items():
            rows[name][2] = source

    source_gaps = [name for name, row in rows.items() if not str(row[2] or "").strip()]
    if source_gaps:
        print("Shopping-list leaves with no acquisition source:", file=sys.stderr)
        for name in source_gaps:
            print(f"  - {name}", file=sys.stderr)
        raise RuntimeError(f"{len(source_gaps)} shopping-list leaves have no source badge")

    suspicious = [
        name for name, row in rows.items()
        if name in item_modes and source_is_recipe_like(str(row[2] or ""), recipe_inputs_by_result.get(name, set()))
    ]
    if suspicious:
        print("Shopping-list leaves whose source badge is still a recipe/decraft route:", file=sys.stderr)
        for name in suspicious:
            print(f"  - {name}: {rows[name][2]}", file=sys.stderr)
        raise RuntimeError(f"{len(suspicious)} shopping-list leaves still use recipe-like source badges")

    OUT.write_text(
        "window.TERRARIA_GENERATED_AVAILABILITY=" + json.dumps(rows, ensure_ascii=False, separators=(",", ":")) + ";\n",
        encoding="utf-8",
    )
    hard = sum(1 for row in rows.values() if row[0] == "Hardmode")
    sourced = sum(1 for row in rows.values() if row[2])
    print(
        f"Wrote {OUT}: {len(rows)} leaves ({hard} Hardmode, {len(rows)-hard} Pre-Hardmode; {sourced} sourced; 0 source gaps; 0 recipe-like sources)",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
