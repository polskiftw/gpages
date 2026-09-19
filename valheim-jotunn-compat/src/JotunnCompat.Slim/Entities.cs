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
        public CustomRecipe Recipe { get; }
        public bool FixReference { get; set; }

        public CustomItem(GameObject itemPrefab, bool fixReference)
        {
            ItemPrefab = itemPrefab;
            ItemDrop = itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
            FixReference = fixReference;
        }

        public CustomItem(
            GameObject itemPrefab,
            bool fixReference,
            ItemConfig itemConfig)
            : this(itemPrefab, fixReference)
        {
            if (ItemPrefab && itemConfig != null)
            {
                itemConfig.Apply(ItemPrefab);
                var recipe = itemConfig.GetRecipe(ItemPrefab);
                if (recipe)
                {
                    Recipe = new CustomRecipe(recipe, true, true);
                }
            }
        }

        public CustomItem(string name, string basePrefabName, ItemConfig itemConfig)
            : this(
                PrefabManager.Instance.CreateClonedPrefab(name, basePrefabName),
                false,
                itemConfig)
        {
        }

        public CustomItem(
            AssetBundle bundle,
            string assetName,
            bool fixReference,
            ItemConfig itemConfig)
            : this(
                bundle != null ? bundle.LoadAsset<GameObject>(assetName) : null,
                fixReference,
                itemConfig)
        {
        }

        internal bool IsValid() => ItemPrefab && ItemDrop;
        public override string ToString() =>
            ItemPrefab ? ItemPrefab.name : "<invalid item>";
    }

    public class CustomPiece
    {
        public GameObject PiecePrefab { get; }
        public Piece Piece { get; }
        public string PieceTable { get; set; }
        public string Category { get; set; }
        public string[] Usage { get; set; } = Array.Empty<string>();
        public bool FixReference { get; set; }
        internal bool FixConfig { get; set; }

        public CustomPiece(
            GameObject piecePrefab,
            bool fixReference,
            PieceConfig pieceConfig)
        {
            PiecePrefab = piecePrefab;
            Piece = piecePrefab ? piecePrefab.GetComponent<Piece>() : null;
            FixReference = fixReference;

            if (pieceConfig != null && piecePrefab)
            {
                PieceTable = pieceConfig.PieceTable;
                Category = pieceConfig.Category;
                Usage = pieceConfig.Usage ?? Array.Empty<string>();
                FixConfig = true;
                pieceConfig.Apply(piecePrefab);
            }
        }

        public CustomPiece(
            GameObject piecePrefab,
            string pieceTable,
            bool fixReference)
        {
            PiecePrefab = piecePrefab;
            Piece = piecePrefab ? piecePrefab.GetComponent<Piece>() : null;
            PieceTable = pieceTable;
            FixReference = fixReference;
        }

        public bool IsValid() =>
            PiecePrefab && Piece && !string.IsNullOrEmpty(PieceTable);

        public override string ToString() =>
            PiecePrefab ? PiecePrefab.name : "<invalid piece>";
    }

    public class CustomPieceTable
    {
        public GameObject PieceTablePrefab { get; }
        public PieceTable PieceTable { get; }
        public string[] Categories { get; set; } = Array.Empty<string>();
        public bool GuessUsage { get; set; } = true;

        public CustomPieceTable(string name, PieceTableConfig config)
        {
            PieceTablePrefab = new GameObject(name);
            PieceTable = PieceTablePrefab.AddComponent<PieceTable>();
            if (config != null)
            {
                config.Apply(PieceTablePrefab);
                Categories = config.GetCategories();
                GuessUsage = config.GuessUsage;
            }
        }

        public CustomPieceTable(GameObject prefab)
        {
            PieceTablePrefab = prefab;
            PieceTable = prefab ? prefab.GetComponent<PieceTable>() : null;
        }

        public bool IsValid() => PieceTablePrefab && PieceTable;
        public override string ToString() =>
            PieceTablePrefab ? PieceTablePrefab.name : "<invalid piece table>";
    }

    public class CustomRecipe
    {
        public Recipe Recipe { get; }
        public bool FixReference { get; set; }
        public bool FixRequirementReferences { get; set; }

        public CustomRecipe(
            Recipe recipe,
            bool fixReference,
            bool fixRequirementReferences)
        {
            Recipe = recipe;
            FixReference = fixReference;
            FixRequirementReferences = fixRequirementReferences;
        }

        public CustomRecipe(RecipeConfig recipeConfig)
        {
            Recipe = recipeConfig?.GetRecipe();
            FixReference = true;
            FixRequirementReferences = true;
        }

        public bool IsValid() => Recipe && Recipe.m_item;
        public override string ToString() =>
            Recipe ? Recipe.name : "<invalid recipe>";
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
        private readonly LocationConfig locationConfig;

        public GameObject Prefab { get; private set; }
        public ZoneSystem.ZoneLocation ZoneLocation { get; }
        public Location Location { get; private set; }
        public string Name { get; }
        public bool FixReference { get; set; }
        public bool SoftReference { get; }

        public CustomLocation(GameObject exteriorPrefab, bool fixReference, LocationConfig config)
        {
            Prefab = exteriorPrefab;
            Name = exteriorPrefab ? exteriorPrefab.name : string.Empty;
            locationConfig = config;
            FixReference = fixReference;

            if (exteriorPrefab)
            {
                Location = exteriorPrefab.GetComponent<Location>();
                if (!Location)
                {
                    Location = exteriorPrefab.AddComponent<Location>();
                    Location.m_clearArea = config.ClearArea;
                    Location.m_exteriorRadius = config.ExteriorRadius;
                    Location.m_hasInterior = config.HasInterior;
                    Location.m_interiorRadius = config.InteriorRadius;
                    Location.m_interiorEnvironment = config.InteriorEnvironment;
                }
            }

            ZoneLocation = config.GetZoneLocation();
            ZoneLocation.m_prefabName = Name;

            if (exteriorPrefab)
            {
                var id = AssetManager.Instance.AddAsset(exteriorPrefab);
                ZoneLocation.m_prefab = new SoftReference<GameObject>(id);
                SyncZoneLocationFromComponent(Location);
            }
        }

        public CustomLocation(
            SoftReference<GameObject> softReferencePrefab,
            bool fixReference,
            LocationConfig config)
        {
            Name = softReferencePrefab.Name;
            locationConfig = config;
            FixReference = fixReference;
            SoftReference = true;
            ZoneLocation = config.GetZoneLocation();
            ZoneLocation.m_prefab = softReferencePrefab;
            ZoneLocation.m_prefabName = Name;

            AssetManager.Instance.ResolveMocksOnLoad(
                softReferencePrefab,
                ZoneManager.Instance.LocationContainer.transform,
                OnLocationResolve);
        }

        private void OnLocationResolve(GameObject gameObject)
        {
            Prefab = gameObject;
            if (!gameObject)
            {
                return;
            }

            gameObject.SetActive(true);
            Location = gameObject.GetComponent<Location>();
            if (Location)
            {
                SyncZoneLocationFromComponent(Location);
            }

            ZoneManager.Instance.PrepareLocation(ZoneLocation);
        }

        private void SyncZoneLocationFromComponent(Location location)
        {
            if (!location || ZoneLocation == null || locationConfig == null)
            {
                return;
            }

            if (!locationConfig.HasExteriorRadius)
            {
                ZoneLocation.m_exteriorRadius = location.m_exteriorRadius;
            }

            if (!locationConfig.HasInteriorRadius)
            {
                ZoneLocation.m_interiorRadius = location.m_interiorRadius;
            }

            if (!locationConfig.HasClearArea)
            {
                ZoneLocation.m_clearArea = location.m_clearArea;
            }
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

        private readonly Dictionary<string, Dictionary<string, string>> map =
            new Dictionary<string, Dictionary<string, string>>();

        private static void AddWord(string key, string value)
        {
            if (Localization.instance == null || AddWordMethod == null)
            {
                return;
            }

            AddWordMethod.Invoke(
                Localization.instance,
                new object[] { key, value });
        }

        internal void AddToken(string token, string value, bool force)
        {
            if (string.IsNullOrEmpty(token))
            {
                return;
            }

            var key = token.TrimStart('$');
            if (!map.TryGetValue("English", out var lang))
            {
                map["English"] = lang =
                    new Dictionary<string, string>();
            }

            if (force || !lang.ContainsKey(key))
            {
                lang[key] = value ?? string.Empty;
            }

            AddWord(key, lang[key]);
        }

        public void AddYamlFile(string language, string fileContent)
        {
            if (string.IsNullOrEmpty(language) ||
                string.IsNullOrEmpty(fileContent))
            {
                return;
            }

            Dictionary<string, string> parsed;
            try
            {
                parsed = new YamlDotNet.Serialization.DeserializerBuilder()
                    .Build()
                    .Deserialize<Dictionary<string, string>>(fileContent);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "Could not parse localization YAML: " + ex.Message);
                return;
            }

            if (parsed == null)
            {
                return;
            }

            if (!map.TryGetValue(language, out var lang))
            {
                map[language] = lang =
                    new Dictionary<string, string>();
            }

            foreach (var kv in parsed)
            {
                var key = kv.Key.TrimStart('$');
                lang[key] = kv.Value;
                AddWord(key, kv.Value);
            }
        }

        internal void ApplyCurrent()
        {
            if (Localization.instance == null)
            {
                return;
            }

            var language = Localization.instance.GetSelectedLanguage();
            if (!map.TryGetValue(language, out var words) &&
                !map.TryGetValue("English", out words))
            {
                return;
            }

            foreach (var kv in words)
            {
                AddWord(kv.Key, kv.Value);
            }
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
