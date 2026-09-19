using System;
using BepInEx;
using HarmonyLib;

namespace JotunnCompat.FastPath
{
    /// <summary>
    /// Loads after Jotunn's Awake (hard dependency) and before Unity calls Start.
    /// This lets us replace Jotunn's expensive Start-time compatibility routines
    /// without changing the Jotunn assembly or public API that existing mods bind to.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, Version)]
    [BepInDependency(JotunnGuid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "claire.valheim.jotunncompat.fastpath";
        public const string ModName = "Jotunn Compatibility FastPath";
        public const string Version = "0.1.0";
        public const string JotunnGuid = "com.jotunn.jotunn";

        private Harmony harmony;

        private void Awake()
        {
            harmony = new Harmony(ModGuid);

            PatchRequired(
                "Jotunn.Utils.PatchInit",
                "InitializePatches",
                nameof(PatchInitPrefix));

            PatchRequired(
                "Jotunn.Utils.AutomaticLocalizationsLoading",
                "Init",
                nameof(LocalizationInitPrefix));

            Logger.LogInfo("Jotunn compatibility fast paths installed.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        private void PatchRequired(string typeName, string methodName, string prefixName)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                throw new TypeLoadException("Required Jotunn type not found: " + typeName);
            }

            var original = AccessTools.Method(type, methodName, Type.EmptyTypes);
            if (original == null)
            {
                throw new MissingMethodException(typeName, methodName);
            }

            var prefix = AccessTools.Method(typeof(Plugin), prefixName);
            if (prefix == null)
            {
                throw new MissingMethodException(typeof(Plugin).FullName, prefixName);
            }

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }

        private static bool PatchInitPrefix()
        {
            Startup.InitializePatches();
            return false;
        }

        private static bool LocalizationInitPrefix()
        {
            Startup.LoadLocalizations();
            return false;
        }
    }
}
