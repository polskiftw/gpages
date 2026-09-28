using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jotunn.Managers
{
    public sealed class AssetManager
    {
        private static AssetManager instance;
        public static AssetManager Instance => instance ??= new AssetManager();

        private readonly Dictionary<AssetID, Object> runtimeAssets = new Dictionary<AssetID, Object>();
        private readonly Dictionary<AssetID, ResolutionContext> resolve = new Dictionary<AssetID, ResolutionContext>();
        private readonly GameObject resolvedAssetsContainer;
        private Dictionary<Type, Dictionary<string, AssetID>> byType;

        public static event Action OnSoftReferenceableAssetsReady;

        private AssetManager()
        {
            resolvedAssetsContainer = new GameObject("Resolved Assets");
            resolvedAssetsContainer.transform.SetParent(Main.RootObject.transform);
            resolvedAssetsContainer.SetActive(false);

            var loaderType = AccessTools.TypeByName("SoftReferenceableAssets.AssetBundleLoader");
            var initCompleted = AccessTools.Method(loaderType, "OnInitCompleted");
            if (initCompleted != null)
            {
                Main.Harmony.Patch(
                    initCompleted,
                    postfix: new HarmonyMethod(
                        typeof(AssetManager),
                        nameof(AssetBundleLoaderReady)));
            }
        }

        private static void AssetBundleLoaderReady(object __instance)
        {
            Instance.OnLoaderReady(__instance);
        }

        private void OnLoaderReady(object loader)
        {
            byType = null;

            foreach (var pair in runtimeAssets.ToArray())
            {
                TryRegisterRuntimeAsset(pair.Key, pair.Value);
            }

            OnSoftReferenceableAssetsReady?.Invoke();
        }

        public AssetID GenerateAssetID(string asset)
        {
            uint hash = (uint)(asset ?? string.Empty).GetStableHashCode();
            return new AssetID(hash, hash, hash, hash);
        }

        internal AssetID AddAsset(Object asset)
        {
            if (!asset) return default;
            var id = GenerateAssetID(asset.name);
            runtimeAssets[id] = asset;
            if (IsReady())
            {
                TryRegisterRuntimeAsset(id, asset);
            }
            return id;
        }


        private void TryRegisterRuntimeAsset(AssetID assetID, Object asset)
        {
            var loader = GameInternals.AssetLoaderObject;
            if (loader == null || !asset) return;

            try
            {
                var loaderType = loader.GetType();
                var idMapField = AccessTools.Field(loaderType, "m_assetIDToLoaderIndex");
                var assetLoadersField = AccessTools.Field(loaderType, "m_assetLoaders");
                var bundleMapField = AccessTools.Field(loaderType, "m_bundleNameToLoaderIndex");
                var bundleLoadersField = AccessTools.Field(loaderType, "m_bundleLoaders");
                if (idMapField == null || assetLoadersField == null || bundleMapField == null || bundleLoadersField == null) return;

                var idMap = idMapField.GetValue(loader);
                var containsKey = AccessTools.Method(idMap.GetType(), "ContainsKey");
                if ((bool)containsKey.Invoke(idMap, new object[] { assetID })) return;

                var bundleMap = (System.Collections.IDictionary)bundleMapField.GetValue(loader);
                var bundleLoaders = (Array)bundleLoadersField.GetValue(loader);
                var bundleLoaderType = bundleLoaders.GetType().GetElementType();
                var bundleName = "JVL_Slim_" + assetID.ToString();
                int bundleIndex;

                if (bundleMap.Contains(bundleName))
                {
                    bundleIndex = (int)bundleMap[bundleName];
                }
                else
                {
                    var bundleLoader = Activator.CreateInstance(
                        bundleLoaderType,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        args: new object[] { bundleName, string.Empty },
                        culture: null);
                    AccessTools.Method(bundleLoaderType, "HoldReference")?.Invoke(
                        bundleLoader,
                        Array.Empty<object>());

                    // Match upstream Jotunn's ordering exactly. SetDependencies
                    // consults the loader's bundle-name map, so the synthetic
                    // bundle must already exist in the map/array before it is
                    // asked to resolve dependencies.
                    bundleIndex = bundleLoaders.Length;
                    bundleMap.Add(bundleName, bundleIndex);

                    var expandedBundles = Array.CreateInstance(
                        bundleLoaderType,
                        bundleIndex + 1);
                    Array.Copy(bundleLoaders, expandedBundles, bundleIndex);
                    expandedBundles.SetValue(bundleLoader, bundleIndex);
                    bundleLoadersField.SetValue(loader, expandedBundles);

                    AccessTools.Method(bundleLoaderType, "SetDependencies")?.Invoke(
                        bundleLoader,
                        new object[] { Array.Empty<string>() });

                    // BundleLoader is a value type in current Valheim. Reflection
                    // mutates the boxed copy, so write it back after dependencies
                    // have been assigned.
                    expandedBundles.SetValue(bundleLoader, bundleIndex);
                    bundleLoadersField.SetValue(loader, expandedBundles);
                }

                var assetLocationType = AccessTools.TypeByName("SoftReferenceableAssets.AssetLocation");
                var assetLoaderType = AccessTools.TypeByName("SoftReferenceableAssets.AssetLoader");
                if (assetLocationType == null || assetLoaderType == null) return;

                var location = Activator.CreateInstance(
                    assetLocationType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { bundleName, "JotunnSlim/Prefabs/" + asset.name },
                    culture: null);
                var assetLoader = Activator.CreateInstance(
                    assetLoaderType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { assetID, location },
                    culture: null);

                AccessTools.Field(assetLoaderType, "m_asset")?.SetValue(assetLoader, asset);
                AccessTools.Method(assetLoaderType, "HoldReference")?.Invoke(
                    assetLoader,
                    Array.Empty<object>());

                var assetLoaders = (Array)assetLoadersField.GetValue(loader);
                int assetIndex = assetLoaders.Length;
                var expandedAssets = Array.CreateInstance(assetLoaderType, assetIndex + 1);
                Array.Copy(assetLoaders, expandedAssets, assetIndex);
                expandedAssets.SetValue(assetLoader, assetIndex);
                assetLoadersField.SetValue(loader, expandedAssets);

                var add = AccessTools.Method(idMap.GetType(), "Add");
                add.Invoke(idMap, new object[] { assetID, assetIndex });
            }
            catch (Exception ex)
            {
                var detail = ex;
                while (detail is TargetInvocationException &&
                       detail.InnerException != null)
                {
                    detail = detail.InnerException;
                }

                Logger.LogWarning(
                    "Could not register runtime soft-reference asset '" +
                    asset.name + "': " + detail);
            }
        }

        internal bool IsReady() => GameInternals.AssetLoaderReady;

        internal SoftReference<Object> GetSoftReference(Type type, string name)
        {
            var id = GetAssetID(type, name);
            return id.IsValid ? new SoftReference<Object>(id) : default;
        }

        public SoftReference<T> GetSoftReference<T>(string name) where T : Object
        {
            var id = GetAssetID(typeof(T), name);
            return id.IsValid ? new SoftReference<T>(id) : default;
        }

        private AssetID GetAssetID(Type type, string name)
        {
            EnsureIndex();
            if (byType.TryGetValue(type, out var map) && map.TryGetValue(name, out var id)) return id;
            if (byType.TryGetValue(typeof(Object), out map) && map.TryGetValue(name, out id)) return id;
            foreach (var kv in runtimeAssets)
                if (kv.Value && kv.Value.name == name && type.IsAssignableFrom(kv.Value.GetType())) return kv.Key;
            return default;
        }

        private void EnsureIndex()
        {
            if (byType != null) return;
            byType = new Dictionary<Type, Dictionary<string, AssetID>>();
            if (!IsReady()) return;

            var pathsByType =
                new Dictionary<Type, Dictionary<string, string>>();

            foreach (var pair in Runtime.GetAllAssetPathsInBundleMappedToAssetID())
            {
                var path = (pair.Key ?? string.Empty).Replace('\\', '/');
                if (path.Equals(
                        "Assets/UI/prefabs/radials/elements/Hammer.prefab",
                        StringComparison.OrdinalIgnoreCase) ||
                    path.Equals(
                        "Assets/UI/prefabs/Radial/elements/Hammer.prefab",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Prefer the real hammer item over the same-named UI prefab.
                    continue;
                }

                var file = path.Split('/').Last();
                var dot = file.LastIndexOf('.');
                var name = dot > 0 ? file.Substring(0, dot) : file;
                var ext = dot > 0
                    ? file.Substring(dot + 1).ToLowerInvariant()
                    : string.Empty;
                Type type = ext == "prefab"
                    ? typeof(GameObject)
                    : typeof(Object);

                if (!byType.TryGetValue(type, out var map))
                {
                    byType[type] = map =
                        new Dictionary<string, AssetID>();
                }

                if (!pathsByType.TryGetValue(type, out var paths))
                {
                    pathsByType[type] = paths =
                        new Dictionary<string, string>();
                }

                if (map.ContainsKey(name) &&
                    paths.TryGetValue(name, out var oldPath) &&
                    SkipAmbiguousPath(oldPath, path, ext))
                {
                    continue;
                }

                map[name] = pair.Value;
                paths[name] = path;
            }
        }

        private static bool SkipAmbiguousPath(
            string oldPath,
            string newPath,
            string extension)
        {
            if (!string.Equals(
                    extension,
                    "prefab",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (oldPath.StartsWith(
                    "Assets/world/Locations",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Prefer a later non-location prefab with the same short name.
                return false;
            }

            if (newPath.StartsWith(
                    "Assets/world/Locations",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (oldPath.StartsWith(
                    "Assets/world/Props/DeepNorth",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Current Valheim contains new Deep North assets whose names
                // collide with older vanilla assets. Jotunn 2.30.2 keeps the
                // later legacy asset for mock resolution in this case.
                return false;
            }

            return true;
        }

        public void ResolveMocksOnLoad<T>(SoftReference<T> softReference, Transform parent, Action<T> callback) where T : Object
        {
            ResolveMocksOnLoad(softReference.m_assetID, parent, obj => callback?.Invoke(obj as T));
        }

        public void ResolveMocksOnLoad(AssetID assetID, Transform parent, Action<Object> callback)
        {
            if (!resolve.TryGetValue(assetID, out var context))
            {
                resolve[assetID] = context = new ResolutionContext
                {
                    Parent = resolvedAssetsContainer.transform
                };
            }
            if (parent != null) context.Parent = parent;
            context.Callback += callback;

            if (runtimeAssets.TryGetValue(assetID, out var direct) && direct)
                context.Resolve(direct);
        }

        internal void OnLoaded(AssetID assetID, ref Object asset, LoadResult result)
        {
            if (result != LoadResult.Succeeded || !resolve.TryGetValue(assetID, out var context) || !asset) return;
            context.Resolve(asset);
            if (context.Asset) asset = context.Asset;
        }

        private sealed class ResolutionContext
        {
            public Transform Parent;
            public Action<Object> Callback;
            public Object Asset;

            public void Resolve(Object source)
            {
                if (Asset) { Callback?.Invoke(Asset); return; }
                Asset = Object.Instantiate(source, Parent);
                Asset.name = source.name;
                if (Asset is GameObject go) go.FixReferences(true);
                Callback?.Invoke(Asset);
            }
        }

        [HarmonyPatch]
        internal static class Patches
        {
            private static MethodBase TargetMethod()
            {
                var assetLoader = AccessTools.TypeByName("SoftReferenceableAssets.AssetLoader");
                return AccessTools.Method(assetLoader, "InvokeCallbacks");
            }

            [HarmonyPrefix]
            private static void Loaded(AssetID ___m_assetID, ref Object ___m_asset, LoadResult result)
                => Instance.OnLoaded(___m_assetID, ref ___m_asset, result);
        }
    }
}
