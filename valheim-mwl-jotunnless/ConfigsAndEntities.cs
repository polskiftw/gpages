using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using Jotunn.Managers;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jotunn.Configs
{
    public class ItemConfig
    {
        public void Apply(GameObject prefab) { }
    }

    public class LocationConfig
    {
        public Heightmap.Biome Biome { get; set; }
        public Heightmap.BiomeArea BiomeArea { get; set; } = Heightmap.BiomeArea.Everything;
        public bool Priotized { get; set; }
        public int Quantity { get; set; }
        public float ChanceToSpawn { get; set; } = 10f;
        public float ExteriorRadius { get; set; } = 10f;
        public bool CenterFirst { get; set; }
        public bool InForest { get; set; }
        public float ForestTresholdMin { get; set; }
        public float ForestTrasholdMax { get; set; } = 1f;
        public bool Unique { get; set; }
        public float MinAltitude { get; set; } = -1000f;
        public float MaxAltitude { get; set; } = 1000f;
        public float MinDistance { get; set; }
        public float MaxDistance { get; set; }
        public string Group { get; set; } = string.Empty;
        public float MinDistanceFromSimilar { get; set; }
        public float MinTerrainDelta { get; set; }
        public float MaxTerrainDelta { get; set; } = 2f;
        public bool SlopeRotation { get; set; }
        public bool HasInterior { get; set; }
        public float InteriorRadius { get; set; }
        public string InteriorEnvironment { get; set; }
        public bool RandomRotation { get; set; } = true;
        public bool SnapToWater { get; set; }
        public bool IconPlaced { get; set; }
        public bool IconAlways { get; set; }
        public bool ClearArea { get; set; }

        public LocationConfig() { }

        public ZoneSystem.ZoneLocation GetZoneLocation()
        {
            return new ZoneSystem.ZoneLocation
            {
                m_biome = Biome,
                m_biomeArea = BiomeArea,
                m_quantity = Quantity,
                m_prioritized = Priotized,
                m_interiorRadius = InteriorRadius,
                m_exteriorRadius = ExteriorRadius,
                m_clearArea = ClearArea,
                m_centerFirst = CenterFirst,
                m_forestTresholdMin = ForestTresholdMin,
                m_forestTresholdMax = ForestTrasholdMax,
                m_unique = Unique,
                m_minAltitude = MinAltitude,
                m_maxAltitude = MaxAltitude,
                m_minDistance = MinDistance,
                m_maxDistance = MaxDistance,
                m_group = Group,
                m_inForest = InForest,
                m_minTerrainDelta = MinTerrainDelta,
                m_maxTerrainDelta = MaxTerrainDelta,
                m_minDistanceFromSimilar = MinDistanceFromSimilar,
                m_slopeRotation = SlopeRotation,
                m_randomRotation = RandomRotation,
                m_snapToWater = SnapToWater,
                m_iconPlaced = IconPlaced,
                m_iconAlways = IconAlways
            };
        }
    }

    public class RoomConfig
    {
        public string ThemeName { get; set; }
        public bool? Enabled { get; set; }
        public bool? Entrance { get; set; }
        public bool? Endcap { get; set; }
        public bool? Divider { get; set; }
        public int? EndcapPrio { get; set; }
        public int? MinPlaceOrder { get; set; }
        public float? Weight { get; set; }
        public bool? FaceCenter { get; set; }
        public bool? Perimeter { get; set; }

        public RoomConfig() { }
        public RoomConfig(string themeName) { ThemeName = themeName; }

        public Room Apply(Room room)
        {
            if (Enabled.HasValue) room.m_enabled = Enabled.Value;
            if (Entrance.HasValue) room.m_entrance = Entrance.Value;
            if (Endcap.HasValue) room.m_endCap = Endcap.Value;
            if (Divider.HasValue) room.m_divider = Divider.Value;
            if (EndcapPrio.HasValue) room.m_endCapPrio = EndcapPrio.Value;
            if (MinPlaceOrder.HasValue) room.m_minPlaceOrder = MinPlaceOrder.Value;
            if (Weight.HasValue) room.m_weight = Weight.Value;
            if (FaceCenter.HasValue) room.m_faceCenter = FaceCenter.Value;
            if (Perimeter.HasValue) room.m_perimeter = Perimeter.Value;
            return room;
        }
    }
}

namespace Jotunn.Entities
{
    using Jotunn.Configs;

    public class CustomPrefab : CustomEntity
    {
        public GameObject Prefab { get; }
        public bool FixReference { get; set; }

        public CustomPrefab(GameObject prefab, bool fixReference) : base(Assembly.GetCallingAssembly())
        {
            Prefab = prefab;
            FixReference = fixReference;
        }

        public CustomPrefab(AssetBundle bundle, string assetName, bool fixReference) : base(Assembly.GetCallingAssembly())
        {
            Prefab = bundle.LoadAsset<GameObject>(assetName);
            FixReference = fixReference;
        }

        internal CustomPrefab(GameObject prefab, BepInPlugin source) : base(source) { Prefab = prefab; }
        public static bool IsCustomPrefab(string name) => PrefabManager.Instance.HasPrefab(name);
        public override string ToString() => Prefab ? Prefab.name : "<null>";
    }

    public class CustomItem : CustomEntity
    {
        public GameObject ItemPrefab { get; }
        public ItemDrop ItemDrop { get; }
        public bool FixReference { get; set; }

        public CustomItem(GameObject itemPrefab, bool fixReference) : base(Assembly.GetCallingAssembly())
        {
            ItemPrefab = itemPrefab;
            ItemDrop = itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
            FixReference = fixReference;
        }

        public CustomItem(GameObject itemPrefab, bool fixReference, ItemConfig itemConfig) : this(itemPrefab, fixReference)
        {
            itemConfig?.Apply(ItemPrefab);
        }

        public CustomItem(AssetBundle bundle, string assetName, bool fixReference, ItemConfig itemConfig) : base(Assembly.GetCallingAssembly())
        {
            ItemPrefab = bundle.LoadAsset<GameObject>(assetName);
            ItemDrop = ItemPrefab ? ItemPrefab.GetComponent<ItemDrop>() : null;
            FixReference = fixReference;
            itemConfig?.Apply(ItemPrefab);
        }

        public CustomItem(string name, string basePrefabName, ItemConfig itemConfig) : base(Assembly.GetCallingAssembly())
        {
            ItemPrefab = PrefabManager.Instance.CreateClonedPrefab(name, basePrefabName);
            ItemDrop = ItemPrefab ? ItemPrefab.GetComponent<ItemDrop>() : null;
            itemConfig?.Apply(ItemPrefab);
        }

        public bool IsValid() => ItemPrefab && ItemDrop;
        public override string ToString() => ItemPrefab ? ItemPrefab.name : "<null>";
    }

    public class CustomStatusEffect : CustomEntity
    {
        public StatusEffect StatusEffect { get; }
        public bool FixReference { get; set; }
        public CustomStatusEffect(StatusEffect statusEffect, bool fixReference) : base(Assembly.GetCallingAssembly())
        {
            StatusEffect = statusEffect;
            FixReference = fixReference;
        }
        public bool IsValid() => StatusEffect != null;
        public override string ToString() => StatusEffect ? StatusEffect.name : "<null>";
    }

    public class CustomLocation : CustomEntity
    {
        public GameObject Prefab { get; private set; }
        public ZoneSystem.ZoneLocation ZoneLocation { get; private set; }
        public Location Location { get; private set; }
        public string Name { get; private set; }
        public bool FixReference { get; set; }
        public bool SoftReference { get; set; }

        public CustomLocation(GameObject prefab, bool fixReference, LocationConfig config) : base(Assembly.GetCallingAssembly())
        {
            Prefab = prefab;
            Name = prefab.name;
            Location = prefab.GetComponent<Location>() ?? prefab.AddComponent<Location>();
            Location.m_clearArea = config.ClearArea;
            Location.m_exteriorRadius = config.ExteriorRadius;
            if (config.HasInterior)
            {
                Location.m_hasInterior = true;
                Location.m_interiorRadius = config.InteriorRadius;
                Location.m_interiorEnvironment = config.InteriorEnvironment;
            }
            ZoneLocation = config.GetZoneLocation();
            ZoneLocation.m_prefab = new SoftReference<GameObject>(AssetManager.Instance.AddAsset(prefab));
            ZoneLocation.m_prefabName = prefab.name;
            FixReference = fixReference;
        }

        public CustomLocation(SoftReference<GameObject> softRef, bool fixReference, LocationConfig config) : base(Assembly.GetCallingAssembly())
        {
            Name = softRef.Name;
            ZoneLocation = config.GetZoneLocation();
            ZoneLocation.m_prefab = softRef;
            ZoneLocation.m_prefabName = softRef.Name;
            FixReference = fixReference;
            SoftReference = true;
            AssetManager.Instance.ResolveMocksOnLoad<GameObject>(softRef, ZoneManager.Instance.LocationContainer.transform, go =>
            {
                Prefab = go;
                Location = go ? go.GetComponent<Location>() : null;
            });
        }

        public static bool IsCustomLocation(string name) => ZoneManager.Instance.HasLocation(name);
        public override string ToString() => Name ?? "<null>";
    }

    public class CustomRoom : CustomEntity
    {
        public GameObject Prefab { get; private set; }
        public Room Room { get; private set; }
        public string Name { get; private set; }
        public bool FixReference { get; set; }
        public bool SoftReference { get; set; }
        public string ThemeName { get; set; }
        public DungeonDB.RoomData RoomData { get; private set; }

        public CustomRoom(GameObject prefab, bool fixReference, RoomConfig config) : base(Assembly.GetCallingAssembly())
        {
            Prefab = prefab;
            Name = prefab.name;
            ThemeName = config.ThemeName;
            Room = config.Apply(prefab.GetComponent<Room>() ?? prefab.AddComponent<Room>());
            FixReference = fixReference;
            RoomData = new DungeonDB.RoomData
            {
                m_prefab = new SoftReference<GameObject>(AssetManager.Instance.AddAsset(prefab)),
                m_loadedRoom = Room,
                m_enabled = Room.m_enabled,
                m_theme = GetRoomTheme(ThemeName)
            };
        }

        public CustomRoom(SoftReference<GameObject> softRef, bool fixReference, RoomConfig config) : base(Assembly.GetCallingAssembly())
        {
            Name = softRef.Name;
            ThemeName = config.ThemeName;
            FixReference = fixReference;
            SoftReference = true;
            RoomData = new DungeonDB.RoomData
            {
                m_prefab = softRef,
                m_loadedRoom = null,
                m_enabled = config.Enabled ?? true,
                m_theme = GetRoomTheme(ThemeName)
            };
            AssetManager.Instance.ResolveMocksOnLoad<GameObject>(softRef, DungeonManager.Instance.DungeonRoomContainer.transform, go =>
            {
                Prefab = go;
                Room = go ? go.GetComponent<Room>() : null;
            });
        }

        public static bool IsCustomRoom(string name) => DungeonManager.Instance.HasRoom(name);
        public static bool IsVanillaTheme(string themeName) => Enum.TryParse(themeName, false, out Room.Theme _);
        public static Room.Theme GetRoomTheme(string themeName) =>
            Enum.TryParse(themeName, false, out Room.Theme t) ? t : Room.Theme.None;
        public override string ToString() => Name ?? "<null>";
    }

    public class CustomRPC : CustomEntity
    {
        public string Name { get; }
        internal string ID => SourceMod.GUID + "!" + Name;
        internal NetworkManager.CoroutineHandler ServerReceive;
        internal NetworkManager.CoroutineHandler ClientReceive;

        internal CustomRPC(BepInPlugin source, string name, NetworkManager.CoroutineHandler server, NetworkManager.CoroutineHandler client)
            : base(source)
        {
            Name = name; ServerReceive = server; ClientReceive = client;
        }

        public void SendPackage(long target, ZPackage package)
        {
            if (ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(target, ID, package);
        }

        internal void Receive(long sender, ZPackage package)
        {
            var routine = ZNet.instance != null && ZNet.instance.IsServer() ? ServerReceive : ClientReceive;
            if (routine != null && ZNet.instance != null) ZNet.instance.StartCoroutine(routine(sender, package));
        }

        public override string ToString() => ID;
    }

    public class CustomLocalization : CustomEntity
    {
        private readonly Dictionary<string, Dictionary<string,string>> map =
            new Dictionary<string, Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);

        public CustomLocalization() : base(Assembly.GetCallingAssembly()) { }
        public CustomLocalization(BepInPlugin source) : base(source) { }

        public void AddTranslation(string token, string translation) => AddTranslation("English", token, translation);
        public void AddTranslation(string language, string token, string translation)
        {
            if (!map.TryGetValue(language, out var m)) map[language] = m = new Dictionary<string,string>();
            token = token.TrimStart('$');
            m[token] = translation;
            if (Localization.instance != null) Localization.instance.AddWord(token, translation);
        }

        public void AddTranslation(string language, Dictionary<string,string> translations)
        {
            foreach (var kv in translations) AddTranslation(language, kv.Key, kv.Value);
        }

        public void AddYamlFile(string language, string yaml)
        {
            using (var reader = new System.IO.StringReader(yaml ?? ""))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var s = line.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    int colon = s.IndexOf(':');
                    if (colon <= 0) continue;
                    string key = s.Substring(0, colon).Trim();
                    string val = s.Substring(colon + 1).Trim();
                    if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                        val = val.Substring(1, val.Length - 2)
                            .Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
                    AddTranslation(language, key, val);
                }
            }
        }

        public IEnumerable<string> GetLanguages() => map.Keys;
        public IReadOnlyDictionary<string,string> GetTranslations(string language) =>
            map.TryGetValue(language, out var m) ? m : new Dictionary<string,string>();
    }
}
