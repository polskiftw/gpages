# Jotunn Compatibility Layer

A slim, source-built drop-in compatibility implementation of Jotunn for Valheim.

## Goal

Existing mods compiled against Jotunn should load without being ported. The compatibility package preserves the identity and public contract those binaries expect:

- assembly file/name: `Jotunn.dll`
- BepInEx plugin GUID: `com.jotunn.jotunn`
- compatibility version/assembly identity: Jotunn 2.30.1
- public Jotunn namespaces, types, members, lifecycle events, and network-facing behavior required by supported mods

This is not a new framework for mod authors to target. Existing mods continue targeting Jotunn; this project is intended to replace the runtime underneath that contract.

## Architecture

The current package no longer bundles the official Jotunn DLL or the old compatibility preloader.

The ZIP installs:

- `BepInEx/plugins/JotunnCompat/Jotunn.dll` — our source-built slim Jotunn-compatible runtime
- `BepInEx/plugins/JotunnCompat/YamlDotNet.dll` — localization/config parsing dependency

The slim runtime implements the Jotunn-facing managers and entities needed by current compatibility targets, including prefab lookup/registration, locations, dungeons, items/status effects, recipes, pieces and piece tables, localization, custom RPCs, GUI helpers, minimap lifecycle notifications, runtime texture loading, transform/network helpers, JVLmock-style reference resolution, resource helpers, and the Valheim SoftReferenceableAssets bridge.

Production runtime code is generic. Canary mods may drive coverage and tests, but their names or special cases are forbidden in the runtime.

## Exact Valheim runtime validation

CI compiles and validates the slim runtime against a pinned `Managed.zip` from the current Valheim installation.

The draft-release asset is pinned by SHA-256 and is used only as CI input; the game binaries are never committed to this repository or copied into the public package.

A Cecil contract checker verifies the private Valheim and SoftReferenceableAssets fields/methods reached by the slim runtime before compilation. This includes lifecycle hooks, ObjectDB/ZoneSystem/Dungeon internals, localization, and the soft-reference loader bridge.

## More World Locations canary

More World Locations AIO 5.1.1 is the first broad compatibility canary.

CI checks every DLL in the downloaded MWL package against the source-built slim `Jotunn.dll`. The main MWL assembly currently references:

- 26 Jotunn types
- 91 Jotunn members

All of those references resolve against the slim build.

This proves binary/API compatibility for the exercised surface. It does not replace an actual in-game regression test; world generation, asset lifetime, networking, and other Unity runtime behavior still need live-game testing.

## ValheimRAFT canary

ValheimRAFT 4.3.5 is the second broad compatibility canary. CI probes every DLL in the Thunderstore package against the same source-built slim `Jotunn.dll`.

Jotunn-dependent assemblies currently resolve completely:

- `DynamicLocations.dll`: 10 Jotunn types / 19 Jotunn members
- `ValheimRAFT.dll`: 9 Jotunn types / 9 Jotunn members
- `ValheimVehicles.dll`: 26 Jotunn types / 103 Jotunn members
- `ZdoWatcher.dll`: 8 Jotunn types / 13 Jotunn members

`ServerSync.dll` and `Zolantris.Shared.dll` have no Jotunn assembly reference.

This adds coverage for item/recipe/piece APIs, custom piece tables, GUI construction/styling and color picking, minimap lifecycle events, texture loading, transform helpers, and ZNet helpers. These are implemented generically; CI forbids ValheimRAFT-specific identifiers in production runtime source.

Like the MWL result, this is static binary/API compatibility. Actual in-game startup and behavior still need live Valheim regression testing.

## Installation

1. Install BepInEx 5 for Valheim.
2. Remove any separately installed official Jotunn package.
3. Remove obsolete prototype files such as `JotunnCompat.Preloader.dll` or `JotunnCompat.FastPath.dll` if present.
4. Extract `JotunnCompat.zip` directly into the **Valheim game directory**.
5. Launch Valheim normally.

The ZIP contains the correct `BepInEx/plugins/JotunnCompat/` layout.

## Build guardrails

CI:

- rejects target-mod identifiers in production runtime source;
- downloads and SHA-256-verifies the pinned exact Valheim `Managed.zip`;
- validates every private Valheim/SoftReferenceableAssets hook used by the runtime, including current ZNet admin, GameCamera input, minimap lifecycle, and image-decoding bridges;
- builds the slim `Jotunn.dll` against those exact managed assemblies;
- probes every DLL in the MWL and ValheimRAFT canary packages for Jotunn symbol compatibility;
- hashes, packages, and publishes the resulting artifact on a Windows runner.

## Scope

The current goal is practical drop-in compatibility, not a line-for-line fork of upstream Jotunn. APIs and backend behavior are implemented as needed to preserve observable contracts while avoiding unnecessary framework machinery.

Additional mods can be added as generic compatibility canaries to expand the exercised Jotunn surface.

See [COMPATIBILITY.md](COMPATIBILITY.md).

## License

This subproject is MIT licensed.

Jotunn itself is MIT licensed and remains the compatibility/API reference:
https://github.com/Valheim-Modding/Jotunn
