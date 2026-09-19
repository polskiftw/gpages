using System;
using System.Collections.Generic;
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

        internal static Dictionary<int, GameObject> NamedPrefabs(ZNetScene scene) =>
            (Dictionary<int, GameObject>)ZNetNamedPrefabs.GetValue(scene);

        internal static Dictionary<int, GameObject> ItemByHash(ObjectDB db) =>
            (Dictionary<int, GameObject>)ObjectDbItemByHash.GetValue(db);

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

        internal static bool AssetLoaderReady =>
            AssetLoaderObject != null;
    }
}
