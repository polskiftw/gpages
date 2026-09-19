using System.Collections;
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

            StartCoroutine(AnnounceGUIAvailability());

            Harmony.PatchAll(typeof(Managers.SlimPatches));
            Harmony.PatchAll(typeof(Managers.AssetManager.Patches));
            PatchMinimapLifecycle();
            Game.isModded = true;
        }

        private static void PatchMinimapLifecycle()
        {
            var target = AccessTools.Method(typeof(Minimap), "LoadMapData") ??
                AccessTools.Method(typeof(Minimap), "Start");
            var postfix = AccessTools.Method(
                typeof(Managers.SlimPatches),
                nameof(Managers.SlimPatches.MinimapDataLoaded));

            if (target == null || postfix == null)
            {
                Jotunn.Logger.LogWarning(
                    "Could not find a Minimap lifecycle method for map-ready notifications");
                return;
            }

            Harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        private IEnumerator AnnounceGUIAvailability()
        {
            // Jotunn loads before dependent plugins. Defer one frame so consumers
            // have had a chance to subscribe to OnCustomGUIAvailable.
            yield return null;
            GUIManager.Instance.EnsureGUI();
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
            Jotunn.Logger.LogDebug("Initializing " + module);
        }
    }
}
