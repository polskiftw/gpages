using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Utils;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jotunn.Managers
{
    public sealed class PrefabManager
    {
        private static PrefabManager instance;
        public static PrefabManager Instance => instance ??= new PrefabManager();
        public static event Action OnVanillaPrefabsAvailable;
        private readonly Dictionary<string, CustomPrefab> prefabs = new Dictionary<string, CustomPrefab>();
        internal GameObject PrefabContainer { get; }

        private PrefabManager()
        {
            PrefabContainer = new GameObject("Prefabs");
            PrefabContainer.transform.SetParent(Main.RootObject.transform);
            PrefabContainer.SetActive(false);
        }

        internal bool Contains(string name) => prefabs.ContainsKey(name);

        public void AddPrefab(CustomPrefab customPrefab)
        {
            if (customPrefab == null || !customPrefab.IsValid()) return;
            var name = customPrefab.Prefab.name;
            if (prefabs.ContainsKey(name)) return;
            customPrefab.Prefab.transform.SetParent(PrefabContainer.transform, false);
            prefabs.Add(name, customPrefab);
            AssetManager.Instance.AddAsset(customPrefab.Prefab);
        }

        public void AddPrefab(GameObject prefab)
        {
            if (!prefab) return;
            AddPrefab(new CustomPrefab(prefab, false));
        }

        public GameObject GetPrefab(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (prefabs.TryGetValue(name, out var custom)) return custom.Prefab;
            int hash = name.GetStableHashCode();
            if (ZNetScene.instance != null && GameInternals.NamedPrefabs(ZNetScene.instance).TryGetValue(hash, out var networked)) return networked;
            if (ObjectDB.instance != null && GameInternals.ItemByHash(ObjectDB.instance).TryGetValue(hash, out var item)) return item;
            return Cache.GetPrefab<GameObject>(name);
        }

        public GameObject CreateEmptyPrefab(string name, bool addZNetView = true)
        {
            if (string.IsNullOrEmpty(name) || GetPrefab(name))
            {
                return null;
            }

            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = name;
            prefab.transform.SetParent(PrefabContainer.transform, false);

            if (addZNetView)
            {
                var view = prefab.GetComponent<ZNetView>() ?? prefab.AddComponent<ZNetView>();
                view.m_persistent = true;
            }

            AssetManager.Instance.AddAsset(prefab);
            return prefab;
        }

        public GameObject CreateClonedPrefab(string name, string baseName)
        {
            return CreateClonedPrefab(name, GetPrefab(baseName));
        }

        public GameObject CreateClonedPrefab(string name, GameObject source)
        {
            if (!source || string.IsNullOrEmpty(name) || GetPrefab(name)) return null;
            var clone = Object.Instantiate(source, PrefabContainer.transform);
            clone.name = name;
            AssetManager.Instance.AddAsset(clone);
            return clone;
        }

        public void RemovePrefab(string name) => prefabs.Remove(name);

        internal void InvokeVanilla() => OnVanillaPrefabsAvailable?.Invoke();

        internal void RegisterAll(ZNetScene scene)
        {
            foreach (var custom in prefabs.Values.ToArray())
            {
                var prefab = custom.Prefab;
                if (!prefab) continue;
                if (custom.FixReference)
                {
                    prefab.FixReferences(true);
                    custom.FixReference = false;
                }

                RegisterToZNetScene(prefab, scene);
            }
        }

        internal void RegisterToZNetScene(GameObject prefab, ZNetScene scene = null)
        {
            scene ??= ZNetScene.instance;
            if (scene == null || !prefab || prefab.name.StartsWith("JVLmock_", StringComparison.Ordinal))
            {
                return;
            }

            int hash = prefab.name.GetStableHashCode();
            var namedPrefabs = GameInternals.NamedPrefabs(scene);
            if (namedPrefabs.ContainsKey(hash))
            {
                return;
            }

            if (prefab.GetComponent<ZNetView>() != null)
            {
                scene.m_prefabs.Add(prefab);
            }
            else
            {
                scene.m_nonNetViewPrefabs.Add(prefab);
            }

            namedPrefabs.Add(hash, prefab);

            var terrainOp = prefab.GetComponent<TerrainOp>();
            if (terrainOp)
            {
                GameInternals.RegisterTerrainOp(terrainOp);
            }
        }

        public static class Cache
        {
            private static readonly Dictionary<Type, Dictionary<string, Object>> cache = new Dictionary<Type, Dictionary<string, Object>>();

            public static T GetPrefab<T>(string name) where T : Object => (T)GetPrefab(typeof(T), name);

            internal static Object GetPrefab(Type type, string name)
            {
                try
                {
                    if (AssetManager.Instance.IsReady())
                    {
                        var soft = AssetManager.Instance.GetSoftReference(type, name);
                        if (soft.IsValid)
                        {
                            soft.Load();
                            var asset = soft.Asset;
                            if (asset && type.IsAssignableFrom(asset.GetType())) return asset;
                            if (asset is GameObject go)
                            {
                                var comp = go.GetComponent(type);
                                if (comp) return comp;
                            }
                        }
                    }
                }
                catch { }

                if (!cache.TryGetValue(type, out var map))
                {
                    map = new Dictionary<string, Object>();
                    foreach (var obj in Resources.FindObjectsOfTypeAll(type))
                        if (obj) map[obj.name] = obj;
                    cache[type] = map;
                }
                return map.TryGetValue(name, out var found) ? found : null;
            }

            internal static void Clear() => cache.Clear();
        }
    }

    public sealed class ZoneManager
    {
        private static ZoneManager instance;
        public static ZoneManager Instance => instance ??= new ZoneManager();
        public static event Action OnVanillaLocationsAvailable;
        internal readonly Dictionary<string, CustomLocation> Locations = new Dictionary<string, CustomLocation>();
        internal GameObject LocationContainer { get; }

        private ZoneManager()
        {
            LocationContainer = new GameObject("Locations");
            LocationContainer.transform.SetParent(Main.RootObject.transform);
            LocationContainer.SetActive(false);
        }

        public GameObject CreateLocationContainer(GameObject gameObject)
        {
            if (!gameObject) return null;
            var copy = Object.Instantiate(gameObject, LocationContainer.transform);
            copy.name = gameObject.name;
            return copy;
        }

        public bool AddCustomLocation(CustomLocation customLocation)
        {
            if (customLocation == null || string.IsNullOrEmpty(customLocation.Name) || customLocation.ZoneLocation == null) return false;
            if (Locations.ContainsKey(customLocation.Name)) return false;
            if (!customLocation.SoftReference && customLocation.Prefab) customLocation.Prefab.SetActive(true);
            Locations.Add(customLocation.Name, customLocation);
            return true;
        }

        internal void Setup(ZoneSystem zone)
        {
            OnVanillaLocationsAvailable?.Invoke();

            foreach (var custom in Locations.Values)
            {
                if (custom.FixReference && custom.Prefab && !custom.SoftReference)
                {
                    custom.Prefab.FixReferences(true);
                    custom.FixReference = false;
                }

                if (!custom.SoftReference)
                {
                    PrepareLocation(custom.ZoneLocation);
                }

                int hash = custom.Name.GetStableHashCode();
                if (!GameInternals.LocationHashes(zone).ContainsKey(hash))
                {
                    zone.m_locations.Add(custom.ZoneLocation);
                    GameInternals.LocationHashes(zone)[hash] = custom.ZoneLocation;
                }

                if (custom.ZoneLocation.m_prefab.IsLoaded)
                {
                    custom.ZoneLocation.m_prefab.Release();
                }
            }
        }

        internal void PrepareLocation(ZoneSystem.ZoneLocation location)
        {
            location.m_prefab.Load();
            var root = location.m_prefab.Asset;
            if (!root)
            {
                return;
            }

            foreach (var znet in GameInternals.EnabledComponentsInChildren<ZNetView>(root))
            {
                RegisterUnknownPrefab(znet);
            }

            foreach (var randomSpawn in GameInternals.EnabledComponentsInChildren<RandomSpawn>(root))
            {
                GameInternals.PrepareRandomSpawn(randomSpawn);
                foreach (var znet in GameInternals.RandomSpawnChildViews(randomSpawn))
                {
                    RegisterUnknownPrefab(znet);
                }
            }
        }

        private static void RegisterUnknownPrefab(ZNetView znet)
        {
            if (!znet)
            {
                return;
            }

            var prefabName = GameInternals.GetPrefabName(znet);
            if (string.IsNullOrEmpty(prefabName) ||
                prefabName.StartsWith("JVLmock_", StringComparison.Ordinal))
            {
                return;
            }

            var scene = ZNetScene.instance;
            int hash = prefabName.GetStableHashCode();
            if (scene != null && GameInternals.NamedPrefabs(scene).ContainsKey(hash))
            {
                return;
            }

            var prefab = Object.Instantiate(znet.gameObject, PrefabManager.Instance.PrefabContainer.transform);
            prefab.name = prefabName;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
            PrefabManager.Instance.RegisterToZNetScene(prefab, scene);
        }
    }

    public sealed class DungeonManager
    {
        private static DungeonManager instance;
        public static DungeonManager Instance => instance ??= new DungeonManager();
        public static event Action OnVanillaRoomsAvailable;
        internal readonly Dictionary<string, CustomRoom> Rooms = new Dictionary<string, CustomRoom>();
        private readonly List<string> themeList = new List<string>();
        private readonly Dictionary<int, string> roomHashes = new Dictionary<int, string>();
        internal GameObject DungeonRoomContainer { get; }

        private DungeonManager()
        {
            DungeonRoomContainer = new GameObject("DungeonRooms");
            DungeonRoomContainer.transform.SetParent(Main.RootObject.transform);
            DungeonRoomContainer.SetActive(false);
        }

        public bool AddCustomRoom(CustomRoom customRoom)
        {
            if (customRoom == null || string.IsNullOrEmpty(customRoom.Name) || customRoom.RoomData == null) return false;
            if (string.IsNullOrEmpty(customRoom.ThemeName)) throw new ArgumentException("ThemeName must have a value", nameof(customRoom));
            if (!CustomRoom.IsVanillaTheme(customRoom.ThemeName) && !themeList.Contains(customRoom.ThemeName))
                throw new ArgumentException("ThemeName must be vanilla or registered", nameof(customRoom));
            if (Rooms.ContainsKey(customRoom.Name)) return false;
            if (!customRoom.SoftReference && customRoom.Prefab)
            {
                customRoom.Prefab.transform.SetParent(DungeonRoomContainer.transform);
                customRoom.Prefab.SetActive(true);
            }
            Rooms.Add(customRoom.Name, customRoom);
            return true;
        }

        public bool RegisterDungeonTheme(GameObject prefab, string themeName)
        {
            if (!prefab) throw new ArgumentException("Cannot be null", nameof(prefab));
            if (string.IsNullOrEmpty(themeName)) throw new ArgumentException("Cannot be empty", nameof(themeName));
            var generator = prefab.GetComponentInChildren<DungeonGenerator>();
            if (!generator) throw new ArgumentException("Prefab must contain a DungeonGenerator", nameof(prefab));
            var proxy = generator.gameObject.GetComponent<DungeonGeneratorTheme>() ?? generator.gameObject.AddComponent<DungeonGeneratorTheme>();
            proxy.m_themeName = themeName;
            if (!themeList.Contains(themeName)) themeList.Add(themeName);
            return true;
        }

        internal void StartDungeonDB(DungeonDB db)
        {
            OnVanillaRoomsAvailable?.Invoke();

            foreach (var room in Rooms.Values)
            {
                if (room.FixReference && room.Prefab && !room.SoftReference)
                {
                    room.Prefab.FixReferences(true);
                    room.FixReference = false;
                }

                if (CustomRoom.IsVanillaTheme(room.ThemeName) &&
                    !GameInternals.DungeonRooms(db).Contains(room.RoomData))
                {
                    GameInternals.DungeonRooms(db).Add(room.RoomData);
                }
            }

            GameInternals.GenerateDungeonHashList(db);

            roomHashes.Clear();
            foreach (var room in Rooms.Values)
            {
                int hash = room.Name.GetStableHashCode();
                if (!roomHashes.ContainsKey(hash))
                {
                    roomHashes.Add(hash, room.Name);
                }
            }
        }

        internal DungeonDB.RoomData GetRoomByHash(int hash)
        {
            return roomHashes.TryGetValue(hash, out var name) &&
                   Rooms.TryGetValue(name, out var room)
                ? room.RoomData
                : null;
        }

        internal void AppendRooms(DungeonGenerator generator)
        {
            if (GameInternals.AvailableRooms == null) return;
            var proxy = generator.GetComponent<DungeonGeneratorTheme>();
            IEnumerable<CustomRoom> selected;
            if (proxy != null && !string.IsNullOrEmpty(proxy.m_themeName))
                selected = Rooms.Values.Where(r => r.RoomData.m_enabled && r.ThemeName == proxy.m_themeName);
            else
                selected = Rooms.Values.Where(r => r.RoomData.m_enabled && CustomRoom.IsVanillaTheme(r.ThemeName) &&
                    Enum.TryParse(r.ThemeName, false, out Room.Theme theme) && theme != Room.Theme.None && generator.m_themes.HasFlag(theme));
            foreach (var room in selected)
                if (!GameInternals.AvailableRooms.Contains(room.RoomData)) GameInternals.AvailableRooms.Add(room.RoomData);
        }
    }

    public sealed class ItemManager
    {
        private static ItemManager instance;
        public static ItemManager Instance => instance ??= new ItemManager();
        public static event Action OnItemsRegistered;
        private readonly Dictionary<string, CustomItem> items = new Dictionary<string, CustomItem>();
        private readonly List<CustomRecipe> recipes = new List<CustomRecipe>();
        private readonly List<CustomStatusEffect> effects = new List<CustomStatusEffect>();

        private ItemManager() { }

        public bool AddItem(CustomItem item)
        {
            if (item == null || !item.IsValid() || items.ContainsKey(item.ItemPrefab.name)) return false;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(item.ItemPrefab, item.FixReference));
            if (item.ItemPrefab.layer == 0) item.ItemPrefab.layer = LayerMask.NameToLayer("item");
            items.Add(item.ItemPrefab.name, item);
            if (item.Recipe != null)
            {
                AddRecipe(item.Recipe);
            }
            return true;
        }

        public CustomItem GetItem(string itemName)
        {
            return itemName != null &&
                items.TryGetValue(itemName, out var item)
                ? item
                : null;
        }

        public bool AddRecipe(CustomRecipe recipe)
        {
            if (recipe == null || !recipe.IsValid())
            {
                return false;
            }

            if (recipes.Any(existing =>
                existing.Recipe &&
                recipe.Recipe &&
                existing.Recipe.name == recipe.Recipe.name))
            {
                return false;
            }

            recipes.Add(recipe);
            return true;
        }

        public CustomRecipe GetRecipe(string recipeName)
        {
            return recipes.FirstOrDefault(recipe =>
                recipe?.Recipe && recipe.Recipe.name == recipeName);
        }

        public bool AddStatusEffect(CustomStatusEffect effect)
        {
            if (effect == null || !effect.IsValid() || effects.Any(x => x.StatusEffect == effect.StatusEffect)) return false;
            effects.Add(effect);
            return true;
        }

        internal void Register(ObjectDB db)
        {
            foreach (var item in items.Values)
            {
                if (!item.ItemPrefab || !item.ItemDrop) continue;
                item.ItemDrop.m_itemData.m_dropPrefab = item.ItemPrefab;
                int hash = item.ItemPrefab.name.GetStableHashCode();
                if (!GameInternals.ItemByHash(db).ContainsKey(hash))
                {
                    db.m_items.Add(item.ItemPrefab);
                    GameInternals.ItemByHash(db).Add(hash, item.ItemPrefab);
                }
            }
            foreach (var recipe in recipes)
            {
                if (recipe?.Recipe == null)
                {
                    continue;
                }

                if ((recipe.FixReference || recipe.FixRequirementReferences))
                {
                    recipe.Recipe.FixReferences();
                    recipe.FixReference = false;
                    recipe.FixRequirementReferences = false;
                }

                if (!db.m_recipes.Contains(recipe.Recipe))
                {
                    db.m_recipes.Add(recipe.Recipe);
                }
            }

            foreach (var effect in effects)
                if (effect.StatusEffect != null && !db.m_StatusEffects.Contains(effect.StatusEffect))
                    db.m_StatusEffects.Add(effect.StatusEffect);
        }

        internal void InvokeRegistered() => OnItemsRegistered?.Invoke();
    }

    public sealed class LocalizationManager
    {
        private static LocalizationManager instance;
        public static LocalizationManager Instance => instance ??= new LocalizationManager();
        private readonly CustomLocalization localization = new CustomLocalization();
        private LocalizationManager() { }
        public CustomLocalization GetLocalization() => localization;

        public void AddToken(string token, string value, bool force = false)
        {
            localization.AddToken(token, value, force);
        }

        internal void Apply() => localization.ApplyCurrent();
    }

    public sealed class NetworkManager
    {
        private static NetworkManager instance;
        public static NetworkManager Instance => instance ??= new NetworkManager();
        public delegate IEnumerator CoroutineHandler(long sender, ZPackage package);
        private readonly List<CustomRPC> rpcs = new List<CustomRPC>();

        private NetworkManager() { }

        public CustomRPC AddRPC(string name, CoroutineHandler serverReceive, CoroutineHandler clientReceive)
        {
            var callingAssembly = Assembly.GetCallingAssembly();
            var plugin = callingAssembly.GetCustomAttributes(typeof(BepInEx.BepInPlugin), false)
                .OfType<BepInEx.BepInPlugin>()
                .FirstOrDefault();
            var owner = plugin?.GUID ?? callingAssembly.GetName().Name ?? "mod";
            var id = owner + "!" + name;
            var found = rpcs.FirstOrDefault(r => r.ID == id);
            if (found != null) return found;
            var rpc = new CustomRPC(id, serverReceive, clientReceive);
            rpcs.Add(rpc);
            if (ZRoutedRpc.instance != null) Register(rpc);
            return rpc;
        }

        internal void RegisterAll()
        {
            if (ZRoutedRpc.instance == null) return;
            foreach (var rpc in rpcs) Register(rpc);
        }

        private static void Register(CustomRPC rpc)
        {
            ZRoutedRpc.instance.Register(rpc.ID, new Action<long, ZPackage>(rpc.Receive));
        }
    }

    internal static class SlimPatches
    {
        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPrefix]
        private static void ObjectDBCopyPrefix() => PrefabManager.Instance.InvokeVanilla();

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void ZNetSceneAwake(ZNetScene __instance) => PrefabManager.Instance.RegisterAll(__instance);

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPrefix]
        private static void ObjectDBAwakePrefix(ObjectDB __instance)
        {
            ItemManager.Instance.Register(__instance);
            PieceManager.Instance.Register(__instance);
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void ObjectDBAwakePostfix() => ItemManager.Instance.InvokeRegistered();

        [HarmonyPatch(typeof(ZoneSystem), "SetupLocations")]
        [HarmonyPostfix]
        private static void ZoneSetupPostfix(ZoneSystem __instance)
        {
            PrefabManager.Cache.Clear();
            ZoneManager.Instance.Setup(__instance);
        }

        [HarmonyPatch(typeof(DungeonDB), "Start")]
        [HarmonyPostfix]
        private static void DungeonStart(DungeonDB __instance) => DungeonManager.Instance.StartDungeonDB(__instance);

        [HarmonyPatch(typeof(DungeonDB), "GetRoom")]
        [HarmonyPrefix]
        private static bool DungeonGetRoom(int hash, ref DungeonDB.RoomData __result)
        {
            var custom = DungeonManager.Instance.GetRoomByHash(hash);
            if (custom == null)
            {
                return true;
            }

            __result = custom;
            return false;
        }

        [HarmonyPatch(typeof(DungeonGenerator), "SetupAvailableRooms")]
        [HarmonyPostfix]
        private static void DungeonRooms(DungeonGenerator __instance) => DungeonManager.Instance.AppendRooms(__instance);

        [HarmonyPatch(typeof(Game), "Start")]
        [HarmonyPostfix]
        private static void GameStart() => NetworkManager.Instance.RegisterAll();

        [HarmonyPatch(typeof(global::Console), "Awake")]
        [HarmonyPostfix]
        private static void ConsoleAwake() => CommandManager.Instance.RegisterAll();

        [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
        [HarmonyPostfix]
        private static void LocalizationSetup() => LocalizationManager.Instance.Apply();

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.LoadMapData))]
        [HarmonyPostfix]
        private static void MinimapLoadMapData() =>
            MinimapManager.InvokeVanillaMapDataLoaded();
    }
}
