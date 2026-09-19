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

## ValheimRAFT

Canary package: ValheimRAFT 4.3.5 (`team0/ValheimRAFT` on Thunderstore).

ValheimRAFT is the second broad canary. It expands coverage beyond world-generation APIs into Jotunn's item/recipe/piece registration, GUI helpers, minimap lifecycle events, runtime texture loading, transform extensions, and network helpers.

### Jotunn surface exercised

The ValheimRAFT package currently exercises generic compatibility for:

- `ItemConfig`, `PieceConfig`, `PieceTableConfig`, `RecipeConfig`, and `RequirementConfig`
- `CustomItem`, `CustomPiece`, `CustomPieceTable`, and `CustomRecipe`
- `ItemManager`: item lookup, recipe lookup, and recipe registration
- `PieceManager`: piece/piece-table registration and lookup
- `GUIManager`: style helpers, buttons, inputs, scroll views, wood panels, color picker, input blocking, and GUI lifecycle
- `MinimapManager.OnVanillaMapDataLoaded`
- `LocalizationManager.AddToken`
- `AssetUtils.LoadTexture`
- transform/GameObject deep-child helpers
- `ZNetExtension` local/client/admin helpers
- synchronization compatibility types

The implementation remains generic: ValheimRAFT identifiers are rejected from production runtime source by CI.

### ValheimRAFT binary compatibility status

CI probes every DLL shipped in ValheimRAFT 4.3.5 against the source-built slim `Jotunn.dll`.

Jotunn-referencing assemblies:

- `DynamicLocations.dll`: **10 Jotunn types / 19 Jotunn members**
- `ValheimRAFT.dll`: **9 Jotunn types / 9 Jotunn members**
- `ValheimVehicles.dll`: **26 Jotunn types / 103 Jotunn members**
- `ZdoWatcher.dll`: **8 Jotunn types / 13 Jotunn members**

`ServerSync.dll` and `Zolantris.Shared.dll` contain no Jotunn assembly reference and are therefore skipped by the compatibility probe.

All referenced Jotunn symbols in every Jotunn-dependent DLL resolve against the slim build.

### Current-game binary validation

The same CI run compiles the slim runtime against the SHA-256-pinned unpublished-draft `Managed.zip` from the current Valheim installation. The Cecil contract checker also verifies current private/version-sensitive bridges used for:

- `ZNet` admin-list lookup
- `GameCamera` mouse capture/input blocking
- minimap map-ready lifecycle selection
- Unity runtime image decoding
- the existing ObjectDB, ZoneSystem, DungeonDB, localization, and SoftReferenceableAssets bridges

The exact-game contract check and slim compilation are currently passing.

### Remaining validation

ValheimRAFT now passes static binary/API compatibility. The next validation is live Valheim testing with official Jotunn absent and the slim replacement installed, checking at minimum:

- plugin startup without loader/Harmony exceptions
- vehicle hammer/item/piece registration
- recipes and custom piece tables
- vehicle configuration UI and color picker
- map pin/minimap behavior
- texture loading
- multiplayer/admin-dependent behavior

A failure found with this target must be fixed at the underlying generic Jotunn contract level. Do not add a ValheimRAFT-specific runtime branch.
