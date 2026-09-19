using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;

namespace Jotunn.Managers
{
    internal static class GameInternals
    {
        private static readonly FieldInfo ZNetNamedPrefabs =
            AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
        private static readonly FieldInfo ObjectDbItemByHash =
            AccessTools.Field(typeof(ObjectDB), "m_itemByHash");
        private static readonly FieldInfo ObjectDbTerrainOps =
            AccessTools.Field(typeof(ObjectDB), "m_terrainOps");
        private static readonly FieldInfo ObjectDbTerrainOpsByHash =
            AccessTools.Field(typeof(ObjectDB), "m_terrainOpsByHash");
        private static readonly MethodInfo ObjectDbGetPrefabHash =
            AccessTools.Method(typeof(ObjectDB), "GetPrefabHash", new[] { typeof(GameObject) });
        private static readonly FieldInfo ZoneLocationsByHash =
            AccessTools.Field(typeof(ZoneSystem), "m_locationsByHash");
        private static readonly FieldInfo DungeonDbRooms =
            AccessTools.Field(typeof(DungeonDB), "m_rooms");
        private static readonly MethodInfo DungeonDbGenerateHashList =
            AccessTools.Method(typeof(DungeonDB), "GenerateHashList");
        private static readonly FieldInfo DungeonAvailableRooms =
            AccessTools.Field(typeof(DungeonGenerator), "m_availableRooms");
        private static readonly FieldInfo RuntimeAssetLoader =
            AccessTools.Field(typeof(Runtime), "s_assetLoader");

        private static readonly MethodInfo ZNetViewGetPrefabName =
            AccessTools.Method(typeof(ZNetView), "GetPrefabName");
        private static readonly MethodInfo RandomSpawnPrepare =
            AccessTools.Method(typeof(RandomSpawn), "Prepare");
        private static readonly FieldInfo RandomSpawnChildNetViews =
            AccessTools.Field(typeof(RandomSpawn), "m_childNetViews");

        private static readonly MethodInfo GetEnabledComponentsInChildrenDefinition =
            typeof(global::Utils)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                    method.Name == "GetEnabledComponentsInChildren" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(GameObject));

        internal static Dictionary<int, GameObject> NamedPrefabs(ZNetScene scene) =>
            (Dictionary<int, GameObject>)ZNetNamedPrefabs.GetValue(scene);

        internal static Dictionary<int, GameObject> ItemByHash(ObjectDB db) =>
            (Dictionary<int, GameObject>)ObjectDbItemByHash.GetValue(db);

        internal static void RegisterTerrainOp(TerrainOp terrainOp)
        {
            var db = ObjectDB.instance;
            if (db == null || !terrainOp ||
                ObjectDbTerrainOps == null ||
                ObjectDbTerrainOpsByHash == null ||
                ObjectDbGetPrefabHash == null)
            {
                return;
            }

            var terrainOps = (List<TerrainOp>)ObjectDbTerrainOps.GetValue(db);
            var terrainOpsByHash =
                (Dictionary<int, TerrainOp>)ObjectDbTerrainOpsByHash.GetValue(db);
            int hash = (int)ObjectDbGetPrefabHash.Invoke(
                db,
                new object[] { terrainOp.gameObject });

            if (terrainOpsByHash.TryGetValue(hash, out var registered))
            {
                if (registered != terrainOp)
                {
                    Logger.LogWarning(
                        $"TerrainOp prefab hash collision for " +
                        $"{terrainOp.gameObject.name} ({hash})");
                }
                return;
            }

            if (!terrainOps.Contains(terrainOp))
            {
                terrainOps.Add(terrainOp);
            }

            terrainOpsByHash.Add(hash, terrainOp);
        }

        internal static Dictionary<int, ZoneSystem.ZoneLocation> LocationHashes(ZoneSystem zone) =>
            (Dictionary<int, ZoneSystem.ZoneLocation>)ZoneLocationsByHash.GetValue(zone);

        internal static List<DungeonDB.RoomData> DungeonRooms(DungeonDB db) =>
            (List<DungeonDB.RoomData>)DungeonDbRooms.GetValue(db);

        internal static void GenerateDungeonHashList(DungeonDB db) =>
            DungeonDbGenerateHashList.Invoke(db, Array.Empty<object>());

        internal static List<DungeonDB.RoomData> AvailableRooms =>
            (List<DungeonDB.RoomData>)DungeonAvailableRooms.GetValue(null);

        internal static object AssetLoaderObject =>
            RuntimeAssetLoader?.GetValue(null);

        internal static bool AssetLoaderReady
        {
            get
            {
                var loader = AssetLoaderObject;
                if (loader == null)
                {
                    return false;
                }

                var initializedProperty =
                    AccessTools.Property(loader.GetType(), "Initialized");
                if (initializedProperty != null &&
                    initializedProperty.PropertyType == typeof(bool))
                {
                    return (bool)initializedProperty.GetValue(loader, null);
                }

                var initializedField =
                    AccessTools.Field(loader.GetType(), "Initialized") ??
                    AccessTools.Field(loader.GetType(), "m_initialized");
                if (initializedField != null &&
                    initializedField.FieldType == typeof(bool))
                {
                    return (bool)initializedField.GetValue(loader);
                }

                return true;
            }
        }

        internal static IEnumerable<T> EnabledComponentsInChildren<T>(GameObject root)
            where T : Behaviour
        {
            if (!root)
            {
                return Array.Empty<T>();
            }

            if (GetEnabledComponentsInChildrenDefinition != null)
            {
                try
                {
                    var method = GetEnabledComponentsInChildrenDefinition.MakeGenericMethod(typeof(T));
                    if (method.Invoke(null, new object[] { root }) is IEnumerable<T> result)
                    {
                        return result;
                    }
                }
                catch
                {
                    // Fall through to the Unity traversal below.
                }
            }

            return root.GetComponentsInChildren<T>(true)
                .Where(component => component && component.enabled);
        }

        internal static string GetPrefabName(ZNetView view)
        {
            if (!view)
            {
                return string.Empty;
            }

            try
            {
                return (string)ZNetViewGetPrefabName?.Invoke(view, Array.Empty<object>())
                    ?? view.gameObject.name;
            }
            catch
            {
                return view.gameObject.name;
            }
        }

        internal static void PrepareRandomSpawn(RandomSpawn spawn)
        {
            if (!spawn || RandomSpawnPrepare == null)
            {
                return;
            }

            RandomSpawnPrepare.Invoke(spawn, Array.Empty<object>());
        }

        internal static IEnumerable<ZNetView> RandomSpawnChildViews(RandomSpawn spawn)
        {
            if (!spawn || RandomSpawnChildNetViews == null)
            {
                return Array.Empty<ZNetView>();
            }

            return RandomSpawnChildNetViews.GetValue(spawn) as IEnumerable<ZNetView>
                ?? Array.Empty<ZNetView>();
        }
    }
}
