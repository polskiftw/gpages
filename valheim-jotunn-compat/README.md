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

The current release bundles the **unmodified official Jotunn 2.30.1 release DLL** and a tiny companion BepInEx plugin, `JotunnCompat.FastPath.dll`. CI downloads that DLL directly from Jotunn's GitHub release and verifies its pinned SHA-256 before packaging.

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

### Prefab cache fast path

Jotunn already caches the expensive `Resources.FindObjectsOfTypeAll` fallback, but every `PrefabManager.Cache.GetPrefab(Type, string)` call still checks `AssetManager`, creates a SoftReference, and calls `Load` before consulting that cache.

The compatibility layer memoizes **successful lookups only**. Upstream Jotunn intentionally keeps those SoftReference assets loaded, so repeating the same lookup path is unnecessary. The memoizer is cleared whenever Jotunn clears its own cache, and texture-family lookups are deliberately excluded because Jotunn selectively invalidates textures later in startup.

### Mock-reference traversal fast path

Jotunn's `JVLmock_` resolver recursively reflects arbitrary component/object graphs with a depth limit of five. Cyclic and multiply-referenced graphs can cause the same object to be walked repeatedly.

The compatibility layer records the shallowest depth at which an object has already been processed during one top-level reference-fix operation. A duplicate visit is skipped only when the earlier visit had equal or greater remaining traversal reach. Successful mock resolutions are also reused inside that one traversal.

The cache is traversal-local, so arbitrary mod objects are not retained after reference fixing completes.

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

Future work should profile real mod packs and replace one Jotunn subsystem at a time while maintaining a corpus of existing Jotunn-dependent mods as compatibility tests. See [COMPATIBILITY.md](COMPATIBILITY.md) for the canary matrix. Candidate areas include manager initialization, asset handling, GUI helpers, synchronization, prefab/item/piece registration, and location/zone management.

The important rule is that optimization happens behind the existing Jotunn contract. Mods do not get forced onto a new API.

## License

This subproject is MIT licensed. The bundled Jotunn binary remains covered by Jotunn's MIT license, and the upstream copyright/license text is preserved in [LICENSE](LICENSE).

Jotunn: https://github.com/Valheim-Modding/Jotunn
