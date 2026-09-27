# Gear Inspector

A Jotunn-free BepInEx 5 debug/fitting-room tool for Valheim.

Gear Inspector scans the live `ObjectDB.m_items` collection, keeps only equipable `ItemDrop` entries, and exposes the identifiers and metadata useful when exploring vanilla or mod-added gear.

## Features

- F8 in-game browser (configurable in `claire.valheim.gearinspector.cfg`)
- live ObjectDB scan, including normally registered mod items
- categories for armor, weapons, shields, tools, utility, ammo, and everything else equipable
- search by localized display name, prefab name, item type, or localization token
- exact prefab/spawn ID
- Valheim-compatible stable hash
- localization token, item type, variant count, set metadata, attach override, equip effect, movement modifier, armor, and weight
- recipe-backed indicator (absence of a recipe is deliberately **not** labeled proof of unobtainability)
- fitting-room preview using cloned `ItemData` objects without adding them to inventory
- multi-slot outfit preview: keep selecting pieces and Valheim handles normal equipment conflicts
- automatic loadout snapshot on the first preview and restoration when the window closes
- variant cycling
- copy prefab ID, spawn command, or a technical block to the clipboard
- JSON dump to `BepInEx/config/GearInspector/equipable-items.json`

## Design

The plugin intentionally avoids a hardcoded item list. It reflects over Valheim's loaded runtime types and reads the live ObjectDB so new vanilla gear and normally registered mod gear can appear without an update.

Valheim internals are accessed through reflection to keep the inspector resilient to ordinary game updates. The UI uses Unity IMGUI directly. Jotunn and Harmony are not dependencies.

## Build

The repository's `.github/workflows/build-valheim.yml` workflow supplies BepInEx and the pinned current Valheim Unity managed assemblies, builds `GearInspector.dll`, and publishes it to `valheim/gear-inspector/GearInspector.dll`.

For a local build, put these Valheim/BepInEx runtime references in `lib/`:

- `BepInEx.dll`
- `UnityEngine.dll`
- `UnityEngine.CoreModule.dll`
- `UnityEngine.IMGUIModule.dll`
- `UnityEngine.InputLegacyModule.dll`

Then run:

```text
dotnet build GearInspector.csproj -c Release
```

Install the resulting DLL in `BepInEx/plugins/`.
