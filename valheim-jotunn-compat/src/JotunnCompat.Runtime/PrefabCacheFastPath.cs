using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JotunnCompat.Runtime
{
    /// <summary>
    /// Positive memoization in front of Jotunn PrefabManager.Cache.
    ///
    /// Upstream already caches its expensive Resources.FindObjectsOfTypeAll fallback, but
    /// every GetPrefab call still checks AssetManager, constructs a SoftReference, and calls
    /// Load before consulting that cache. Successful soft-reference loads are intentionally
    /// never released by upstream Jotunn, so re-running that path for the same (type, name)
    /// is unnecessary.
    ///
    /// We cache successful results only and clear them whenever Jotunn clears its own cache.
    /// Missing assets are never cached because an asset can become available later.
    /// </summary>
    internal static class PrefabCacheFastPath
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<CacheKey, Object> PositiveCache =
            new Dictionary<CacheKey, Object>();

        public static void Install(Harmony harmony)
        {
            var cacheType = AccessTools.TypeByName("Jotunn.Managers.PrefabManager+Cache");
            if (cacheType == null)
            {
                throw new TypeLoadException(
                    "Required Jotunn type not found: Jotunn.Managers.PrefabManager+Cache");
            }

            var getPrefab = AccessTools.Method(
                cacheType,
                "GetPrefab",
                new[] { typeof(Type), typeof(string) });

            if (getPrefab == null)
            {
                throw new MissingMethodException(
                    "Jotunn.Managers.PrefabManager+Cache",
                    "GetPrefab(Type, string)");
            }

            MethodInfo clear = null;
            foreach (var method in cacheType.GetMethods(
                         BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name == "Clear" &&
                    !method.IsGenericMethod &&
                    method.GetParameters().Length == 0)
                {
                    clear = method;
                    break;
                }
            }

            if (clear == null)
            {
                throw new MissingMethodException(
                    "Jotunn.Managers.PrefabManager+Cache",
                    "Clear()");
            }

            harmony.Patch(
                getPrefab,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(PrefabCacheFastPath), nameof(GetPrefabPrefix))),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(PrefabCacheFastPath), nameof(GetPrefabPostfix))));

            harmony.Patch(
                clear,
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(PrefabCacheFastPath), nameof(ClearPostfix))));
        }

        private static bool GetPrefabPrefix(
            Type type,
            string name,
            ref Object __result)
        {
            if (type == null || name == null || IsIndividuallyInvalidatedTextureType(type))
            {
                return true;
            }

            lock (Sync)
            {
                if (!PositiveCache.TryGetValue(new CacheKey(type, name), out var cached))
                {
                    return true;
                }

                __result = cached;
                return false;
            }
        }

        private static void GetPrefabPostfix(
            Type type,
            string name,
            Object __result)
        {
            if (type == null ||
                name == null ||
                ReferenceEquals(__result, null) ||
                IsIndividuallyInvalidatedTextureType(type))
            {
                return;
            }

            lock (Sync)
            {
                PositiveCache[new CacheKey(type, name)] = __result;
            }
        }

        private static void ClearPostfix()
        {
            lock (Sync)
            {
                PositiveCache.Clear();
            }
        }

        private static bool IsIndividuallyInvalidatedTextureType(Type type)
        {
            // Jotunn selectively calls Cache.Clear<Texture>() after more textures become
            // available. Do not memoize texture-family lookups so that selective invalidation
            // remains exactly effective even though Harmony does not patch the generic Clear<T>.
            var fullName = type.FullName;
            return fullName == "UnityEngine.Texture" ||
                   fullName == "UnityEngine.Texture2D" ||
                   fullName == "UnityEngine.Cubemap";
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            private readonly Type type;
            private readonly string name;

            public CacheKey(Type type, string name)
            {
                this.type = type;
                this.name = name;
            }

            public bool Equals(CacheKey other)
            {
                return ReferenceEquals(type, other.type) &&
                       string.Equals(name, other.name, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((type != null ? type.GetHashCode() : 0) * 397) ^
                           (name != null ? StringComparer.Ordinal.GetHashCode(name) : 0);
                }
            }
        }
    }
}
