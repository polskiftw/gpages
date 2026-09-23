#if GLOADER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;

/// <summary>
/// Terraria 1.4.5.8 has several fixed worldgen bookkeeping capacities that the
/// canonical THICC ladder can exceed. Growing these stores changes no count,
/// placement, RNG call, or seed behavior; it only gives Terraria's own formulas
/// enough scratch space to finish at the selected physical dimensions.
/// </summary>
internal static class ExpandedWorldGenerationCapacity
{
    private const string GenVarsTypeName = "Terraria.WorldBuilding.GenVars";

    public static void EnsureForCurrentWorld()
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return;

        Type genVars = AccessTools.TypeByName(GenVarsTypeName)
            ?? throw new TypeLoadException("[Expanded Worlds] Terraria.WorldBuilding.GenVars was not found.");

        EnsureFloatingIslandStorage(genVars);
        EnsureCrimsonHeartStorage();
        EnsureMountainCaveStorage(genVars);
        EnsureSurfaceTunnelStorage(genVars);
        EnsureSurfaceOreTrackingStorage(genVars);
    }

    private static void EnsureFloatingIslandStorage(Type genVars)
    {
        int required = ExpandedWorldCapacityMath.FloatingIslandScratchCapacity(
            Main.maxTilesX,
            ExpandedWorldGenerationContext.ActiveTier);

        // Vanilla arrays contain 300 records. THICC 11 worst-case Error World +
        // Care Bears x10 reaches 890 records.
        EnsureStaticArray(genVars, required, typeof(bool), "skyLake");
        EnsureStaticArray(genVars, required, typeof(int), "floatingIslandHouseX");
        EnsureStaticArray(genVars, required, typeof(int), "floatingIslandHouseY");
        EnsureStaticArray(genVars, required, typeof(int), "floatingIslandStyle");
    }

    private static void EnsureCrimsonHeartStorage()
    {
        int required = ExpandedWorldCapacityMath.CrimsonHeartScratchCapacity(Main.maxTilesX);

        // Vanilla heartPos contains 100 records. THICC 11 Remix worst-case is
        // 232 records (29 region attempts x at most eight CrimVein appends).
        EnsureStaticArray(typeof(WorldGen), required, typeof(Point), "heartPos");
    }

    private static void EnsureMountainCaveStorage(Type genVars)
    {
        int required = ExpandedWorldCapacityMath.MountainCaveScratchCapacity(Main.maxTilesX);

        // Vanilla mCaveX/mCaveY contain 30 records and have no bounds guard at
        // the write site. THICC 11 Remix can request 46 attempts.
        EnsureStaticArray(genVars, required, typeof(int), "mCaveX");
        EnsureStaticArray(genVars, required, typeof(int), "mCaveY");
    }

    private static void EnsureSurfaceTunnelStorage(Type genVars)
    {
        int required = ExpandedWorldCapacityMath.SurfaceTunnelSentinelCapacity(Main.maxTilesX);

        // tunnelX has length 50 and generation stops at maxTunnels - 1. The
        // transpiler below replaces that formerly-unreachable sentinel with the
        // source-derived expanded value; keep the array at the same capacity.
        EnsureStaticArray(genVars, required, typeof(int), "tunnelX");
    }

    private static void EnsureSurfaceOreTrackingStorage(Type genVars)
    {
        int required = ExpandedWorldCapacityMath.SurfaceOrePatchSentinelCapacity(Main.maxTilesX);

        // Surface Ore records successful patches for later spacing checks and
        // stops recording at maxOrePatch - 1. Extend only that tracking sentinel.
        EnsureStaticArray(genVars, required, typeof(int), "orePatchX");
    }

    private static void EnsureStaticArray(Type holder, int requiredLength, Type elementType, string fieldName)
    {
        FieldInfo field = AccessTools.Field(holder, fieldName);
        if (field == null)
            throw new MissingFieldException(holder.FullName, fieldName);

        if (!field.IsStatic || !field.FieldType.IsArray || field.FieldType.GetElementType() != elementType)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Capacity field " + holder.FullName + "." + field.Name +
                " no longer has the audited static " + elementType.Name + "[] shape.");
        }

        Array current = field.GetValue(null) as Array;
        if (current == null)
            throw new InvalidOperationException("[Expanded Worlds] Capacity field " + holder.FullName + "." + field.Name + " is null.");

        if (current.Length >= requiredLength)
            return;

        Array replacement = Array.CreateInstance(elementType, requiredLength);
        Array.Copy(current, replacement, current.Length);
        field.SetValue(null, replacement);

        Console.WriteLine(
            "[Expanded Worlds] Expanded " + holder.FullName + "." + field.Name +
            " scratch capacity from " + current.Length + " to " + requiredLength + ".");
    }
}

/// <summary>
/// clearWorld is late enough that physical dimensions are active and early
/// enough that generation passes have consumed any fixed record array. Client
/// and server share this exact capacity contract.
/// </summary>
[HarmonyPatch(typeof(WorldGen), "clearWorld")]
internal static class ExpandedWorldGenerationCapacityPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        ExpandedWorldGenerationCapacity.EnsureForCurrentWorld();
    }
}

/// <summary>
/// Two 1.4.5.8 tracking guards use static readonly capacity sentinels rather than
/// the backing-array Length: GenVars.maxTunnels and GenVars.maxOrePatch, both 50.
/// At vanilla dimensions the guards are unreachable. THICC 11 can naturally ask
/// for 70 Remix surface tunnels and 74 surface-ore records, so keeping 50 would
/// silently truncate Terraria's own width formulas. Replace only those two field
/// loads with source-derived expanded sentinels while leaving every loop/count
/// roll and RNG call unchanged.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldGenerationTrackingCapacityPatch
{
    private const string GenVarsTypeName = "Terraria.WorldBuilding.GenVars";
    private static readonly Type GenVarsType =
        AccessTools.TypeByName(GenVarsTypeName)
        ?? throw new TypeLoadException("[Expanded Worlds] Terraria.WorldBuilding.GenVars was not found.");

    private static readonly FieldInfo MaxTunnelsField = RequireCapacityField("maxTunnels", 50);
    private static readonly FieldInfo MaxOrePatchField = RequireCapacityField("maxOrePatch", 50);

    private static readonly MethodInfo CurrentTunnelSentinelMethod =
        AccessTools.Method(typeof(ExpandedWorldGenerationTrackingCapacityPatch), nameof(CurrentTunnelSentinel), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(ExpandedWorldGenerationTrackingCapacityPatch).FullName, nameof(CurrentTunnelSentinel));

    private static readonly MethodInfo CurrentOreSentinelMethod =
        AccessTools.Method(typeof(ExpandedWorldGenerationTrackingCapacityPatch), nameof(CurrentOreSentinel), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(ExpandedWorldGenerationTrackingCapacityPatch).FullName, nameof(CurrentOreSentinel));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        MethodBase tunnelMethod = ResolveUniqueFieldConsumer(MaxTunnelsField, "Surface Tunnels");
        MethodBase oreMethod = ResolveUniqueFieldConsumer(MaxOrePatchField, "Surface Ore tracking");
        yield return tunnelMethod;
        yield return oreMethod;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Ldsfld)
                continue;

            if (Equals(code[i].operand, MaxTunnelsField))
            {
                code[i].opcode = OpCodes.Call;
                code[i].operand = CurrentTunnelSentinelMethod;
                patched++;
            }
            else if (Equals(code[i].operand, MaxOrePatchField))
            {
                code[i].opcode = OpCodes.Call;
                code[i].operand = CurrentOreSentinelMethod;
                patched++;
            }
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Tracking-capacity source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (__originalMethod?.Name ?? "<generated>") +
                ": expected exactly one audited capacity-field load, found " + patched + ".");
        }

        return code;
    }

    private static int CurrentTunnelSentinel()
    {
        return ExpandedWorldGenerationContext.IsActive
            ? ExpandedWorldCapacityMath.SurfaceTunnelSentinelCapacity(Main.maxTilesX)
            : 50;
    }

    private static int CurrentOreSentinel()
    {
        return ExpandedWorldGenerationContext.IsActive
            ? ExpandedWorldCapacityMath.SurfaceOrePatchSentinelCapacity(Main.maxTilesX)
            : 50;
    }

    private static FieldInfo RequireCapacityField(string name, int expectedVanillaValue)
    {
        FieldInfo field = AccessTools.Field(GenVarsType, name);
        if (field == null || !field.IsStatic || field.FieldType != typeof(int))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + GenVarsTypeName + "." + name +
                " no longer matches the audited static Int32 capacity field.");
        }

        object raw = field.GetValue(null);
        if (!(raw is int) || (int)raw != expectedVanillaValue)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + GenVarsTypeName + "." + name +
                " changed from audited value " + expectedVanillaValue + " to " + raw + ".");
        }

        return field;
    }

    private static MethodBase ResolveUniqueFieldConsumer(FieldInfo field, string featureName)
    {
        List<MethodBase> matches = EnumerateImplementationMethods(typeof(WorldGen))
            .Where(method => ContainsStaticFieldLoad(method, field))
            .ToList();

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Could not uniquely resolve " + featureName +
                " capacity guard: expected one WorldGen implementation method loading " +
                field.Name + ", found " + matches.Count + ".");
        }

        return matches[0];
    }

    private static IEnumerable<MethodBase> EnumerateImplementationMethods(Type root)
    {
        const BindingFlags methodFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance |
            BindingFlags.DeclaredOnly;

        foreach (MethodInfo method in root.GetMethods(methodFlags))
            yield return method;

        const BindingFlags nestedFlags = BindingFlags.Public | BindingFlags.NonPublic;
        foreach (Type nested in root.GetNestedTypes(nestedFlags))
        {
            foreach (MethodBase method in EnumerateImplementationMethods(nested))
                yield return method;
        }
    }

    private static bool ContainsStaticFieldLoad(MethodBase method, FieldInfo field)
    {
        byte[] il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch
        {
            return false;
        }

        if (il == null || il.Length < 5)
            return false;

        int token = field.MetadataToken;
        for (int i = 0; i + 4 < il.Length; i++)
        {
            // ldsfld <metadata-token>
            if (il[i] == 0x7E && BitConverter.ToInt32(il, i + 1) == token)
                return true;
        }

        return false;
    }
}
#endif
