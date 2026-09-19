# Compatibility Targets

This file tracks mods used to exercise the generic Jotunn compatibility layer.

A target listed here is a **canary/regression target**, never a runtime dependency. Target-specific names and behavior belong in this document, tests, fixtures, or benchmarks. They must not appear in production runtime logic under `src/JotunnCompat.FastPath`.

## MoreWorldLocations_All

Repository: `jneb802/MoreWorldLocations_All`

Inspected target: current `master` at `f5d0bd659b8ee7d80fec623059adbed31dc283e1`

The AIO plugin identifies itself as version 5.1.1 and declares a BepInEx dependency through `Jotunn.Main.ModGuid`. It also checks for Jotunn 2.28.0 or newer.

### Jotunn surface exercised

The current source uses a broad cross-section of Jotunn:

- `PrefabManager`
  - `OnVanillaPrefabsAvailable`
  - `Instance.GetPrefab`
  - `Instance.AddPrefab`
  - `Instance.RemovePrefab`
  - `Instance.CreateClonedPrefab`
  - `Cache.GetPrefab<T>`
- `ZoneManager`
  - `OnVanillaLocationsAvailable`
  - `CreateLocationContainer`
  - `AddCustomLocation`
- `DungeonManager`
  - `OnVanillaRoomsAvailable`
  - `RegisterDungeonTheme`
  - custom room registration paths
- `ItemManager`
  - custom item registration
  - `OnItemsRegistered`
- Jotunn entities/configs
  - `CustomPrefab`
  - `CustomItem`
  - `CustomLocation`
  - `LocationConfig`
  - `RoomConfig`
- mock/reference system
  - `FixReferences`
  - bundled `JVLmock_` reference resolution
- localization helpers
- `NetworkManager` / `CustomRPC`
- utility/resource helpers used by its YAML/config paths
- Valheim SoftReferenceableAssets integration exposed through Jotunn

This makes MWL useful as a first canary because it is not narrowly coupled to one Jotunn helper.

### Generic fast paths currently relevant

- PatchInit discovery preflight
- one-pass automatic localization discovery
- positive memoization of successful `PrefabManager.Cache.GetPrefab(Type, string)` lookups
- depth-aware duplicate suppression while traversing Jotunn mock-reference object graphs
- successful mock-resolution reuse within one top-level reference-fix traversal

### Still delegated to upstream Jotunn

The compatibility package intentionally still delegates the following behavior to the bundled upstream Jotunn implementation while their generic replacements are developed and tested:

- Zone/location registration
- dungeon-room registration and theme handling
- item registration
- SoftReferenceableAssets registration/lifetime behavior
- custom RPC transport
- the public entity/config object model

Keeping those paths upstream preserves exact behavior while each subsystem is replaced independently.

### Validation status

- Source/API inventory: complete for the current target revision.
- Generic-runtime invariant: enforced in CI.
- Jotunn hook contract: enforced in CI against the pinned upstream DLL.
- Fast-path assembly build: enforced in CI.
- Live Valheim runtime regression: not automatable in GitHub Actions and must be exercised in a real Valheim+BepInEx client/server environment.

A failure found with this target must be fixed at the underlying Jotunn contract level. Do not add a target-specific runtime branch.
