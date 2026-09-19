# Compatibility Targets

This file tracks mods used to exercise the generic Jotunn compatibility layer.

A target listed here is a **canary/regression target**, never a runtime dependency. Target-specific names and behavior belong in this document, tests, fixtures, or benchmarks. They must not appear in production runtime logic under `src/JotunnCompat.Runtime` or `src/JotunnCompat.Preloader`.

## MoreWorldLocations_All

Repository: `jneb802/MoreWorldLocations_All`

Inspected target: current `master` at `f5d0bd659b8ee7d80fec623059adbed31dc283e1`

The AIO plugin identifies itself as version 5.1.1 and declares a BepInEx dependency through `Jotunn.Main.ModGuid`. Its code checks for Jotunn 2.28.0 or newer; its current Thunderstore manifest declares Jotunn 2.29.2.

### Jotunn surface exercised

The current source uses a broad cross-section of Jotunn:

- `PrefabManager`: lifecycle event, get/add/remove/clone, `Cache.GetPrefab<T>`
- `ZoneManager`: lifecycle event, location containers, custom locations
- `DungeonManager`: lifecycle event, custom rooms and dungeon themes
- `ItemManager`: custom items and item-registration lifecycle
- `CustomPrefab`, `CustomItem`, `CustomLocation`
- `LocationConfig` and `RoomConfig`
- `FixReferences` and bundled `JVLmock_` resolution
- localization helpers
- `NetworkManager` / `CustomRPC`
- utility/resource helpers
- Valheim SoftReferenceableAssets integration exposed through Jotunn

This makes MWL useful as a first canary because it is not narrowly coupled to one helper.

### Generic compatibility work currently relevant

- early preloader installation before any dependent plugin `Awake`
- PatchInit discovery preflight
- one-pass automatic localization discovery
- positive memoization of successful prefab-cache lookups
- depth-aware duplicate suppression in mock-reference graphs
- successful mock-resolution reuse within one top-level reference-fix traversal
- graceful handling of Jotunn's known invalid-CodeMatcher AssetManager collision

### Still delegated to upstream Jotunn

The compatibility package intentionally delegates these contracts to the bundled upstream implementation while generic replacements are developed and tested:

- zone/location registration
- dungeon-room registration and theme handling
- item registration
- SoftReferenceableAssets registration/lifetime behavior
- custom RPC transport
- the public entity/config object model

Keeping those paths upstream preserves exact behavior while each subsystem is replaced independently.

### Validation status

- Source/API inventory: complete for the inspected target revision.
- Generic-runtime invariant: enforced in CI.
- Jotunn hook contract: enforced in CI against the pinned upstream DLL.
- BepInEx 5 preloader-discovery contract: enforced in CI.
- Runtime/preloader compilation: enforced in CI.
- Live Valheim regression: requires a real Valheim+BepInEx client/server environment.

A failure found with this target must be fixed at the underlying Jotunn contract level. Do not add a target-specific runtime branch.
