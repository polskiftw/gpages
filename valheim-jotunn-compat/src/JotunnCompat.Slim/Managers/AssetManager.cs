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
        private Dictionary<Type, Dictionary<string, AssetID>> byType;

        private AssetManager() { }

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
            return id;
        }

        internal bool IsReady() => Runtime.s_assetLoader != null;

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
            foreach (var pair in Runtime.GetAllAssetPathsInBundleMappedToAssetID())
            {
                var file = pair.Key.Split('/').Last();
                var dot = file.LastIndexOf('.');
                var name = dot > 0 ? file.Substring(0, dot) : file;
                var ext = dot > 0 ? file.Substring(dot + 1).ToLowerInvariant() : string.Empty;
                Type type = ext == "prefab" ? typeof(GameObject) : typeof(Object);
                if (!byType.TryGetValue(type, out var map)) byType[type] = map = new Dictionary<string, AssetID>();
                if (!map.ContainsKey(name)) map.Add(name, pair.Value);
            }
        }

        public void ResolveMocksOnLoad<T>(SoftReference<T> softReference, Transform parent, Action<T> callback) where T : Object
        {
            ResolveMocksOnLoad(softReference.m_assetID, parent, obj => callback?.Invoke(obj as T));
        }

        public void ResolveMocksOnLoad(AssetID assetID, Transform parent, Action<Object> callback)
        {
            if (!resolve.TryGetValue(assetID, out var context))
                resolve[assetID] = context = new ResolutionContext();
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
