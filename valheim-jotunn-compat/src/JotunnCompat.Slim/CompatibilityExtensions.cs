using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ConfigurationManagerAttributes
{
    public bool? ShowRangeAsPercent;
    public Action<ConfigEntryBase> CustomDrawer;
    public bool? Browsable;
    public string Category;
    public object DefaultValue;
    public bool? HideDefaultButton;
    public bool? HideSettingName;
    public string Description;
    public string DispName;
    public int? Order;
    public bool? ReadOnly;
    public bool? IsAdvanced;
    public Func<object, string> ObjToStr;
    public Func<string, object> StrToObj;
    public bool IsAdminOnly { get; set; }
}

namespace Jotunn.Extensions
{
    public static class ConfigFileExtensions
    {
        private static readonly Dictionary<string, int> sectionOrder = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> settingOrder = new Dictionary<string, int>();

        public static ConfigEntry<T> BindConfig<T>(this ConfigFile configFile, string section, string key, T defaultValue,
            string description, bool synced = true, int? order = null, AcceptableValueBase acceptableValues = null,
            Action<ConfigEntryBase> customDrawer = null, ConfigurationManagerAttributes configAttributes = null)
        {
            var attrs = configAttributes ?? new ConfigurationManagerAttributes();
            attrs.IsAdminOnly = synced;
            attrs.Order = order;
            attrs.CustomDrawer = customDrawer;
            var suffix = synced ? " [Synced with Server]" : " [Not Synced with Server]";
            return configFile.Bind(section, key, defaultValue, new ConfigDescription(description + suffix, acceptableValues, attrs));
        }

        public static ConfigEntry<T> BindConfigInOrder<T>(this ConfigFile configFile, string section, string key, T defaultValue,
            string description, bool synced = true, bool sectionOrdering = true, bool settingOrdering = true,
            AcceptableValueBase acceptableValues = null, Action<ConfigEntryBase> customDrawer = null,
            ConfigurationManagerAttributes configAttributes = null)
        {
            var file = configFile.ConfigFilePath ?? string.Empty;
            var skey = file + "\n" + section;
            if (sectionOrdering)
            {
                if (!sectionOrder.TryGetValue(skey, out var n)) sectionOrder[skey] = n = sectionOrder.Count + 1;
                section = n + " - " + section;
            }
            int? order = null;
            if (settingOrdering)
            {
                var key2 = file + "\n" + section;
                if (!settingOrder.TryGetValue(key2, out var n)) n = 0;
                order = n;
                settingOrder[key2] = n - 1;
            }
            return configFile.BindConfig(section, key, defaultValue, description, synced, order, acceptableValues, customDrawer, configAttributes);
        }
    }
}

namespace Jotunn
{
    public static class ExposedGameObjectExtension
    {
        public static Transform FindDeepChild(this GameObject gameObject, string childName,
            global::Utils.IterativeSearchType searchType = global::Utils.IterativeSearchType.BreadthFirst)
        {
            return gameObject ? gameObject.transform.FindDeepChild(childName, searchType) : null;
        }

        private static Transform FindDeepChild(this Transform root, string name, global::Utils.IterativeSearchType searchType)
        {
            if (!root) return null;
            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current != root && current.name == name) return current;
                foreach (Transform child in current) queue.Enqueue(child);
            }
            return null;
        }
    }

    public static class PrefabExtension
    {
        private const string Prefix = "JVLmock_";

        public static void FixReferences(this GameObject gameObject, bool recursive)
        {
            if (!gameObject) return;
            FixObject(gameObject);
            if (!recursive) return;
            foreach (Transform child in gameObject.transform)
            {
                var target = ResolveMock(child.gameObject, typeof(GameObject)) as GameObject;
                if (target)
                {
                    var replacement = Object.Instantiate(target, gameObject.transform);
                    replacement.name = target.name;
                    replacement.transform.SetSiblingIndex(child.GetSiblingIndex());
                    replacement.transform.localPosition = child.localPosition;
                    replacement.transform.localRotation = child.localRotation;
                    replacement.transform.localScale = child.localScale;
                    Object.DestroyImmediate(child.gameObject);
                }
                else child.gameObject.FixReferences(true);
            }
        }

        private static void FixObject(GameObject go)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (!component || component is Transform) continue;
                FixFields(component, 0, new HashSet<object>(ReferenceComparer.Instance));
            }
        }

        private static void FixFields(object obj, int depth, HashSet<object> visited)
        {
            if (obj == null || depth >= 5 || !visited.Add(obj)) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in obj.GetType().GetFields(flags))
            {
                if (field.IsStatic || field.IsLiteral || field.IsInitOnly) continue;
                object value;
                try { value = field.GetValue(obj); } catch { continue; }
                if (value is Object unity)
                {
                    var resolved = ResolveMock(unity, field.FieldType);
                    if (resolved != null) try { field.SetValue(obj, resolved); } catch { }
                }
                else if (value != null && !(value is string) && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                {
                    FixFields(value, depth + 1, visited);
                }
            }
        }

        private static Object ResolveMock(Object obj, Type expected)
        {
            if (!obj || string.IsNullOrEmpty(obj.name) || !obj.name.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            var spec = obj.name.Substring(Prefix.Length);
            var parts = spec.Split(new[] { "__" }, StringSplitOptions.RemoveEmptyEntries);
            var prefab = PrefabManager.Instance.GetPrefab(parts[0]);
            if (!prefab) return null;
            GameObject current = prefab;
            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.transform.Find(parts[i]);
                if (!child) return null;
                current = child.gameObject;
            }
            if (expected == typeof(GameObject) || expected.IsAssignableFrom(typeof(GameObject))) return current;
            if (typeof(Component).IsAssignableFrom(expected)) return current.GetComponent(expected);
            return null;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
