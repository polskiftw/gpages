# Jotunn Compatibility Layer

A compatibility-first, performance-oriented replacement for Jotunn in Valheim.

## Goal

Existing mods compiled against Jotunn should load without being ported. The installed compatibility package therefore preserves the identities that binary mods and BepInEx already expect:

- assembly file and assembly name: `Jotunn.dll`
- BepInEx plugin GUID: `com.jotunn.jotunn`
- Jotunn public namespaces, types, members, and current compatibility version
- network compatibility behavior and all existing Jotunn features unless a subsystem has been proven safe to replace

This is intentionally different from creating a new framework with a new API. Mods should believe they are talking to Jotunn.

## Current implementation

The first compatibility release uses the official Jotunn 2.30.1 binary as its ABI/API baseline and rewrites only startup implementations that can be optimized without changing their external behavior.

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

## Why keep the official public surface?

A small hand-written shim that merely implements common Jotunn calls is not a real drop-in replacement. Already-compiled mods contain metadata references to exact Jotunn types and method signatures. Missing one uncommon type can stop a mod before its code runs.

This project instead keeps the complete binary contract and replaces internals incrementally. A build-time verifier rejects a patched DLL if its public API differs from its upstream baseline.

## Installation

1. Remove the official Jotunn DLL/package. Do not install both at once.
2. Put both `Jotunn.dll` and `JotunnCompat.FastPath.dll` from this package in the same BepInEx plugin folder.
3. Launch Valheim normally.

The recommended download is `JotunnCompat.zip`, which contains both DLLs.

## Roadmap

The compatibility contract is the permanent part. The implementation underneath it can keep getting smaller.

Future work should profile real mod packs and replace one Jotunn subsystem at a time, while maintaining a corpus of existing Jotunn-dependent mods as compatibility tests. Candidate areas include manager initialization, asset handling, GUI helpers, synchronization, prefab/item/piece registration, and location/zone management.

A subsystem only gets removed or rewritten after its externally observable behavior and API contract are covered by tests.

## License

This subproject is MIT licensed because it is a derivative/compatibility build of Jotunn. The upstream Jotunn copyright and MIT license are preserved in [LICENSE](LICENSE).

Jotunn: https://github.com/Valheim-Modding/Jotunn
