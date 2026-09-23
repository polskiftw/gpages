#if GLOADER_CLIENT
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;

public static class Mod
{
    public static void Load()
    {
        ExpandedWorldDimensions.ValidateVanillaSizeContract();
        Console.WriteLine(
            "[Expanded Worlds] THICC through THICC 11 world sizes enabled (10,600x3,000 through 31,600x9,000).");
        Console.WriteLine(
            "[Expanded Worlds] Custom dimensions continue Terraria's Small/Medium/Large section cadence; vanilla still categorizes them as Large.");
    }
}

internal static class ExpandedWorldState
{
    public static ExpandedWorldPreset Selected { get; private set; }
    public static bool IsCustomSelected => Selected != ExpandedWorldPreset.None;
    public static bool GenerationArmed => ExpandedWorldGenerationContext.IsActive;

    public static void Select(ExpandedWorldPreset preset)
    {
        if (preset != ExpandedWorldPreset.None)
            ExpandedWorldMath.DefinitionFor(preset);
        Selected = preset;
    }

    public static void ClearSelection()
    {
        Selected = ExpandedWorldPreset.None;
    }

    public static void ArmGeneration()
    {
        if (Selected == ExpandedWorldPreset.None)
            return;

        ExpandedWorldGenerationContext.Begin(Selected);
    }

    public static void EndGeneration()
    {
        ExpandedWorldGenerationContext.End();
    }

    public static string LabelFor(ExpandedWorldPreset preset)
    {
        return ExpandedWorldMath.LabelFor(preset);
    }
}

/// <summary>
/// Arm this one generation job at Terraria's normal CreateNewWorld boundary.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldCreatePatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "CreateNewWorld" && candidate.IsStatic);
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "CreateNewWorld");
        return method;
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        if (!ExpandedWorldState.IsCustomSelected)
            return;

        ExpandedWorldState.ArmGeneration();
        ExpandedWorldDimensions.ApplyActive("CreateNewWorld");
    }
}

/// <summary>
/// Reassert the selected dimensions immediately before Terraria allocates and
/// clears its world storage.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldClearPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "clearWorld" && candidate.IsStatic);
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "clearWorld");
        return method;
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        ExpandedWorldDimensions.ApplyActive("clearWorld");
    }
}

/// <summary>
/// Never leak an expanded creation preset into later world activity in the same
/// process, including exception paths.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldGenerateWorldLifetimePatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "GenerateWorld" && candidate.IsStatic);
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "GenerateWorld");
        return method;
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Exception __exception)
    {
        if (ExpandedWorldGenerationContext.IsActive)
        {
            if (__exception == null)
            {
                Console.WriteLine(
                    "[Expanded Worlds] Generation finished at " + Main.maxTilesX + "x" + Main.maxTilesY + ".");
            }
            else
            {
                Console.WriteLine("[Expanded Worlds] Generation failed: " + __exception);
            }

            ExpandedWorldState.EndGeneration();
        }

        return __exception;
    }
}
#endif
