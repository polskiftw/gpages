using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace JotunnCompat.Preloader
{
    /// <summary>
    /// Generic reliability guards for Jotunn AssetManager startup.
    ///
    /// Jotunn 2.30.1's asset-path transpiler assumes its Dictionary.Add match always
    /// exists. If another mod has already transformed that instruction, CodeMatcher
    /// SetInstruction throws and AssetManager's static initialization can fail.
    ///
    /// We only swallow that exact Harmony CodeMatcher failure and return the incoming
    /// instruction stream unchanged. Any other exception is preserved.
    /// </summary>
    internal static class AssetManagerSafety
    {
        private const string InvalidMatcherMessage =
            "Cannot set instruction/opcode at invalid position.";

        private static ManualLogSource log;

        public static void Install(Harmony harmony, Assembly jotunnAssembly)
        {
            var patchesType = jotunnAssembly.GetType(
                "Jotunn.Managers.AssetManager+Patches",
                false);

            if (patchesType == null)
            {
                throw new TypeLoadException(
                    "Required Jotunn type not found: Jotunn.Managers.AssetManager+Patches");
            }

            var original = AccessTools.Method(
                patchesType,
                "AssetBundleLoader_GetAllAssetPathsMappedToAssetID");

            if (original == null)
            {
                throw new MissingMethodException(
                    patchesType.FullName,
                    "AssetBundleLoader_GetAllAssetPathsMappedToAssetID");
            }

            var finalizer = AccessTools.Method(
                typeof(AssetManagerSafety),
                nameof(AssetPathTranspilerFinalizer));

            if (finalizer == null)
            {
                throw new MissingMethodException(
                    typeof(AssetManagerSafety).FullName,
                    nameof(AssetPathTranspilerFinalizer));
            }

            harmony.Patch(
                original,
                finalizer: new HarmonyMethod(finalizer));
        }

        private static Exception AssetPathTranspilerFinalizer(
            IEnumerable<CodeInstruction> instructions,
            ref IEnumerable<CodeInstruction> __result,
            Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            if (__exception is InvalidOperationException &&
                string.Equals(
                    __exception.Message,
                    InvalidMatcherMessage,
                    StringComparison.Ordinal))
            {
                __result = instructions;
                Log.LogWarning(
                    "Jotunn AssetManager asset-path transpiler found an already-modified " +
                    "instruction stream. Keeping the incoming instructions instead of " +
                    "failing AssetManager initialization.");
                return null;
            }

            return __exception;
        }

        private static ManualLogSource Log =>
            log ?? (log = Logger.CreateLogSource("JotunnCompat"));
    }
}
