using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Jotunn.Extensions;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jotunn.Managers
{
    public sealed class AssetManager
    {
        private static readonly AssetManager _instance = new AssetManager();
        public static AssetManager Instance => _instance;

        private readonly Dictionary<AssetID, Object> runtimeAssets = new Dictionary<AssetID, Object>();
        private readonly Dictionary<AssetID, ResolveContext> resolve = new Dictionary<AssetID, ResolveContext>();
        private Dictionary<Type, Dictionary<string, AssetID>> names;

        public static event Action OnSoftReferenceableAssetsReady;

        private AssetManager() { }

        public bool IsReady()
        {
            try
            {
                var mi = typeof(Runtime).GetMethod("GetAllAssetPathsInBundleMappedToAssetID",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return mi != null && mi.Invoke(null, null) != null;
            }
            catch { return false; }
        }

        public AssetID GenerateAssetID(string asset)
        {
            uint u = unchecked((uint)Jotunn.Compat.StableHash(asset));
            return new AssetID(u, u, u, u);
        }

        public AssetID GenerateAssetID(Object asset) => GenerateAssetID(asset.name);

        public AssetID AddAsset(Object asset)
        {
            var id = GenerateAssetID(asset);
            if (!runtimeAssets.ContainsKey(id)) runtimeAssets[id] = asset;
            TryRegisterRuntimeAsset(id, asset);
            names = null;
            return id;
        }

        public AssetID GetAssetID<T>(string name) where T : Object => GetAssetID(typeof(T), name);

        public AssetID GetAssetID(Type type, string name)
        {
            EnsureNameMap();
            if (names != null)
            {
                if (names.TryGetValue(type, out var typed) && typed.TryGetValue(name, out var id)) return id;
                if (names.TryGetValue(typeof(Object), out var any) && any.TryGetValue(name, out id)) return id;
            }
            foreach (var kv in runtimeAssets)
                if (kv.Value && kv.Value.name == name && type.IsAssignableFrom(kv.Value.GetType())) return kv.Key;
            return new AssetID();
        }

        public SoftReference<T> GetSoftReference<T>(string name) where T : Object
        {
            var id = GetAssetID<T>(name);
            return id.IsValid ? new SoftReference<T>(id) : default;
        }

        public SoftReference<Object> GetSoftReference(Type type, string name)
        {
            var id = GetAssetID(type, name);
            return id.IsValid ? new SoftReference<Object>(id) : default;
        }

        public void ResolveMocksOnLoad(AssetID assetID) => ResolveMocksOnLoad(assetID, null, null);
        public void ResolveMocksOnLoad(AssetID assetID, Transform parent) => ResolveMocksOnLoad(assetID, parent, null);

        public void ResolveMocksOnLoad<T>(SoftReference<T> softRef, Transform parent, Action<T> callback) where T : Object
        {
            ResolveMocksOnLoad(softRef.m_assetID, parent, o => callback?.Invoke(o as T));
        }

        public void ResolveMocksOnLoad(AssetID id, Transform parent, Action<Object> callback)
        {
            if (!resolve.TryGetValue(id, out var ctx))
                resolve[id] = ctx = new ResolveContext();
            if (parent) ctx.Parent = parent;
            if (callback != null) ctx.Callback += callback;
        }

        private void EnsureNameMap()
        {
            if (names != null) return;
            names = new Dictionary<Type, Dictionary<string, AssetID>>();
            try
            {
                var mi = typeof(Runtime).GetMethod("GetAllAssetPathsInBundleMappedToAssetID",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var enumerable = mi?.Invoke(null, null) as IEnumerable;
                if (enumerable == null) return;
                foreach (var entry in enumerable)
                {
                    var et = entry.GetType();
                    string path = et.GetProperty("Key")?.GetValue(entry, null) as string;
                    object v = et.GetProperty("Value")?.GetValue(entry, null);
                    if (string.IsNullOrEmpty(path) || !(v is AssetID id)) continue;
                    string file = path.Replace('\\','/').Split('/').Last();
                    int dot = file.LastIndexOf('.');
                    string name = dot > 0 ? file.Substring(0, dot) : file;
                    string ext = dot > 0 ? file.Substring(dot + 1).ToLowerInvariant() : "";
                    Type type = ext == "prefab" ? typeof(GameObject) : typeof(Object);
                    if (!names.TryGetValue(type, out var map)) names[type] = map = new Dictionary<string,AssetID>();
                    if (!map.ContainsKey(name) || path.StartsWith("Assets/world/Locations", StringComparison.OrdinalIgnoreCase))
                        map[name] = id;
                }
            }
            catch (Exception ex) { Jotunn.Logger.LogDebug("Asset name map: " + ex.Message); }
        }

        private static object GetLoaderInstance()
        {
            var t = AccessTools.TypeByName("SoftReferenceableAssets.AssetBundleLoader");
            return AccessTools.Property(t, "Instance")?.GetValue(null, null)
                ?? AccessTools.Field(t, "Instance")?.GetValue(null);
        }

        private void TryRegisterRuntimeAsset(AssetID id, Object asset)
        {
            try
            {
                var loader = GetLoaderInstance();
                if (loader == null) return;
                RegisterRuntimeAsset(loader, id, asset);
            }
            catch (Exception ex) { Jotunn.Logger.LogDebug("Deferred runtime asset registration for " + asset.name + ": " + ex.Message); }
        }

        private static Array Append(Array source, object value)
        {
            var element = source.GetType().GetElementType();
            var dest = Array.CreateInstance(element, source.Length + 1);
            Array.Copy(source, dest, source.Length);
            dest.SetValue(value, source.Length);
            return dest;
        }

        private static void RegisterRuntimeAsset(object loader, AssetID id, Object asset)
        {
            var lt = loader.GetType();
            var assetIds = AccessTools.Field(lt, "m_assetIDToLoaderIndex")?.GetValue(loader) as IDictionary;
            if (assetIds == null || assetIds.Contains(id)) return;

            var bundleNames = AccessTools.Field(lt, "m_bundleNameToLoaderIndex")?.GetValue(loader) as IDictionary;
            var bundleField = AccessTools.Field(lt, "m_bundleLoaders");
            var assetField = AccessTools.Field(lt, "m_assetLoaders");
            if (bundleNames == null || bundleField == null || assetField == null) return;

            Array bundles = bundleField.GetValue(loader) as Array;
            Array assets = assetField.GetValue(loader) as Array;
            if (bundles == null || assets == null) return;

            string bundleName = "MWLCompat_" + asset.name + "_" + id.GetHashCode();
            int bundleIndex;
            if (bundleNames.Contains(bundleName))
            {
                bundleIndex = (int)bundleNames[bundleName];
            }
            else
            {
                Type bundleType = bundles.GetType().GetElementType();
                object bundle = Activator.CreateInstance(bundleType, new object[] { bundleName, "" });
                AccessTools.Method(bundleType, "HoldReference")?.Invoke(bundle, null);
                AccessTools.Method(bundleType, "SetDependencies")?.Invoke(bundle, new object[] { Array.Empty<string>() });
                bundleIndex = bundles.Length;
                bundleNames.Add(bundleName, bundleIndex);
                bundles = Append(bundles, bundle);
                bundleField.SetValue(loader, bundles);
            }

            Type locType = typeof(AssetID).Assembly.GetType("SoftReferenceableAssets.AssetLocation");
            object location = Activator.CreateInstance(locType, new object[] { bundleName, "MWLCompat/" + asset.name });
            Type loaderType = assets.GetType().GetElementType();
            object assetLoader = Activator.CreateInstance(loaderType, new object[] { id, location });
            AccessTools.Field(loaderType, "m_bundleLoaderIndex")?.SetValue(assetLoader, bundleIndex);
            AccessTools.Field(loaderType, "m_asset")?.SetValue(assetLoader, asset);
            AccessTools.Method(loaderType, "HoldReference")?.Invoke(assetLoader, null);

            assetIds.Add(id, assets.Length);
            assets = Append(assets, assetLoader);
            assetField.SetValue(loader, assets);
        }

        private sealed class ResolveContext
        {
            public Transform Parent;
            public Object Instance;
            public Action<Object> Callback;
        }

        internal void LoaderReady(object loader)
        {
            foreach (var kv in runtimeAssets) RegisterRuntimeAsset(loader, kv.Key, kv.Value);
            names = null;
            OnSoftReferenceableAssetsReady?.Invoke();
        }

        internal void OnAssetLoaded(object loaderObj, object result)
        {
            try
            {
                if (result == null || !string.Equals(result.ToString(), "Succeeded", StringComparison.OrdinalIgnoreCase)) return;
                var t = loaderObj.GetType();
                object idObj = AccessTools.Field(t, "m_assetID")?.GetValue(loaderObj);
                if (!(idObj is AssetID id) || !resolve.TryGetValue(id, out var ctx) || ctx.Instance) return;
                var real = AccessTools.Field(t, "m_asset")?.GetValue(loaderObj) as Object;
                if (!real) return;

                Object clone = Object.Instantiate(real, ctx.Parent);
                clone.name = real.name;
                if (clone is GameObject go) go.FixReferences(true); else clone.FixReferences();
                ctx.Instance = clone;
                AccessTools.Field(t, "m_asset")?.SetValue(loaderObj, clone);
                ctx.Callback?.Invoke(clone);
            }
            catch (Exception ex) { Jotunn.Logger.LogWarning("Soft-reference mock resolution failed: " + ex); }
        }

        [HarmonyPatch]
        private static class LoaderInitPatch
        {
            private static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("SoftReferenceableAssets.AssetBundleLoader");
                return AccessTools.Method(t, "OnInitCompleted");
            }
            private static void Postfix(object __instance) => Instance.LoaderReady(__instance);
        }

        [HarmonyPatch]
        private static class LoaderCallbackPatch
        {
            private static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("SoftReferenceableAssets.AssetLoader");
                return AccessTools.Method(t, "InvokeCallbacks");
            }
            private static void Prefix(object __instance, object result) => Instance.OnAssetLoaded(__instance, result);
        }
    }
}
