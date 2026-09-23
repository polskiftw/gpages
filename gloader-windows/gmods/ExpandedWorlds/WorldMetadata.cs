#if GLOADER_CLIENT
using System;
using System.Reflection;
using HarmonyLib;
using Terraria.IO;

/// <summary>
/// WorldFileData's vanilla full-seed prefix recognizes only the three exact
/// Small/Medium/Large dimension pairs. Expanded Worlds intentionally keeps every
/// THICC tier categorically Large, so copied/displayed full seeds use the vanilla
/// Large prefix (3), not Unknown (0).
/// </summary>
[HarmonyPatch(typeof(WorldFileData), nameof(WorldFileData.GetFullSeedText))]
internal static class ExpandedWorldFullSeedPatch
{
    [HarmonyPostfix]
    private static void Postfix(WorldFileData __instance, ref string __result)
    {
        if (!IsExpandedLarge(__instance))
            return;

        if (string.IsNullOrEmpty(__result))
            throw new InvalidOperationException("[Expanded Worlds] WorldFileData.GetFullSeedText returned an empty value.");

        int firstDot = __result.IndexOf('.');
        if (firstDot <= 0)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] WorldFileData.GetFullSeedText format changed; refusing to guess the size prefix.");
        }

        string prefix = __result.Substring(0, firstDot);
        if (prefix == "3")
            return;

        if (prefix != "0")
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Unexpected full-seed world-size prefix '" + prefix +
                "' for " + __instance.WorldSizeX + "x" + __instance.WorldSizeY + ".");
        }

        __result = "3" + __result.Substring(firstDot);
    }

    private static bool IsExpandedLarge(WorldFileData data)
    {
        return data != null &&
               ExpandedWorldMath.IsExpandedPresetDimensions(data.WorldSizeX, data.WorldSizeY);
    }
}

/// <summary>
/// Vanilla labels any nonstandard physical dimensions as "Unknown". This is only
/// presentation state; expose the canonical THICC tier name while leaving all
/// gameplay/category logic at vanilla Large. Old worlds automatically relabel by
/// dimensions: former XL becomes THICC, former Huge becomes THICC 2, and former
/// THICC becomes THICC 3. No .wld migration or custom size identifier is needed.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldSizeNamePatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.PropertyGetter(typeof(WorldFileData), nameof(WorldFileData.WorldSizeName));
        if (method == null)
            throw new MissingMethodException(typeof(WorldFileData).FullName, "get_WorldSizeName");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix(WorldFileData __instance, ref string __result)
    {
        if (__instance == null)
            return;

        if (ExpandedWorldMath.TryGetPresetByDimensions(
                __instance.WorldSizeX,
                __instance.WorldSizeY,
                out ExpandedWorldPreset preset))
        {
            __result = ExpandedWorldMath.LabelFor(preset);
        }
    }
}
#endif
