using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Managers;
using UnityEngine;

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
        private static readonly Dictionary<string, int> sectionOrder =
            new Dictionary<string, int>();
        private static readonly Dictionary<string, int> settingOrder =
            new Dictionary<string, int>();

        public static ConfigEntry<T> BindConfig<T>(
            this ConfigFile configFile,
            string section,
            string key,
            T defaultValue,
            string description,
            bool synced = true,
            int? order = null,
            AcceptableValueBase acceptableValues = null,
            Action<ConfigEntryBase> customDrawer = null,
            ConfigurationManagerAttributes configAttributes = null)
        {
            var attrs = configAttributes ?? new ConfigurationManagerAttributes();
            attrs.IsAdminOnly = synced;
            attrs.Order = order;
            attrs.CustomDrawer = customDrawer;
            var suffix = synced
                ? " [Synced with Server]"
                : " [Not Synced with Server]";

            return configFile.Bind(
                section,
                key,
                defaultValue,
                new ConfigDescription(
                    description + suffix,
                    acceptableValues,
                    attrs));
        }

        public static ConfigEntry<T> BindConfigInOrder<T>(
            this ConfigFile configFile,
            string section,
            string key,
            T defaultValue,
            string description,
            bool synced = true,
            bool sectionOrdering = true,
            bool settingOrdering = true,
            AcceptableValueBase acceptableValues = null,
            Action<ConfigEntryBase> customDrawer = null,
            ConfigurationManagerAttributes configAttributes = null)
        {
            var file = configFile.ConfigFilePath ?? string.Empty;
            var skey = file + "\n" + section;

            if (sectionOrdering)
            {
                if (!sectionOrder.TryGetValue(skey, out var n))
                {
                    sectionOrder[skey] = n = sectionOrder.Count + 1;
                }
                section = n + " - " + section;
            }

            int? order = null;
            if (settingOrdering)
            {
                var key2 = file + "\n" + section;
                if (!settingOrder.TryGetValue(key2, out var n))
                {
                    n = 0;
                }

                order = n;
                settingOrder[key2] = n - 1;
            }

            return configFile.BindConfig(
                section,
                key,
                defaultValue,
                description,
                synced,
                order,
                acceptableValues,
                customDrawer,
                configAttributes);
        }
    }
}

namespace Jotunn
{
    public static class ExposedGameObjectExtension
    {
        public static Transform FindDeepChild(
            this GameObject gameObject,
            string childName,
            global::Utils.IterativeSearchType searchType =
                global::Utils.IterativeSearchType.BreadthFirst)
        {
            return gameObject
                ? gameObject.transform.FindDeepChild(childName, searchType)
                : null;
        }

        private static Transform FindDeepChild(
            this Transform root,
            string name,
            global::Utils.IterativeSearchType searchType)
        {
            if (!root)
            {
                return null;
            }

            var queue = new Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current != root && current.name == name)
                {
                    return current;
                }

                foreach (Transform child in current)
                {
                    queue.Enqueue(child);
                }
            }

            return null;
        }
    }

    public static class PrefabExtension
    {
        public static void FixReferences(this object objectToFix)
        {
            MockResolver.FixObject(objectToFix);
        }

        public static void FixReferences(this GameObject gameObject)
        {
            MockResolver.FixGameObject(gameObject, false);
        }

        public static void FixReferences(this GameObject gameObject, bool recursive)
        {
            MockResolver.FixGameObject(gameObject, recursive);
        }
    }
}
