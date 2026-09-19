using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jotunn
{
    [BepInPlugin(ModGuid, ModName, Version)]
    public sealed class Main : BaseUnityPlugin
    {
        public const string Version = "2.30.1";
        public const string ModName = "MWL Jotunn Compatibility";
        public const string ModGuid = "com.jotunn.jotunn";

        internal static Main Instance;
        internal static Harmony Harmony;
        internal static GameObject RootObject;

        private void Awake()
        {
            Instance = this;
            Logger.Source = base.Logger;
            RootObject = new GameObject("_MWLJotunnCompat");
            DontDestroyOnLoad(RootObject);
            RootObject.SetActive(false);

            Harmony = new Harmony(ModGuid);
            Harmony.PatchAll(Assembly.GetExecutingAssembly());

            try { Runtime.MakeAllAssetsLoadable(); }
            catch (Exception ex) { Logger.LogDebug("Runtime.MakeAllAssetsLoadable unavailable: " + ex.Message); }

            Game.isModded = true;
            Logger.LogInfo("Loaded lightweight Jotunn compatibility layer for More World Locations");
        }
    }

    public static class Logger
    {
        internal static ManualLogSource Source;
        private static ManualLogSource Log => Source ?? (Source = BepInEx.Logging.Logger.CreateLogSource("MWL-JotunnCompat"));
        public static void LogDebug(object data) => Log.LogDebug(data);
        public static void LogInfo(object data) => Log.LogInfo(data);
        public static void LogWarning(object data) => Log.LogWarning(data);
        public static void LogError(object data) => Log.LogError(data);
        public static void LogDebug(BepInPlugin _, object data) => LogDebug(data);
        public static void LogInfo(BepInPlugin _, object data) => LogInfo(data);
        public static void LogWarning(BepInPlugin _, object data) => LogWarning(data);
        public static void LogError(BepInPlugin _, object data) => LogError(data);
    }

    internal static class Compat
    {
        internal static BepInPlugin SourceMod(Assembly asm)
        {
            try
            {
                foreach (var pi in Chainloader.PluginInfos.Values)
                    if (pi.Instance != null && pi.Instance.GetType().Assembly == asm)
                        return pi.Metadata;
            }
            catch { }

            return Main.Instance != null
                ? Main.Instance.Info.Metadata
                : new BepInPlugin("mwl.jotunncompat.unknown", "MWL Jotunn Compat", Main.Version);
        }

        internal static int StableHash(string str)
        {
            if (str == null) return 0;
            unchecked
            {
                int h1 = 5381, h2 = h1;
                for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
                {
                    h1 = ((h1 << 5) + h1) ^ str[i];
                    if (i == str.Length - 1 || str[i + 1] == '\0') break;
                    h2 = ((h2 << 5) + h2) ^ str[i + 1];
                }
                return h1 + h2 * 1566083941;
            }
        }
    }
}

namespace Jotunn.Entities
{
    public abstract class CustomEntity
    {
        public BepInPlugin SourceMod { get; }
        protected CustomEntity(Assembly assembly) { SourceMod = Jotunn.Compat.SourceMod(assembly); }
        protected CustomEntity(BepInPlugin sourceMod) { SourceMod = sourceMod ?? Jotunn.Compat.SourceMod(Assembly.GetCallingAssembly()); }
    }
}

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
    public Color EntryColor { get; set; } = new Color(1f, .631f, .235f, 1f);
    public Color DescriptionColor { get; set; } = Color.white;
    public bool IsUnlocked { get; internal set; } = true;
}

namespace Jotunn.Extensions
{
    public static class ConfigFileExtensions
    {
        private sealed class Ordering
        {
            public readonly Dictionary<string,int> Sections = new Dictionary<string,int>();
            public readonly Dictionary<string,int> Settings = new Dictionary<string,int>();
        }
        private static readonly Dictionary<string,Ordering> Maps = new Dictionary<string,Ordering>();

        private static Ordering Get(ConfigFile f)
        {
            if (!Maps.TryGetValue(f.ConfigFilePath, out var m)) Maps[f.ConfigFilePath] = m = new Ordering();
            return m;
        }

        public static ConfigEntry<T> BindConfigInOrder<T>(this ConfigFile configFile, string section, string key, T defaultValue,
            string description, bool synced = true, bool sectionOrder = true, bool settingOrder = true,
            AcceptableValueBase acceptableValues = null, Action<ConfigEntryBase> customDrawer = null,
            ConfigurationManagerAttributes configAttributes = null)
        {
            var map = Get(configFile);
            if (sectionOrder)
            {
                if (!map.Sections.TryGetValue(section, out var n)) map.Sections[section] = n = map.Sections.Count + 1;
                section = n + " - " + section;
            }
            int? order = null;
            if (settingOrder)
            {
                if (!map.Settings.TryGetValue(section, out var n)) n = 0;
                map.Settings[section] = n - 1;
                order = n;
            }
            return BindConfig(configFile, section, key, defaultValue, description, synced, order, acceptableValues, customDrawer, configAttributes);
        }

        public static ConfigEntry<T> BindConfig<T>(this ConfigFile configFile, string section, string key, T defaultValue,
            string description, bool synced = true, int? order = null, AcceptableValueBase acceptableValues = null,
            Action<ConfigEntryBase> customDrawer = null, ConfigurationManagerAttributes configAttributes = null)
        {
            configAttributes = configAttributes ?? new ConfigurationManagerAttributes();
            configAttributes.IsAdminOnly = synced;
            configAttributes.Order = order;
            configAttributes.CustomDrawer = customDrawer;
            return configFile.Bind(section, key, defaultValue,
                new ConfigDescription(description + (synced ? " [Synced with Server]" : " [Not Synced with Server]"),
                    acceptableValues, configAttributes));
        }
    }

    public static class MockExtensions
    {
        public static void FixReferences(this object obj) => Jotunn.Managers.MockResolver.Fix(obj, false);
        public static void FixReferences(this GameObject obj, bool recursive) => Jotunn.Managers.MockResolver.Fix(obj, recursive);
        public static void FixReferences(this Object obj) => Jotunn.Managers.MockResolver.Fix(obj, false);
    }
}

namespace Jotunn.Utils
{
    public static class AssetUtils
    {
        public static string LoadTextFromResources(string resourceName) =>
            LoadTextFromResources(resourceName, Assembly.GetCallingAssembly());

        public static string LoadTextFromResources(string resourceName, Assembly assembly)
        {
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    var actual = assembly.GetManifestResourceNames().FirstOrDefault(n =>
                        n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
                    if (actual != null) return LoadTextFromResources(actual, assembly);
                    throw new FileNotFoundException("Embedded resource not found: " + resourceName);
                }
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        public static AssetBundle LoadAssetBundleFromResources(string resourceName) =>
            LoadAssetBundleFromResources(resourceName, Assembly.GetCallingAssembly());

        public static AssetBundle LoadAssetBundleFromResources(string resourceName, Assembly assembly)
        {
            var name = assembly.GetManifestResourceNames().FirstOrDefault(n =>
                n.Equals(resourceName, StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase));
            if (name == null) throw new FileNotFoundException("Embedded asset bundle not found: " + resourceName);
            using (var stream = assembly.GetManifestResourceStream(name))
            {
                var bundle = AssetBundle.LoadFromStream(stream);
                if (bundle == null) throw new InvalidDataException("Could not load embedded asset bundle " + resourceName);
                return bundle;
            }
        }
    }
}
