# Jotunn Compatibility Layer

A compatibility-first, performance-oriented Jotunn distribution for Valheim.

## Goal

Existing mods compiled against Jotunn should load without being ported. The compatibility package preserves what those binaries already expect:

- assembly file/name: `Jotunn.dll`
- BepInEx plugin GUID: `com.jotunn.jotunn`
- Jotunn public namespaces, types, members, version, and network behavior
- existing behavior unless a subsystem is deliberately replaced behind that same contract

This is not a new framework for mods to target. Mods continue targeting Jotunn.

## Architecture

The package contains only two runtime DLLs:

- `BepInEx/plugins/JotunnCompat/Jotunn.dll` — the **unmodified official Jotunn 2.30.1 release DLL**
- `BepInEx/patchers/JotunnCompat.Preloader.dll` — the generic compatibility/optimization layer

CI downloads Jotunn directly from its GitHub release and verifies a pinned SHA-256 before packaging.

The preloader arms before BepInEx starts plugin discovery. It watches for the CLR assembly named `Jotunn` and installs the generic Harmony layer immediately when that assembly loads, before BepInEx can construct or call `Awake` on Jotunn-dependent mods. This removes sibling-plugin load-order ambiguity while leaving `Jotunn.dll` itself byte-for-byte upstream.

## Current generic improvements

### PatchInit discovery

Jotunn's obsolete `PatchInit` compatibility path reflects every type and every public static method in every Jotunn-dependent assembly. The replacement first checks assembly metadata for `PatchInitAttribute` and only reflects assemblies that can actually contain it.

### Automatic localization discovery

Upstream recursively scans the full BepInEx plugin tree separately for five localization patterns. The replacement performs one recursive filesystem walk, classifies matches in memory, and avoids initializing the localization manager when no automatic translation files exist.

### Prefab lookup memoization

`PrefabManager.Cache.GetPrefab(Type, string)` normally repeats the AssetManager/SoftReference lookup/load path even after a successful lookup. Successful non-texture lookups are memoized and invalidated with Jotunn's own cache. Missing assets are never negatively cached.

### Mock-reference traversal

Jotunn's `JVLmock_` resolver reflects arbitrary object graphs with a depth limit of five. The compatibility layer remembers the shallowest processed depth per object for one top-level traversal and reuses successful mock resolutions inside that traversal. This avoids duplicate reflection/cycle churn while preserving the original depth budget.

### AssetManager collision hardening

Jotunn 2.30.1's asset-path transpiler assumes its target `Dictionary.Add` instruction still exists. If another mod has already transformed that instruction, Harmony's `CodeMatcher.SetInstruction` throws and can kill AssetManager initialization.

The early compatibility layer catches **only that exact invalid-CodeMatcher failure** and falls back to the incoming instruction stream. Other exceptions are preserved.

## Compatibility strategy

A hand-written partial clone of common Jotunn APIs would not be a real drop-in replacement: already-compiled mods bind to exact types and signatures, including obscure ones.

The project therefore keeps upstream Jotunn as the ABI floor while replacing internals incrementally. A subsystem is only replaced when its observable contract can be preserved. Canary mods reveal missing behavior, but production runtime code may never special-case a canary.

See [COMPATIBILITY.md](COMPATIBILITY.md).

## Installation

1. Install BepInEx 5 for Valheim.
2. Remove any separately installed official Jotunn package.
3. If upgrading from the old compatibility prototype, remove `JotunnCompat.FastPath.dll`.
4. Extract `JotunnCompat.zip` directly into the **Valheim game directory**. The ZIP already contains the correct `BepInEx/plugins` and `BepInEx/patchers` paths.
5. Launch Valheim normally.

## Build guardrails

CI:

- rejects target-mod identifiers in production runtime/preloader source;
- pins and hashes the canonical upstream Jotunn DLL;
- verifies every private/public Jotunn hook our runtime expects;
- verifies `JotunnCompat.Preloader.dll` has the exact static patcher shape BepInEx 5 discovers;
- builds, hashes, packages, and publishes on a Windows runner.

## Roadmap

Continue replacing generic Jotunn subsystems one at a time, driven by profiling and compatibility canaries: asset handling, zone/location registration, dungeon handling, items/pieces, synchronization/networking, and other managers where a simpler implementation can preserve the same contract.

## License

This subproject is MIT licensed. The bundled Jotunn binary remains covered by Jotunn's MIT license, and the upstream copyright/license text is preserved in [LICENSE](LICENSE).

Jotunn: https://github.com/Valheim-Modding/Jotunn
