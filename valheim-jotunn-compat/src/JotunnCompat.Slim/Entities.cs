using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using Jotunn.Configs;
using Jotunn.Managers;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jotunn.Entities
{
    public class CustomPrefab
    {
        public GameObject Prefab { get; }
        public bool FixReference { get; set; }

        public CustomPrefab(GameObject prefab, bool fixReference)
        {
            Prefab = prefab;
            FixReference = fixReference;
        }

        public CustomPrefab(AssetBundle bundle, string assetName, bool fixReference)
            : this(bundle != null ? bundle.LoadAsset<GameObject>(assetName) : null, fixReference) { }

        public bool IsValid() => Prefab;
        public static bool IsCustomPrefab(string prefabName) => PrefabManager.Instance.Contains(prefabName);
        public override string ToString() => Prefab ? Prefab.name : "<invalid prefab>";
    }

    public class CustomItem
    {
        public GameObject ItemPrefab { get; }
        public ItemDrop ItemDrop { get; }
        public bool FixReference { get; set; }

        public CustomItem(GameObject itemPrefab, bool fixReference)
        {
            ItemPrefab = itemPrefab;
            ItemDrop = itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
            FixReference = fixReference;
        }

        public CustomItem(string name, string basePrefabName, ItemConfig itemConfig)
            : this(PrefabManager.Instance.CreateClonedPrefab(name, basePrefabName), false) { }

        public CustomItem(AssetBundle bundle, string assetName, bool fixReference, ItemConfig itemConfig)
            : this(bundle != null ? bundle.LoadAsset<GameObject>(assetName) : null, fixReference) { }

        internal bool IsValid() => ItemPrefab && ItemDrop;
    }

    public class CustomStatusEffect
    {
        public StatusEffect StatusEffect { get; }
        public bool FixReference { get; set; }
        public CustomStatusEffect(StatusEffect statusEffect, bool fixReference)
        {
            StatusEffect = statusEffect;
            FixReference = fixReference;
        }
        internal bool IsValid() => StatusEffect != null;
    }

    public class CustomLocation
    {
        public GameObject Prefab { get; private set; }
        public ZoneSystem.ZoneLocation ZoneLocation { get; }
        public string Name { get; }
        public bool FixReference { get; set; }
        public bool SoftReference { get; }

        public CustomLocation(GameObject exteriorPrefab, bool fixReference, LocationConfig config)
        {
            Prefab = exteriorPrefab;
            Name = exteriorPrefab ? exteriorPrefab.name : string.Empty;
            FixReference = fixReference;
            ZoneLocation = config.GetZoneLocation();
            ZoneLocation.m_prefabName = Name;
            if (exteriorPrefab)
            {
                var id = AssetManager.Instance.AddAsset(exteriorPrefab);
                ZoneLocation.m_prefab = new SoftReference<GameObject>(id);
            }
        }

        public CustomLocation(SoftReference<GameObject> softReferencePrefab, bool fixReference, LocationConfig config)
        {
            Name = softReferencePrefab.Name;
            FixReference = fixReference;
            SoftReference = true;
            ZoneLocation = config.GetZoneLocation();
            ZoneLocation.m_prefab = softReferencePrefab;
            ZoneLocation.m_prefabName = Name;
            AssetManager.Instance.ResolveMocksOnLoad(softReferencePrefab, ZoneManager.Instance.LocationContainer.transform, go =>
            {
                Prefab = go;
                if (go) go.SetActive(true);
            });
        }
    }

    public class CustomRoom
    {
        public GameObject Prefab { get; private set; }
        public string Name { get; }
        public string ThemeName { get; }
        public bool FixReference { get; set; }
        public bool SoftReference { get; }
        public DungeonDB.RoomData RoomData { get; }

        public CustomRoom(SoftReference<GameObject> softReferencePrefab, bool fixReference, RoomConfig config)
        {
            Name = softReferencePrefab.Name;
            ThemeName = config.ThemeName;
            FixReference = fixReference;
            SoftReference = true;
            AssetManager.Instance.ResolveMocksOnLoad(softReferencePrefab, DungeonManager.Instance.DungeonRoomContainer.transform, go => Prefab = go);
            RoomData = new DungeonDB.RoomData
            {
                m_prefab = softReferencePrefab,
                m_enabled = config.Enabled ?? true,
                m_theme = GetRoomTheme(ThemeName)
            };
        }

        public static bool IsVanillaTheme(string themeName) => Enum.TryParse(themeName, false, out Room.Theme _);
        internal static Room.Theme GetRoomTheme(string themeName) =>
            Enum.TryParse(themeName, false, out Room.Theme theme) ? theme : Room.Theme.None;
    }

    public class CustomLocalization
    {
        private static readonly MethodInfo AddWordMethod = typeof(Localization).GetMethod(
            "AddWord",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(string), typeof(string) },
            null);

        private readonly Dictionary<string, Dictionary<string, string>> map = new Dictionary<string, Dictionary<string, string>>();

        private static void AddWord(string key, string value)
        {
            if (Localization.instance == null || AddWordMethod == null) return;
            AddWordMethod.Invoke(Localization.instance, new object[] { key, value });
        }

        public void AddYamlFile(string language, string fileContent)
        {
            if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(fileContent)) return;
            Dictionary<string, string> parsed;
            try
            {
                parsed = new YamlDotNet.Serialization.DeserializerBuilder().Build()
                    .Deserialize<Dictionary<string, string>>(fileContent);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not parse localization YAML: " + ex.Message);
                return;
            }
            if (parsed == null) return;
            if (!map.TryGetValue(language, out var lang))
                map[language] = lang = new Dictionary<string, string>();
            foreach (var kv in parsed)
            {
                var key = kv.Key.TrimStart('$');
                lang[key] = kv.Value;
                AddWord(key, kv.Value);
            }
        }

        internal void ApplyCurrent()
        {
            if (Localization.instance == null) return;
            var language = Localization.instance.GetSelectedLanguage();
            if (!map.TryGetValue(language, out var words) && !map.TryGetValue("English", out words)) return;
            foreach (var kv in words) AddWord(kv.Key, kv.Value);
        }
    }

    public class CustomRPC
    {
        private const byte JotunnPackage = 1;
        internal readonly string ID;
        internal readonly NetworkManager.CoroutineHandler ServerReceive;
        internal readonly NetworkManager.CoroutineHandler ClientReceive;

        internal CustomRPC(string id, NetworkManager.CoroutineHandler server, NetworkManager.CoroutineHandler client)
        {
            ID = id;
            ServerReceive = server;
            ClientReceive = client;
        }

        public void SendPackage(long target, ZPackage package)
        {
            if (ZRoutedRpc.instance == null || package == null) return;
            var wrapped = new ZPackage();
            wrapped.Write(JotunnPackage);
            wrapped.Write(package.GetArray());
            wrapped.SetPos(0);
            ZRoutedRpc.instance.InvokeRoutedRPC(target, ID, wrapped);
        }

        internal void Receive(long sender, ZPackage package)
        {
            if (package == null || package.Size() <= 0 || ZNet.instance == null) return;

            var flags = package.ReadByte();
            if ((flags & JotunnPackage) != JotunnPackage) return;

            var payload = new ZPackage(package.ReadByteArray());
            var handler = ZNet.instance.IsServer() ? ServerReceive : ClientReceive;
            if (handler != null) ZNet.instance.StartCoroutine(handler(sender, payload));
        }
    }
}
