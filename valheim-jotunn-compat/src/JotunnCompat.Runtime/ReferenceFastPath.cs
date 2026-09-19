using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JotunnCompat.Runtime
{
    /// <summary>
    /// Generic acceleration for Jotunn's mock-reference resolver.
    ///
    /// Jotunn recursively walks arbitrary component/object graphs and deliberately caps
    /// recursion at depth five because cyclic graphs can otherwise recurse forever.
    /// A single graph can still reach the same object repeatedly through different paths.
    ///
    /// This layer remembers the shallowest depth at which each object was processed during
    /// one top-level FixReferences traversal. Re-visiting an object at an equal or deeper
    /// depth cannot expose anything the earlier walk could not already reach, so that duplicate
    /// work can be skipped without changing Jotunn's depth-limited behavior.
    /// </summary>
    internal static class ReferenceFastPath
    {
        [ThreadStatic]
        private static Dictionary<object, int> bestVisitedDepth;

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

            if (__state || bestVisitedDepth == null)
            {
                bestVisitedDepth = new Dictionary<object, int>(ReferenceComparer.Instance);
                resolvedMocks = new Dictionary<MockLookupKey, Object>(MockLookupComparer.Instance);
            }

            if (objectToFix == null)
            {
                return false;
            }

            // Upstream does no work at depth 5. Do not mark an object as processed at
            // that depth, because the same object may later be reached by a shorter path.
            if (depth >= 5)
            {
                return true;
            }

            // Jotunn's depth limit makes a simple "seen" set subtly incorrect: an object
            // first reached at depth 4 has less traversal budget than the same object later
            // reached at depth 2. Skip only if we already processed it at an equal or lower
            // depth (which means equal or greater remaining traversal reach).
            if (bestVisitedDepth.TryGetValue(objectToFix, out var previousDepth) &&
                previousDepth <= depth)
            {
                return false;
            }

            bestVisitedDepth[objectToFix] = depth;
            return true;
        }

        private static void FixReferencesPostfix(bool __state)
        {
            if (!__state)
            {
                return;
            }

            // Do not retain arbitrary mod object graphs after one top-level traversal.
            bestVisitedDepth = null;
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
