# Jotunn Compatibility Layer

A compatibility-first, performance-oriented Jotunn distribution for Valheim.

## Goal

Existing mods compiled against Jotunn should load without being ported. The compatibility package therefore preserves exactly what binary mods and BepInEx already expect:

- assembly file and assembly name: `Jotunn.dll`
- BepInEx plugin GUID: `com.jotunn.jotunn`
- Jotunn public namespaces, types, members, version, and network behavior
- existing Jotunn features unless a subsystem has been deliberately replaced behind the same contract

This is intentionally different from inventing a new mod framework. Existing mods should continue to believe they are talking to Jotunn.

## Current implementation

The first release bundles the **unmodified official Jotunn 2.30.1 DLL** and a tiny companion BepInEx plugin, `JotunnCompat.FastPath.dll`.

The companion has a hard BepInEx dependency on Jotunn, so Jotunn's `Awake` runs first. The companion then installs Harmony prefixes before Unity calls Jotunn's `Start`. That lets us replace expensive Start-time internals without rewriting Jotunn metadata or changing any API that existing mods bind against.

Because `Jotunn.dll` itself is untouched, binary/API identity is exact by construction.

### PatchInit fast path

Jotunn's obsolete `PatchInit` compatibility code scans every type and every public static method in every Jotunn-dependent assembly at startup. The replacement first checks assembly metadata bytes for the obsolete attribute name and only reflects assemblies that can actually contain it.

Modern mods that do not use `PatchInitAttribute` avoid the expensive type/method reflection walk entirely.

### Localization fast path

Jotunn's automatic translation discovery recursively scans the full BepInEx plugin tree separately for:

- `community_translation.json`
- `*.json`
- `*.language`
- `*.yaml`
- `*.yml`

The replacement performs one recursive filesystem walk, classifies matching files in memory, and feeds them into Jotunn's existing localization implementation. If no automatic translation files exist, it also avoids initializing the localization manager just for discovery.

## Why start this way?

A hand-written shim that implements only common Jotunn calls is not a real drop-in replacement. Already-compiled mods contain metadata references to exact Jotunn types and signatures; missing one uncommon member can prevent a mod from loading at all.

Keeping the upstream assembly intact gives us a perfect compatibility floor while we replace internals incrementally. It also gives us a safe rollback path for each subsystem: if a faster implementation is not behavior-compatible, that override can be removed without changing what mods link against.

## Installation

1. Remove any separately installed official Jotunn package. Do not load two copies.
2. Put both `Jotunn.dll` and `JotunnCompat.FastPath.dll` from this package in the same BepInEx plugin folder.
3. Launch Valheim normally.

The recommended download is `JotunnCompat.zip`, which contains both DLLs.

## Roadmap

The compatibility contract is permanent; the implementation underneath it can keep shrinking.

Future work should profile real mod packs and replace one Jotunn subsystem at a time while maintaining a corpus of existing Jotunn-dependent mods as compatibility tests. Candidate areas include manager initialization, asset handling, GUI helpers, synchronization, prefab/item/piece registration, and location/zone management.

The important rule is that optimization happens behind the existing Jotunn contract. Mods do not get forced onto a new API.

## License

This subproject is MIT licensed. The bundled Jotunn binary remains covered by Jotunn's MIT license, and the upstream copyright/license text is preserved in [LICENSE](LICENSE).

Jotunn: https://github.com/Valheim-Modding/Jotunn
