using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jotunn.Managers
{
    internal static class MockResolver
    {
        private const string Prefix = "JVLmock_";
        private const string LegacyPrefix = "VLmock_";
        private const string Separator = "__";
        private const int MaxDepth = 5;

        private static readonly MethodInfo ObjectIsPersistent =
            AccessTools.Method(typeof(Object), "IsPersistent");

        private static readonly Dictionary<Type, MemberInfo[]> MemberCache =
            new Dictionary<Type, MemberInfo[]>();

        internal static void FixObject(object value)
        {
            if (value == null)
            {
                return;
            }

            var visited = new Dictionary<object, int>(ReferenceComparer.Instance);
            FixObjectGraph(value, 0, visited);
        }

        internal static void FixGameObject(GameObject gameObject, bool recursive)
        {
            if (!gameObject)
            {
                return;
            }

            var visited = new Dictionary<object, int>(ReferenceComparer.Instance);
            FixGameObjectInternal(gameObject, recursive, visited);
        }

        private static void FixGameObjectInternal(
            GameObject gameObject,
            bool recursive,
            Dictionary<object, int> visited)
        {
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (!component || component is Transform)
                {
                    continue;
                }

                FixObjectGraph(component, 0, visited);
            }

            if (!recursive)
            {
                return;
            }

            var replacements = new List<Tuple<Transform, GameObject>>();

            foreach (Transform child in gameObject.transform)
            {
                var realPrefab = ResolveMock(child.gameObject, typeof(GameObject)) as GameObject;
                if (realPrefab)
                {
                    replacements.Add(Tuple.Create(child, realPrefab));
                }
                else
                {
                    FixGameObjectInternal(child.gameObject, true, visited);
                }
            }

            foreach (var replacement in replacements)
            {
                ReplaceMockChild(replacement.Item1, replacement.Item2, gameObject);
            }
        }

        private static void FixObjectGraph(
            object value,
            int depth,
            Dictionary<object, int> visited)
        {
            if (value == null || depth >= MaxDepth)
            {
                return;
            }

            if (visited.TryGetValue(value, out var previousDepth) && previousDepth <= depth)
            {
                return;
            }
            visited[value] = depth;

            if (value is Material directMaterial)
            {
                FixMaterial(directMaterial);
                return;
            }

            if (value is DropTable directDropTable)
            {
                FixDropTable(directDropTable);
                return;
            }

            foreach (var member in GetMembers(value.GetType()))
            {
                Type memberType = GetMemberType(member);
                if (memberType == null || memberType == typeof(string))
                {
                    continue;
                }

                object current;
                try
                {
                    current = GetValue(member, value);
                }
                catch
                {
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                if (current is DropTable dropTable)
                {
                    FixDropTable(dropTable);
                    continue;
                }

                if (typeof(Object).IsAssignableFrom(memberType))
                {
                    var unityObject = current as Object;
                    if (!unityObject)
                    {
                        continue;
                    }

                    var resolved = ResolveMock(unityObject, memberType);
                    if (resolved)
                    {
                        TrySetValue(member, value, resolved);
                    }
                    else if (unityObject is Material material)
                    {
                        FixMaterial(material);
                    }

                    continue;
                }

                if (TryGetCollectionElement(memberType, out var elementType, out var kind))
                {
                    if (typeof(Object).IsAssignableFrom(elementType))
                    {
                        FixUnityObjectCollection(member, value, current, elementType, kind);
                    }
                    else if (elementType.IsClass && current is IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            FixObjectGraph(item, depth + 1, visited);
                        }
                    }

                    continue;
                }

                if (memberType.IsGenericType &&
                    memberType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    continue;
                }

                if (memberType.IsClass)
                {
                    FixObjectGraph(current, depth + 1, visited);
                }
            }
        }

        private static void FixUnityObjectCollection(
            MemberInfo member,
            object owner,
            object current,
            Type elementType,
            CollectionKind kind)
        {
            if (!(current is IEnumerable enumerable))
            {
                return;
            }

            var values = new List<Object>();
            bool changed = false;

            foreach (var item in enumerable)
            {
                var unityObject = item as Object;
                if (!unityObject)
                {
                    values.Add(unityObject);
                    continue;
                }

                var resolved = ResolveMock(unityObject, elementType);
                if (resolved)
                {
                    values.Add(resolved);
                    changed = true;
                }
                else
                {
                    values.Add(unityObject);
                    if (unityObject is Material material)
                    {
                        FixMaterial(material);
                    }
                }
            }

            if (!changed || !CanSet(member))
            {
                return;
            }

            object replacement;
            switch (kind)
            {
                case CollectionKind.Array:
                    var array = Array.CreateInstance(elementType, values.Count);
                    for (int i = 0; i < values.Count; i++)
                    {
                        array.SetValue(values[i], i);
                    }
                    replacement = array;
                    break;

                case CollectionKind.List:
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = (IList)Activator.CreateInstance(listType);
                    foreach (var item in values)
                    {
                        list.Add(item);
                    }
                    replacement = list;
                    break;

                case CollectionKind.HashSet:
                    var hashType = typeof(HashSet<>).MakeGenericType(elementType);
                    var hash = Activator.CreateInstance(hashType);
                    var add = hashType.GetMethod("Add", new[] { elementType });
                    foreach (var item in values)
                    {
                        add?.Invoke(hash, new object[] { item });
                    }
                    replacement = hash;
                    break;

                default:
                    return;
            }

            TrySetValue(member, owner, replacement);
        }

        private static void FixDropTable(DropTable dropTable)
        {
            if (dropTable?.m_drops == null)
            {
                return;
            }

            for (int i = 0; i < dropTable.m_drops.Count; i++)
            {
                var drop = dropTable.m_drops[i];
                var resolved = ResolveMock(drop.m_item, typeof(GameObject)) as GameObject;
                if (resolved)
                {
                    drop.m_item = resolved;
                    dropTable.m_drops[i] = drop;
                }
            }
        }

        private static void FixMaterial(Material material)
        {
            if (!material)
            {
                return;
            }

            try
            {
                foreach (int propertyId in material.GetTexturePropertyNameIDs())
                {
                    var texture = material.GetTexture(propertyId);
                    if (!texture)
                    {
                        continue;
                    }

                    var realTexture = ResolveMock(texture, typeof(Texture)) as Texture;
                    if (realTexture)
                    {
                        material.SetTexture(propertyId, realTexture);
                    }
                }
            }
            catch
            {
                // Some stripped/headless materials do not expose texture properties.
            }

            try
            {
                var shader = material.shader;
                var realShader = ResolveMock(shader, typeof(Shader)) as Shader;
                if (realShader)
                {
                    material.shader = realShader;
                }
            }
            catch
            {
                // Headless/server material data may not have a usable shader.
            }
        }

        private static Object ResolveMock(Object unityObject, Type expectedType)
        {
            if (!unityObject || string.IsNullOrEmpty(unityObject.name))
            {
                return null;
            }

            var cleanName = GetCleanedName(expectedType, unityObject.name);
            if (!TryParseMockName(cleanName, out var assetName, out var childPath))
            {
                return null;
            }

            if (childPath.Count == 0)
            {
                var direct = PrefabManager.Cache.GetPrefab(expectedType, assetName);
                if (direct)
                {
                    return direct;
                }
            }

            var prefab = PrefabManager.Instance.GetPrefab(assetName);
            if (!prefab)
            {
                return null;
            }

            GameObject current = prefab;
            foreach (var childName in childPath)
            {
                var child = current.transform.Find(childName);
                if (!child)
                {
                    return null;
                }
                current = child.gameObject;
            }

            if (expectedType == typeof(GameObject) ||
                expectedType.IsAssignableFrom(typeof(GameObject)))
            {
                return current;
            }

            if (typeof(Component).IsAssignableFrom(expectedType))
            {
                var component = current.GetComponent(expectedType);
                if (component)
                {
                    return component;
                }

                component = current.GetComponentInChildren(expectedType, true);
                if (component)
                {
                    return component;
                }
            }

            return FindReferencedAsset(current, expectedType);
        }

        private static Object FindReferencedAsset(GameObject gameObject, Type expectedType)
        {
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (!component)
                {
                    continue;
                }

                if (expectedType.IsAssignableFrom(component.GetType()))
                {
                    return component;
                }

                foreach (var member in GetMembers(component.GetType()))
                {
                    var memberType = GetMemberType(member);
                    if (memberType == null || !expectedType.IsAssignableFrom(memberType))
                    {
                        continue;
                    }

                    try
                    {
                        if (GetValue(member, component) is Object value && value)
                        {
                            return value;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static void ReplaceMockChild(
            Transform child,
            GameObject realPrefab,
            GameObject parent)
        {
            if (!child || !realPrefab || !parent || IsPersistent(parent))
            {
                return;
            }

            var replacement = Object.Instantiate(realPrefab, parent.transform);
            replacement.name = realPrefab.name;
            replacement.SetActive(child.gameObject.activeSelf);
            replacement.transform.position = child.position;
            replacement.transform.rotation = child.rotation;
            replacement.transform.localScale = child.localScale;

            int siblingIndex = child.GetSiblingIndex();
            Object.DestroyImmediate(child.gameObject);
            replacement.transform.SetSiblingIndex(siblingIndex);
        }

        private static bool IsPersistent(Object value)
        {
            if (!value || ObjectIsPersistent == null)
            {
                return false;
            }

            try
            {
                return (bool)ObjectIsPersistent.Invoke(null, new object[] { value });
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseMockName(
            string name,
            out string assetName,
            out List<string> childPath)
        {
            name = (name ?? string.Empty).Trim();

            string target;
            if (name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                target = name.Substring(Prefix.Length);
            }
            else if (name.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            {
                target = name.Substring(LegacyPrefix.Length);
            }
            else
            {
                assetName = name;
                childPath = new List<string>();
                return false;
            }

            var parts = target
                .Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length != 0)
                .ToArray();

            assetName = parts.Length > 0 ? parts[0] : string.Empty;
            childPath = parts.Skip(1).ToList();
            return assetName.Length != 0;
        }

        private static string GetCleanedName(Type objectType, string name)
        {
            if (objectType == typeof(Material) && name.EndsWith("(Instance)", StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - "(Instance)".Length);
            }

            if (objectType == typeof(Mesh) && name.EndsWith("Instance", StringComparison.Ordinal))
            {
                return name.Substring(0, name.Length - "Instance".Length);
            }

            return name.Trim();
        }

        private static MemberInfo[] GetMembers(Type type)
        {
            lock (MemberCache)
            {
                if (MemberCache.TryGetValue(type, out var cached))
                {
                    return cached;
                }

                const BindingFlags flags =
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly;

                var members = new List<MemberInfo>();
                for (var current = type; current != null; current = current.BaseType)
                {
                    members.AddRange(current.GetFields(flags)
                        .Where(field =>
                            !field.IsStatic &&
                            !field.IsLiteral &&
                            field.GetCustomAttribute<NonSerializedAttribute>() == null));

                    members.AddRange(current.GetProperties(flags)
                        .Where(property =>
                            property.GetIndexParameters().Length == 0 &&
                            property.GetGetMethod(true) != null));
                }

                cached = members.ToArray();
                MemberCache[type] = cached;
                return cached;
            }
        }

        private static Type GetMemberType(MemberInfo member)
        {
            if (member is FieldInfo field)
            {
                return field.FieldType;
            }

            if (member is PropertyInfo property)
            {
                return property.PropertyType;
            }

            return null;
        }

        private static object GetValue(MemberInfo member, object owner)
        {
            if (member is FieldInfo field)
            {
                return field.GetValue(owner);
            }

            if (member is PropertyInfo property)
            {
                return property.GetValue(owner, null);
            }

            return null;
        }

        private static bool CanSet(MemberInfo member)
        {
            if (member is FieldInfo field)
            {
                return !field.IsLiteral;
            }

            if (member is PropertyInfo property)
            {
                return property.GetSetMethod(true) != null;
            }

            return false;
        }

        private static void TrySetValue(MemberInfo member, object owner, object value)
        {
            if (!CanSet(member))
            {
                return;
            }

            try
            {
                if (member is FieldInfo field)
                {
                    field.SetValue(owner, value);
                }
                else if (member is PropertyInfo property)
                {
                    property.SetValue(owner, value, null);
                }
            }
            catch
            {
                // Inaccessible/readonly runtime members are simply left unchanged.
            }
        }

        private static bool TryGetCollectionElement(
            Type type,
            out Type elementType,
            out CollectionKind kind)
        {
            if (type.IsArray)
            {
                elementType = type.GetElementType();
                kind = CollectionKind.Array;
                return elementType != null;
            }

            if (type.IsGenericType)
            {
                var generic = type.GetGenericTypeDefinition();
                if (generic == typeof(List<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    kind = CollectionKind.List;
                    return true;
                }

                if (generic == typeof(HashSet<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    kind = CollectionKind.HashSet;
                    return true;
                }
            }

            elementType = null;
            kind = CollectionKind.None;
            return false;
        }

        private enum CollectionKind
        {
            None,
            Array,
            List,
            HashSet
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
