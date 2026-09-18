# Hammer Everything

A dependency-light Valheim BepInEx plugin that exposes normally hidden **vanilla** props in the Hammer build menu.

## Goals

- No Jotunn dependency.
- Reuse original Valheim network prefab names rather than introducing custom network prefabs.
- Focus on decorative/physical props such as barrels, Dvergr crates, furniture, banners, curtains, lanterns, and hidden structural pieces.
- Keep obviously unsafe prefabs (creatures, projectiles, VFX, spawners, terrain generators, etc.) out of the Hammer.
- Hidden pieces are free and station-less by default so they are available immediately.
- Naturally spawned copies of newly-piece-enabled props remain non-removable; player-built copies become removable after Valheim assigns a creator ID.

## Icons

Hammer Everything 1.1 automatically renders a transparent 128×128 thumbnail from each added vanilla prefab, so barrels look like barrels, crates look like crates, and so on instead of every generated entry borrowing the wooden-chest icon. Rendering is client-side, processed one icon per frame, and falls back to the normal vanilla icon if a prefab cannot be rendered safely. No icon asset bundle or Jotunn dependency is required.

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
- `AlwaysAvailable`
- `AllowInDungeons`
- `AllowOverlap`
- `ExtraPrefabNames` — comma-separated exact vanilla prefab names.
- `BlockedPrefabNames` — comma-separated exclusions.

`CargoCrate` is deliberately blocked because vanilla treats an empty cargo crate as disposable.
