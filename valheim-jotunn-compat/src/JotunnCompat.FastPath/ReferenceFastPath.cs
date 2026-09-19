using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JotunnCompat.FastPath
{
    /// <summary>
    /// Generic acceleration for Jotunn's mock-reference resolver.
    ///
    /// Jotunn recursively walks arbitrary component/object graphs and deliberately caps
    /// recursion at depth five because cyclic graphs can otherwise recurse forever.
    /// A single graph can still reach the same object repeatedly through different paths.
    ///
    /// This layer remembers object identities for one top-level FixReferences traversal.
    /// Re-visiting an already-completely-processed object cannot discover any new fields,
    /// so skipping the duplicate walk preserves the resulting references while avoiding
    /// repeated reflection and cycle churn.
    /// </summary>
    internal static class ReferenceFastPath
    {
        [ThreadStatic]
        private static HashSet<object> visitedObjects;

        [ThreadStatic]
        private static Dictionary<MockLookupKey, Object> resolvedMocks;

        public static void Install(Harmony harmony)
        {
            var mockManager = AccessTools.TypeByName("Jotunn.Managers.MockManager");
            if (mockManager == null)
            {
                throw new TypeLoadException("Required Jotunn type not found: Jotunn.Managers.MockManager");
            }

            var fixReferences = AccessTools.Method(
                mockManager,
                "FixReferences",
                new[] { typeof(object), typeof(int) });

            if (fixReferences == null)
            {
                throw new MissingMethodException(
                    "Jotunn.Managers.MockManager",
                    "FixReferences(object, int)");
            }

            var getRealPrefab = AccessTools.Method(
                mockManager,
                "GetRealPrefabFromMock",
                new[] { typeof(Object), typeof(Type) });

            if (getRealPrefab == null)
            {
                throw new MissingMethodException(
                    "Jotunn.Managers.MockManager",
                    "GetRealPrefabFromMock(UnityEngine.Object, Type)");
            }

            harmony.Patch(
                fixReferences,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ReferenceFastPath), nameof(FixReferencesPrefix))),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(ReferenceFastPath), nameof(FixReferencesPostfix))));

            harmony.Patch(
                getRealPrefab,
                prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(ReferenceFastPath), nameof(GetRealPrefabPrefix))),
                postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(ReferenceFastPath), nameof(GetRealPrefabPostfix))));
        }

        private static bool FixReferencesPrefix(object objectToFix, int depth, out bool __state)
        {
            __state = depth == 0;

            if (__state || visitedObjects == null)
            {
                visitedObjects = new HashSet<object>(ReferenceComparer.Instance);
                resolvedMocks = new Dictionary<MockLookupKey, Object>(MockLookupComparer.Instance);
            }

            if (objectToFix == null)
            {
                return false;
            }

            // The first visit performs Jotunn's original implementation.
            // Later visits to the identical object in this traversal are redundant.
            return visitedObjects.Add(objectToFix);
        }

        private static void FixReferencesPostfix(bool __state)
        {
            if (!__state)
            {
                return;
            }

            // Do not retain arbitrary mod object graphs after one top-level traversal.
            visitedObjects = null;
            resolvedMocks = null;
        }

        private static bool GetRealPrefabPrefix(
            Object unityObject,
            Type mockObjectType,
            ref Object __result)
        {
            if (resolvedMocks == null ||
                ReferenceEquals(unityObject, null) ||
                mockObjectType == null)
            {
                return true;
            }

            var key = new MockLookupKey(unityObject, mockObjectType);
            if (!resolvedMocks.TryGetValue(key, out var cached))
            {
                return true;
            }

            __result = cached;
            return false;
        }

        private static void GetRealPrefabPostfix(
            Object unityObject,
            Type mockObjectType,
            Object __result)
        {
            // Cache successful resolutions only. Failed resolutions deliberately continue
            // through upstream Jotunn so its diagnostics remain behavior-compatible.
            if (resolvedMocks == null ||
                ReferenceEquals(unityObject, null) ||
                mockObjectType == null ||
                ReferenceEquals(__result, null))
            {
                return;
            }

            resolvedMocks[new MockLookupKey(unityObject, mockObjectType)] = __result;
        }

        private readonly struct MockLookupKey
        {
            public readonly Object Mock;
            public readonly Type RequestedType;

            public MockLookupKey(Object mock, Type requestedType)
            {
                Mock = mock;
                RequestedType = requestedType;
            }
        }

        private sealed class MockLookupComparer : IEqualityComparer<MockLookupKey>
        {
            public static readonly MockLookupComparer Instance = new MockLookupComparer();

            public bool Equals(MockLookupKey x, MockLookupKey y)
            {
                return ReferenceEquals(x.Mock, y.Mock) &&
                       ReferenceEquals(x.RequestedType, y.RequestedType);
            }

            public int GetHashCode(MockLookupKey obj)
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(obj.Mock) * 397) ^
                           RuntimeHelpers.GetHashCode(obj.RequestedType);
                }
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
