using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace Jotunn
{
    [BepInPlugin(ModGuid, ModName, Version)]
    public sealed class Main : BaseUnityPlugin
    {
        public const string Version = "2.30.1";
        public const string ModName = "Jotunn";
        public const string ModGuid = "com.jotunn.jotunn";

        internal static Main Instance;
        internal static readonly Harmony Harmony = new Harmony(ModGuid);

        private static GameObject rootObject;
        internal static GameObject RootObject => GetRootObject();

        private void Awake()
        {
            Instance = this;
            GetRootObject();

            // Touch only the generic subsystems shipped by the slim build.
            _ = AssetManager.Instance;
            _ = PrefabManager.Instance;
            _ = ZoneManager.Instance;
            _ = DungeonManager.Instance;
            _ = ItemManager.Instance;
            _ = LocalizationManager.Instance;
            _ = NetworkManager.Instance;
            _ = GUIManager.Instance;

            Game.isModded = true;
        }

        private void OnApplicationQuit()
        {
            AssetBundle.UnloadAllAssetBundles(false);
        }

        private static GameObject GetRootObject()
        {
            if (rootObject)
            {
                return rootObject;
            }

            rootObject = new GameObject("_JotunnSlimRoot");
            DontDestroyOnLoad(rootObject);
            return rootObject;
        }

        internal static void LogInit(string module)
        {
            Logger.LogDebug("Initializing " + module);
        }
    }
}
