#if GLOADER
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;

/// <summary>
/// Small runtime observability hook used by the THICC stress matrix and useful
/// in ordinary logs. It does not mutate world state: after generation completes
/// it reports how many of Terraria's retail 8,000 chest slots are occupied.
/// Keeping this visible is preferable to guessing that a larger canvas needs a
/// wider chest-index/file/network contract.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldRuntimeMetricsPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "GenerateWorld" && candidate.IsStatic);
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "GenerateWorld");
        return method;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return;

        int occupied = 0;
        Chest[] chests = Main.chest;
        if (chests != null)
        {
            for (int i = 0; i < chests.Length; i++)
            {
                if (chests[i] != null)
                    occupied++;
            }
        }

        Console.WriteLine(
            "[Expanded Worlds] Runtime metrics: preset=" +
            ExpandedWorldMath.LabelFor(ExpandedWorldGenerationContext.ActivePreset) +
            "; dimensions=" + Main.maxTilesX + "x" + Main.maxTilesY +
            "; chests=" + occupied + "/" + (chests == null ? 0 : chests.Length) + ".");
    }
}
#endif
