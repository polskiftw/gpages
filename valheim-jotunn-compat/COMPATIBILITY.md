# Compatibility Targets

This file tracks mods used to exercise the generic Jotunn compatibility layer.

A target listed here is a **canary/regression target**, never a runtime dependency. Target-specific names and behavior belong in this document, tests, fixtures, or benchmarks. They must not appear in production runtime logic under `src/JotunnCompat.Slim`.

## MoreWorldLocations_All

Repository: `jneb802/MoreWorldLocations_All`

Canary package: More World Locations AIO 5.1.1.

MWL was compiled against an older Jotunn assembly identity, while the slim replacement exposes Jotunn 2.30.1-compatible assembly identity. The compatibility probe deliberately binds MWL's Jotunn references to the supplied slim `Jotunn.dll` independent of the compile-time Jotunn version.

### Jotunn surface exercised

The current MWL binary uses a broad cross-section of Jotunn:

- `PrefabManager`: lifecycle event, get/add/remove/clone, `Cache.GetPrefab<T>`
- `ZoneManager`: lifecycle event, location containers, custom locations
- `DungeonManager`: lifecycle event, custom rooms and dungeon themes
- `ItemManager`: custom items, status effects, item-registration lifecycle
- `CustomPrefab`, `CustomItem`, `CustomLocation`, `CustomRoom`, `CustomStatusEffect`
- `LocationConfig` and `RoomConfig`
- `FixReferences` / JVLmock-style resolution
- localization helpers
- `NetworkManager` / `CustomRPC`
- GUI sprite lookup
- resource helpers
- Valheim SoftReferenceableAssets integration exposed through Jotunn

This makes MWL useful as a first canary because it exercises multiple managers and backend paths rather than one narrow helper.

### Current slim implementation

The package now uses our source-built `Jotunn.dll`; the official Jotunn runtime is not bundled.

The slim runtime currently provides generic implementations for the MWL-facing surface, including:

- prefab registration, cloning, cache lookup, and ZNetScene registration
- custom location registration and ZoneSystem lifecycle timing
- custom dungeon rooms and custom dungeon-theme handling
- custom item/status-effect registration
- localization storage/application
- JVLmock-style reference fixing
- custom RPC registration and Jotunn-compatible package framing
- embedded-resource asset/text helpers
- SoftReferenceableAssets lookup, runtime asset registration, and mock-resolution-on-load support

### Exact-game validation

CI downloads a private draft-release `Managed.zip` made from the current Valheim `Valheim_Data/Managed` directory and verifies its pinned SHA-256 before use.

A Cecil contract checker validates the private game members and SoftReferenceableAssets internals used by our reflection/Harmony bridge before the slim runtime is compiled.

Current exact-runtime contract status:

- pinned game binary input: verified
- private/runtime contract checks: passing
- slim runtime compilation against exact game binaries: passing
- generic-runtime invariant: passing

### MWL binary compatibility status

CI probes every DLL shipped in the MWL AIO package.

For the main MWL assembly:

- referenced Jotunn types: **26**
- referenced Jotunn members: **91**
- all referenced symbols resolve against the source-built slim `Jotunn.dll`

This is a static binary/API compatibility result. It does not prove full Unity runtime behavior.

### Remaining validation

The next required validation is live Valheim testing with:

- official Jotunn absent
- slim `Jotunn.dll` installed
- MWL installed
- startup free of loader/Harmony exceptions
- prefab/location/dungeon registration functioning
- locations actually appearing/generating
- networking behavior checked where MWL uses custom RPCs

A failure found with this target must be fixed at the underlying generic Jotunn contract level. Do not add a MWL-specific runtime branch.
