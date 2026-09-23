#if GLOADER_SERVER
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;

/// <summary>
/// Dedicated-server entry point for Expanded Worlds. Set GLOADER_EXPANDED_WORLD
/// to THICC or THICC2 through THICC11 and use Terraria's normal Large autocreate
/// path. The same shared generation context, dimensions, tier continuations, and
/// scratch-capacity guards used by the client are active here.
/// </summary>
public static class Mod
{
    public static void Load()
    {
        ExpandedWorldDimensions.ValidateVanillaSizeContract();
        ExpandedWorldServerState.ConfigureFromEnvironment();
    }
}

internal static class ExpandedWorldServerState
{
    internal static ExpandedWorldPreset Requested { get; private set; }

    internal static void ConfigureFromEnvironment()
    {
        string raw = Environment.GetEnvironmentVariable("GLOADER_EXPANDED_WORLD");
        if (string.IsNullOrWhiteSpace(raw))
        {
            Requested = ExpandedWorldPreset.None;
            Console.WriteLine("[Expanded Worlds] Dedicated-server headless preset not requested; vanilla server sizing is untouched.");
            return;
        }

        if (!ExpandedWorldMath.TryParsePreset(raw, out ExpandedWorldPreset parsed))
        {
            throw new ArgumentException(
                "GLOADER_EXPANDED_WORLD must be THICC or THICC2 through THICC11; received '" + raw + "'.");
        }

        Requested = parsed;
        Console.WriteLine(
            "[Expanded Worlds] Dedicated-server headless preset: " +
            ExpandedWorldMath.LabelFor(Requested) + " " +
            ExpandedWorldMath.WidthFor(Requested) + "x" + ExpandedWorldMath.HeightFor(Requested) + ".");
    }

    internal static void BeginGeneration()
    {
        if (Requested == ExpandedWorldPreset.None)
            return;

        ExpandedWorldGenerationContext.Begin(Requested);
        ExpandedWorldDimensions.ApplyActive("GenerateWorld");
    }

    internal static void EndGeneration()
    {
        ExpandedWorldGenerationContext.End();
    }
}

/// <summary>
/// TerrariaServer autocreate reaches WorldGen.GenerateWorld directly rather than
/// the client's CreateNewWorld wrapper.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldServerGenerateBeginPatch
{
    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.GetDeclaredMethods(typeof(WorldGen))
            .FirstOrDefault(candidate => candidate.Name == "GenerateWorld" && candidate.IsStatic);

        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "GenerateWorld");

        return method;
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        ExpandedWorldServerState.BeginGeneration();
    }
}

/// <summary>
/// Reassert dimensions immediately before Terraria allocates its world storage.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldServerClearPatch
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
/// Never leak one headless generation request into subsequent world activity.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldServerGenerateEndPatch
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
                    "[Expanded Worlds] Dedicated-server generation completed at " +
                    Main.maxTilesX + "x" + Main.maxTilesY + ".");
            }
            else
            {
                Console.WriteLine("[Expanded Worlds] Dedicated-server generation failed: " + __exception);
            }

            ExpandedWorldServerState.EndGeneration();
        }

        return __exception;
    }
}

/// <summary>
/// Validate dimensions after Terraria's normal world-load path completes.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldServerLoadVerificationPatch
{
    private static MethodBase TargetMethod()
    {
        Type worldFileType = typeof(Main).Assembly.GetType("Terraria.IO.WorldFile", false);
        if (worldFileType == null)
            throw new TypeLoadException("Terraria.IO.WorldFile was not found in the loaded Terraria assembly.");

        MethodBase method = AccessTools.Method(worldFileType, "LoadWorld", Type.EmptyTypes);
        if (method == null)
            throw new MissingMethodException(worldFileType.FullName, "LoadWorld()");

        return method;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        ExpandedWorldDimensions.VerifyPreset(ExpandedWorldServerState.Requested, "LoadWorld");
    }
}
#endif
