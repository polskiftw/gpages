# Hammer Everything

A dependency-light Valheim BepInEx plugin that exposes normally hidden **vanilla** props in the Hammer build menu.

## Goals

- No Jotunn dependency.
- Reuse original Valheim network prefab names rather than introducing custom network prefabs.
- Focus on decorative/physical props such as barrels, Dvergr crates, furniture, banners, curtains, lanterns, and hidden structural pieces.
- Keep obviously unsafe prefabs (creatures, projectiles, VFX, spawners, terrain generators, etc.) out of the Hammer.
- Preserve developer-authored vanilla recipes where Iron Gate already serialized one.
- Apply hand-authored survival recipes to the principal recipe-less props instead of making them free by default.
- Naturally spawned copies of newly-piece-enabled props remain non-removable; player-built copies become removable after Valheim assigns a creator ID.
- Strip location-only `DropOnDestroyed` loot from player-built copies so crates, barrels, lanterns, and similar props cannot duplicate world loot.

## Build-menu category

Hammer Everything 1.2 puts every piece it adds under a dedicated **Hammer Everything** tag in Valheim 1.0's build menu. Added pieces have their normal usage tags cleared, so they no longer spill into Building, Furniture, Decor, Storage, and the other vanilla tags. They still appear under Valheim's normal Show All view.

This uses HarmonyX that is already shipped inside BepInEx 5. There is still no Jotunn requirement and no extra dependency to install.

## Icons

Hammer Everything automatically renders a transparent 128×128 thumbnail from each added vanilla prefab, so barrels look like barrels, crates look like crates, and so on instead of every generated entry borrowing the wooden-chest icon. Rendering is client-side, processed one icon per frame, and falls back to the normal vanilla icon if a prefab cannot be rendered safely. No icon asset bundle or Jotunn dependency is required.

## Crafting costs

Version 1.3 changes the default from free building to survival costs. If a hidden vanilla piece already has a non-empty developer-authored `m_resources` array, Hammer Everything leaves it alone. For recipe-less props, the mod carries a reviewed recipe table.

Current curated recipes include:

- `barrell`: 10 Wood + 1 Barrel Hoops
- `dvergrprops_barrel`: 8 Finewood + 2 Copper
- `dvergrprops_crate`: 6 Finewood + 1 Copper
- `dvergrprops_crate_long`: 10 Finewood + 2 Copper
- `dvergrprops_crate_ashlands`: 6 Ashwood + 1 Flametal
- Dvergr bed/chair/stool/table/shelf: Wood + small Copper fittings
- Dvergr banner/curtain: 4 Blue Jute + 1 Finewood
- Dvergr hook and chain: 2 Copper + 1 Chain
- Dvergr lantern props: the crafted Dvergr Lantern item, plus Copper for the standing mount
- Dvergr pickaxe/wood structural props: Yggdrasil Wood with Iron/Copper where the model calls for metal reinforcement

Player-built copies have `DropOnDestroyed` removed after Valheim assigns their creator ID. Natural world copies are untouched, so this prevents building a crate or lantern and then smashing it for location loot.

Set `UseCraftingCosts=false` for the old free-build behavior. Pre-1.3 `AlwaysAvailable` config entries are intentionally ignored so an existing config cannot silently erase the new recipes.

## Install

Install BepInEx 5 for Valheim and copy `HammerEverything.dll` to:

`Valheim/BepInEx/plugins/`

There is no Jotunn requirement.

## Multiplayer

The mod modifies existing vanilla prefabs in memory and keeps their original prefab names. Players who need the extra Hammer entries should install the plugin. The placed objects themselves use vanilla Valheim network prefabs.

## Config

Generated at:

`BepInEx/config/claire.valheim.hammereverything.cfg`

Useful settings:

- `AutomaticPropScan`
- `IncludeHiddenStructures`
- `UseCraftingCosts` — enabled by default; disable for free building.
- `AllowInDungeons`
- `AllowOverlap`
- `ExtraPrefabNames` — comma-separated exact vanilla prefab names.
- `BlockedPrefabNames` — comma-separated exclusions.

`CargoCrate` is deliberately blocked because vanilla treats an empty cargo crate as disposable.
