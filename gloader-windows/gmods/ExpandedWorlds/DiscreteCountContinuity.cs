using System;

#if GLOADER
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
#endif

/// <summary>
/// Exact continuations of Terraria 1.4.5.8 Small/Medium/Large count sequences
/// whose next terms are unambiguous. These stay separate from continuous
/// width/height/area scaling: each source rule is explicitly tier-shaped.
/// </summary>
internal static class ExpandedWorldDiscreteCountMath
{
    public static int GlowTulipCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        // Source Small/Medium/Large: 2, 4, 6.
        return checked(2 * oneBasedWorldTier);
    }

    public static int BoulderPetBaseQuota(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        // Source Small/Medium/Large: 2, 4, 6. The No Traps seed multiplies this
        // base by two afterward, so keep the source multiplier outside this rule.
        return checked(2 * oneBasedWorldTier);
    }

    public static IntRange SpikeCaveCountRange(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        // Source base Small/Medium/Large: 3, 5, 7, then + Next(2).
        int minimum = checked(2 * oneBasedWorldTier + 1);
        return new IntRange(minimum, checked(minimum + 1));
    }

    public static int ChilletEggCount(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        // Source Small/Medium/Large: 6, 9, 12.
        return checked(3 * oneBasedWorldTier + 3);
    }
}

#if GLOADER
internal static class ExpandedWorldDiscreteCountPatchUtil
{
    internal static readonly MethodInfo GetWorldSizeMethod =
        AccessTools.Method(typeof(WorldGen), nameof(WorldGen.GetWorldSize), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(WorldGen).FullName, nameof(WorldGen.GetWorldSize));

    internal static IEnumerable<CodeInstruction> InjectAfterTierSwitch(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        MethodInfo adjustMethod,
        string featureName,
        int[] expectedConstants,
        bool requireGetWorldSizeCall)
    {
        var code = instructions.ToList();
        int searchStart = 0;

        if (requireGetWorldSizeCall)
        {
            searchStart = code.FindIndex(CallsGetWorldSize);
            if (searchStart < 0)
            {
                throw new InvalidOperationException(
                    "[Expanded Worlds] " + featureName +
                    " no longer calls WorldGen.GetWorldSize. Refusing to infer a changed tier rule.");
            }
        }

        bool[] seen = new bool[expectedConstants.Length];
        int patched = 0;

        for (int i = searchStart; i < code.Count; i++)
        {
            for (int c = 0; c < expectedConstants.Length; c++)
            {
                if (IsIntConstant(code[i], expectedConstants[c]))
                    seen[c] = true;
            }

            if (!AllSeen(seen) || !IsLocalStore(code[i]))
                continue;

            var adjust = new CodeInstruction(OpCodes.Call, adjustMethod);
            adjust.labels.AddRange(code[i].labels);
            code[i].labels.Clear();
            adjust.blocks.AddRange(code[i].blocks);
            code[i].blocks.Clear();
            code.Insert(i, adjust);
            patched++;
            break;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + featureName + " tier-count source shape changed in " +
                (original?.DeclaringType?.FullName ?? "WorldGen") + "." +
                (original?.Name ?? "<unknown>") +
                ". Refusing to guess against this Terraria build.");
        }

        return code;
    }

    internal static int ExpandedTierOrLarge(int vanillaLargeCount, int expectedLargeCount, Func<int, int> countForTier, string featureName)
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return vanillaLargeCount;

        if (vanillaLargeCount != expectedLargeCount)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected Terraria Large " + featureName + " count " +
                expectedLargeCount + ", got " + vanillaLargeCount + ". Refusing to guess.");
        }

        return countForTier(ExpandedWorldGenerationContext.ActiveTier);
    }

    private static bool CallsGetWorldSize(CodeInstruction instruction)
    {
        return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
               Equals(instruction.operand, GetWorldSizeMethod);
    }

    private static bool AllSeen(bool[] seen)
    {
        for (int i = 0; i < seen.Length; i++)
        {
            if (!seen[i])
                return false;
        }
        return true;
    }

    private static bool IsLocalStore(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Stloc_0 ||
               instruction.opcode == OpCodes.Stloc_1 ||
               instruction.opcode == OpCodes.Stloc_2 ||
               instruction.opcode == OpCodes.Stloc_3 ||
               instruction.opcode == OpCodes.Stloc ||
               instruction.opcode == OpCodes.Stloc_S;
    }

    private static bool IsIntConstant(CodeInstruction instruction, int expected)
    {
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int)
            return (int)instruction.operand == expected;
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte)
            return (sbyte)instruction.operand == expected;
        if (expected == -1 && instruction.opcode == OpCodes.Ldc_I4_M1) return true;
        if (expected == 0 && instruction.opcode == OpCodes.Ldc_I4_0) return true;
        if (expected == 1 && instruction.opcode == OpCodes.Ldc_I4_1) return true;
        if (expected == 2 && instruction.opcode == OpCodes.Ldc_I4_2) return true;
        if (expected == 3 && instruction.opcode == OpCodes.Ldc_I4_3) return true;
        if (expected == 4 && instruction.opcode == OpCodes.Ldc_I4_4) return true;
        if (expected == 5 && instruction.opcode == OpCodes.Ldc_I4_5) return true;
        if (expected == 6 && instruction.opcode == OpCodes.Ldc_I4_6) return true;
        if (expected == 7 && instruction.opcode == OpCodes.Ldc_I4_7) return true;
        if (expected == 8 && instruction.opcode == OpCodes.Ldc_I4_8) return true;
        return false;
    }
}

/// <summary>
/// GrowGlowTulips hard-stops at Terraria's Large branch (2/4/6). Continue that
/// exact +2-per-tier count through the full THICC ladder.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldGlowTulipCountPatch
{
    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldGlowTulipCountPatch), nameof(AdjustCount))
        ?? throw new MissingMethodException(typeof(ExpandedWorldGlowTulipCountPatch).FullName, nameof(AdjustCount));

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(WorldGen), "GrowGlowTulips", Type.EmptyTypes);
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "GrowGlowTulips");
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return ExpandedWorldDiscreteCountPatchUtil.InjectAfterTierSwitch(
            instructions,
            __originalMethod,
            AdjustMethod,
            "Glow Tulip",
            new[] { 2, 4, 6 },
            requireGetWorldSizeCall: false);
    }

    private static int AdjustCount(int vanillaCount)
    {
        return ExpandedWorldDiscreteCountPatchUtil.ExpandedTierOrLarge(
            vanillaCount,
            6,
            ExpandedWorldDiscreteCountMath.GlowTulipCount,
            "Glow Tulip");
    }
}

/// <summary>
/// placeTrap uses a 2/4/6 Small/Medium/Large cap for the rare Boulder Pet trap
/// variant. No Traps doubles the source cap after this switch, so only the base
/// is continued through the full THICC ladder.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldBoulderPetQuotaPatch
{
    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldBoulderPetQuotaPatch), nameof(AdjustBaseQuota))
        ?? throw new MissingMethodException(typeof(ExpandedWorldBoulderPetQuotaPatch).FullName, nameof(AdjustBaseQuota));

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(WorldGen), "placeTrap");
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "placeTrap");
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return ExpandedWorldDiscreteCountPatchUtil.InjectAfterTierSwitch(
            instructions,
            __originalMethod,
            AdjustMethod,
            "Boulder Pet base quota",
            new[] { 2, 4, 6 },
            requireGetWorldSizeCall: true);
    }

    private static int AdjustBaseQuota(int vanillaCount)
    {
        return ExpandedWorldDiscreteCountPatchUtil.ExpandedTierOrLarge(
            vanillaCount,
            6,
            ExpandedWorldDiscreteCountMath.BoulderPetBaseQuota,
            "Boulder Pet base quota");
    }
}

/// <summary>
/// AddSpikeCaves uses the explicit Small/Medium/Large base sequence 3/5/7 and
/// then performs vanilla's +Next(2). Only the base continues into THICC tiers.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldSpikeCaveCountPatch
{
    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldSpikeCaveCountPatch), nameof(AdjustBaseCount))
        ?? throw new MissingMethodException(typeof(ExpandedWorldSpikeCaveCountPatch).FullName, nameof(AdjustBaseCount));

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(WorldGen), "AddSpikeCaves");
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "AddSpikeCaves");
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return ExpandedWorldDiscreteCountPatchUtil.InjectAfterTierSwitch(
            instructions,
            __originalMethod,
            AdjustMethod,
            "Spike Cave",
            new[] { 3, 5, 7 },
            requireGetWorldSizeCall: true);
    }

    private static int AdjustBaseCount(int vanillaCount)
    {
        return ExpandedWorldDiscreteCountPatchUtil.ExpandedTierOrLarge(
            vanillaCount,
            7,
            tier => ExpandedWorldDiscreteCountMath.SpikeCaveCountRange(tier).Minimum,
            "Spike Cave base");
    }
}

/// <summary>
/// PlaceChilletEggs uses 6/9/12 by Terraria size. Continue its exact +3 sequence
/// through the full THICC ladder while leaving placement search, spacing, Remix
/// depth, and RNG intact.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldChilletEggCountPatch
{
    private static readonly MethodInfo AdjustMethod =
        AccessTools.Method(typeof(ExpandedWorldChilletEggCountPatch), nameof(AdjustCount))
        ?? throw new MissingMethodException(typeof(ExpandedWorldChilletEggCountPatch).FullName, nameof(AdjustCount));

    private static MethodBase TargetMethod()
    {
        MethodBase method = AccessTools.Method(typeof(WorldGen), "PlaceChilletEggs", Type.EmptyTypes);
        if (method == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "PlaceChilletEggs");
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        return ExpandedWorldDiscreteCountPatchUtil.InjectAfterTierSwitch(
            instructions,
            __originalMethod,
            AdjustMethod,
            "Chillet Egg",
            new[] { 6, 9, 12 },
            requireGetWorldSizeCall: true);
    }

    private static int AdjustCount(int vanillaCount)
    {
        return ExpandedWorldDiscreteCountPatchUtil.ExpandedTierOrLarge(
            vanillaCount,
            12,
            ExpandedWorldDiscreteCountMath.ChilletEggCount,
            "Chillet Egg");
    }
}
#endif
