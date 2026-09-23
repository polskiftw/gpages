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
/// Exact continuations of unambiguous Small/Medium/Large sequences introduced
/// by Terraria 1.4.5.8's Dual Dungeon generator. Ambiguous sequences are
/// deliberately not extended: when the source does not define one clear next
/// term, Expanded Worlds leaves Terraria's Large value untouched rather than
/// inventing a rule.
/// </summary>
internal static class ExpandedWorldDungeonTierMath
{
    public static int BookshelfMinimum(int tier) => PositiveTier(tier, checked(5 * tier));
    public static int WaterCandleMinimum(int tier) => PositiveTier(tier, checked(5 * tier));

    // Early Dual Dungeon Features source sequences.
    public static int EarlyAltarCount(int tier) => PositiveTier(tier, checked(10 * tier + 10));       // 20,30,40
    public static int EarlyDesertDropTrapCount(int tier) => PositiveTier(tier, checked(4 * tier + 4)); // 8,12,16
    public static int EarlySnowDropTrapCount(int tier) => PositiveTier(tier, checked(4 * tier + 2));   // 6,10,14
    public static int EarlyCavernDropTrapCount(int tier) => PositiveTier(tier, checked(2 * tier + 2)); // 4,6,8
    public static int EarlyPitTrapCount(int tier) => PositiveTier(tier, checked(4 * tier));             // 4,8,12
    public static int EarlyBiomeClumpCount(int tier) => PositiveTier(tier, checked(20 * tier + 20));   // 40,60,80
    public static int EarlyFloodedPitQuota(int tier) => PositiveTier(tier, checked(2 * tier));          // 2,4,6

    // DualDungeonLayoutProvider.ConvertSpecializedRooms source sequences.
    public static int SpecializedShimmerRoomCount(int tier) => PositiveTier(tier, checked(2 * tier));       // 2,4,6
    public static int SpecializedLivingTreeRoomCount(int tier) => PositiveTier(tier, checked(4 * tier - 2)); // 2,6,10
    public static int SpecializedMahoganyRoomCount(int tier) => PositiveTier(tier, checked(4 * tier - 2));   // 2,6,10
    public static int SpecializedBeehiveRoomCount(int tier) => PositiveTier(tier, checked(3 * tier + 2));    // 5,8,11
    public static int SpecializedCrystalRoomCount(int tier) => PositiveTier(tier, checked(4 * tier + 2));    // 6,10,14
    public static int SpecializedHallCount(int tier) => PositiveTier(tier, checked(tier + 2));               // 3,4,5

    // DungeonGlobalTraps source: 30+Next(11), 50+Next(16), 70+Next(21).
    public static int TempleTrapBase(int tier) => PositiveTier(tier, checked(20 * tier + 10));
    public static int TempleTrapRandomExclusive(int tier) => PositiveTier(tier, checked(5 * tier + 6));

    private static int PositiveTier(int tier, int value)
    {
        if (tier < 1)
            throw new ArgumentOutOfRangeException(nameof(tier));
        return value;
    }
}

#if GLOADER
internal static class ExpandedWorldDungeonTierPatchUtil
{
    internal static readonly MethodInfo GetWorldSizeMethod =
        AccessTools.Method(typeof(WorldGen), nameof(WorldGen.GetWorldSize), Type.EmptyTypes)
        ?? throw new MissingMethodException(typeof(WorldGen).FullName, nameof(WorldGen.GetWorldSize));

    internal static Type RequireType(string fullName)
    {
        return AccessTools.TypeByName(fullName)
            ?? throw new TypeLoadException("[Expanded Worlds] Required Terraria type not found: " + fullName);
    }

    internal static MethodBase RequireMethod(string typeName, string methodName)
    {
        Type type = RequireType(typeName);
        MethodBase method = AccessTools.GetDeclaredMethods(type)
            .SingleOrDefault(candidate => candidate.Name == methodName);
        if (method == null)
            throw new MissingMethodException(type.FullName, methodName);
        return method;
    }

    internal static MethodInfo RequireOwnMethod(Type type, string methodName)
    {
        MethodInfo method = AccessTools.Method(type, methodName);
        if (method == null)
            throw new MissingMethodException(type.FullName, methodName);
        return method;
    }

    internal static int ContinueLarge(int vanillaValue, int expectedLarge, Func<int, int> continuation, string feature)
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return vanillaValue;

        if (vanillaValue != expectedLarge)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Expected Terraria Large " + feature + " value " +
                expectedLarge + ", got " + vanillaValue + ". Refusing to guess.");
        }

        return continuation(ExpandedWorldGenerationContext.ActiveTier);
    }

    internal static IEnumerable<CodeInstruction> InjectAfterTierSwitch(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        MethodInfo adjustMethod,
        string featureName,
        int[] expectedConstants,
        int worldSizeCallOccurrence = 1)
    {
        var code = instructions.ToList();
        int searchStart = FindNthWorldSizeCall(code, worldSizeCallOccurrence);
        if (searchStart < 0)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + featureName + " no longer contains the audited WorldGen.GetWorldSize call.");
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

            InsertCallBeforeStore(code, i, adjustMethod);
            patched++;
            break;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + featureName + " source shape changed in " +
                (original?.DeclaringType?.FullName ?? "<unknown>") + "." +
                (original?.Name ?? "<unknown>") + ". Refusing to guess.");
        }

        return code;
    }

    internal static IEnumerable<CodeInstruction> PatchLargeAssignmentSequence(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        string featureName,
        int[] expectedLargeValues,
        MethodInfo[] adjustMethods)
    {
        if (expectedLargeValues == null || adjustMethods == null || expectedLargeValues.Length != adjustMethods.Length)
            throw new ArgumentException("Large assignment sequence and adjustment list must have equal lengths.");

        var code = instructions.ToList();
        var matches = new List<int[]>();

        for (int start = 0; start < code.Count - 1; start++)
        {
            if (!IsIntConstant(code[start], expectedLargeValues[0]) || !IsLocalStore(code[start + 1]))
                continue;

            var constants = new int[expectedLargeValues.Length];
            constants[0] = start;
            int cursor = start + 2;
            bool complete = true;

            for (int valueIndex = 1; valueIndex < expectedLargeValues.Length; valueIndex++)
            {
                if (cursor + 1 >= code.Count ||
                    !IsIntConstant(code[cursor], expectedLargeValues[valueIndex]) ||
                    !IsLocalStore(code[cursor + 1]))
                {
                    complete = false;
                    break;
                }

                constants[valueIndex] = cursor;
                cursor += 2;
            }

            if (complete)
                matches.Add(constants);
        }

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + featureName + " Large-case assignment shape changed in " +
                (original?.DeclaringType?.FullName ?? "<unknown>") + "." +
                (original?.Name ?? "<unknown>") + ": expected exactly one sequence, found " +
                matches.Count + ". Refusing to guess.");
        }

        int[] positions = matches[0];
        for (int i = positions.Length - 1; i >= 0; i--)
        {
            MethodInfo adjust = adjustMethods[i];
            if (adjust == null)
                continue;

            // The constant leaves the assigned Int32 on the evaluation stack.
            // Calling Int32 -> Int32 here changes only the Large branch value
            // before its existing stloc, preserving all branch/RNG structure.
            var call = new CodeInstruction(OpCodes.Call, adjust);
            int insertAt = positions[i] + 1;
            code.Insert(insertAt, call);
        }

        return code;
    }

    internal static IEnumerable<CodeInstruction> PatchTrapLargeCase(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original,
        MethodInfo adjustBase,
        MethodInfo adjustRandomExclusive)
    {
        var code = instructions.ToList();
        if (FindNthWorldSizeCall(code, 1) < 0)
            throw new InvalidOperationException("[Expanded Worlds] Dual Dungeon traps no longer call WorldGen.GetWorldSize.");

        // Validate all three exact source branches before touching the Large one.
        int[] required = { 30, 11, 50, 16, 70, 21 };
        for (int i = 0; i < required.Length; i++)
        {
            if (code.Count(instruction => IsIntConstant(instruction, required[i])) < 1)
            {
                throw new InvalidOperationException(
                    "[Expanded Worlds] Dual Dungeon trap source no longer contains audited constant " + required[i] + ".");
            }
        }

        var basePositions = code
            .Select((instruction, index) => new { instruction, index })
            .Where(item => IsIntConstant(item.instruction, 70))
            .Select(item => item.index)
            .ToArray();
        var randomPositions = code
            .Select((instruction, index) => new { instruction, index })
            .Where(item => IsIntConstant(item.instruction, 21))
            .Select(item => item.index)
            .ToArray();

        if (basePositions.Length != 1 || randomPositions.Length != 1 || randomPositions[0] <= basePositions[0])
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Dual Dungeon trap Large branch no longer has one 70 + Next(21) shape in " +
                (original?.DeclaringType?.FullName ?? "<unknown>") + "." + (original?.Name ?? "<unknown>") + ".");
        }

        // Insert from right to left so the first position remains valid.
        code.Insert(randomPositions[0] + 1, new CodeInstruction(OpCodes.Call, adjustRandomExclusive));
        code.Insert(basePositions[0] + 1, new CodeInstruction(OpCodes.Call, adjustBase));
        return code;
    }

    private static int FindNthWorldSizeCall(List<CodeInstruction> code, int occurrence)
    {
        int seen = 0;
        for (int i = 0; i < code.Count; i++)
        {
            if (!Calls(code[i], GetWorldSizeMethod))
                continue;
            if (++seen == occurrence)
                return i;
        }
        return -1;
    }

    private static void InsertCallBeforeStore(List<CodeInstruction> code, int storeIndex, MethodInfo adjustMethod)
    {
        var call = new CodeInstruction(OpCodes.Call, adjustMethod);
        call.labels.AddRange(code[storeIndex].labels);
        code[storeIndex].labels.Clear();
        call.blocks.AddRange(code[storeIndex].blocks);
        code[storeIndex].blocks.Clear();
        code.Insert(storeIndex, call);
    }

    private static bool AllSeen(bool[] values)
    {
        for (int i = 0; i < values.Length; i++)
            if (!values[i]) return false;
        return true;
    }

    private static bool Calls(CodeInstruction instruction, MethodInfo method)
    {
        return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
               Equals(instruction.operand, method);
    }

    internal static bool IsLocalStore(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Stloc_0 ||
               instruction.opcode == OpCodes.Stloc_1 ||
               instruction.opcode == OpCodes.Stloc_2 ||
               instruction.opcode == OpCodes.Stloc_3 ||
               instruction.opcode == OpCodes.Stloc ||
               instruction.opcode == OpCodes.Stloc_S;
    }

    internal static bool IsIntConstant(CodeInstruction instruction, int expected)
    {
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int) return (int)instruction.operand == expected;
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte) return (sbyte)instruction.operand == expected;
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

[HarmonyPatch]
internal static class ExpandedWorldDualDungeonBookshelfPatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.Features.DungeonGlobalBookshelves";
    private static readonly MethodInfo Adjust = ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldDualDungeonBookshelfPatch), nameof(AdjustCount));
    private static MethodBase TargetMethod() => ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "Bookshelves");

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        ExpandedWorldDungeonTierPatchUtil.InjectAfterTierSwitch(instructions, original, Adjust, "Dual Dungeon bookshelf minimum", new[] { 5, 10, 15 });

    private static int AdjustCount(int value) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(value, 15, ExpandedWorldDungeonTierMath.BookshelfMinimum, "Dual Dungeon bookshelf minimum");
}

[HarmonyPatch]
internal static class ExpandedWorldDualDungeonGroundFurniturePatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.Features.DungeonGlobalGroundFurniture";
    private static readonly MethodInfo Adjust = ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldDualDungeonGroundFurniturePatch), nameof(AdjustCount));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "GroundFurniture_DualDungeons");
        yield return ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "GroundFurniture");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        ExpandedWorldDungeonTierPatchUtil.InjectAfterTierSwitch(instructions, original, Adjust, "Dual Dungeon water-candle minimum", new[] { 5, 10, 15 });

    private static int AdjustCount(int value) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(value, 15, ExpandedWorldDungeonTierMath.WaterCandleMinimum, "Dual Dungeon water-candle minimum");
}

[HarmonyPatch]
internal static class ExpandedWorldEarlyDualDungeonFeaturePatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.Features.DungeonGlobalEarlyDualDungeonFeatures";

    private static readonly MethodInfo AdjustAltar = Method(nameof(AdjustAltarCount));
    private static readonly MethodInfo AdjustDesert = Method(nameof(AdjustDesertDropTrapCount));
    private static readonly MethodInfo AdjustSnow = Method(nameof(AdjustSnowDropTrapCount));
    private static readonly MethodInfo AdjustCavern = Method(nameof(AdjustCavernDropTrapCount));
    private static readonly MethodInfo AdjustPit = Method(nameof(AdjustPitTrapCount));
    private static readonly MethodInfo AdjustClump = Method(nameof(AdjustBiomeClumpCount));
    private static readonly MethodInfo AdjustFlooded = Method(nameof(AdjustFloodedPitQuota));

    private static MethodBase TargetMethod() => ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "EarlyDungeonFeatures");

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        IEnumerable<CodeInstruction> code = ExpandedWorldDungeonTierPatchUtil.PatchLargeAssignmentSequence(
            instructions,
            original,
            "Early Dual Dungeon feature quotas",
            new[] { 40, 18, 16, 14, 8, 12, 80, 80 },
            new[] { AdjustAltar, null, AdjustDesert, AdjustSnow, AdjustCavern, AdjustPit, AdjustClump, AdjustClump });

        // The second GetWorldSize switch in the same method is the explicit
        // flooded-pit quota 2/4/6. The first is the eight-value block above.
        return ExpandedWorldDungeonTierPatchUtil.InjectAfterTierSwitch(
            code,
            original,
            AdjustFlooded,
            "Early Dual Dungeon flooded-pit quota",
            new[] { 2, 4, 6 },
            worldSizeCallOccurrence: 2);
    }

    private static MethodInfo Method(string name) =>
        ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldEarlyDualDungeonFeaturePatch), name);

    private static int AdjustAltarCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 40, ExpandedWorldDungeonTierMath.EarlyAltarCount, "Early Dual Dungeon altar count");
    private static int AdjustDesertDropTrapCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 16, ExpandedWorldDungeonTierMath.EarlyDesertDropTrapCount, "Early Dual Dungeon desert drop-trap count");
    private static int AdjustSnowDropTrapCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 14, ExpandedWorldDungeonTierMath.EarlySnowDropTrapCount, "Early Dual Dungeon snow drop-trap count");
    private static int AdjustCavernDropTrapCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 8, ExpandedWorldDungeonTierMath.EarlyCavernDropTrapCount, "Early Dual Dungeon cavern drop-trap count");
    private static int AdjustPitTrapCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 12, ExpandedWorldDungeonTierMath.EarlyPitTrapCount, "Early Dual Dungeon pit-trap count");
    private static int AdjustBiomeClumpCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 80, ExpandedWorldDungeonTierMath.EarlyBiomeClumpCount, "Early Dual Dungeon biome-clump count");
    private static int AdjustFloodedPitQuota(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 6, ExpandedWorldDungeonTierMath.EarlyFloodedPitQuota, "Early Dual Dungeon flooded-pit quota");
}

[HarmonyPatch]
internal static class ExpandedWorldDualDungeonSpecializedRoomsPatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.LayoutProviders.DualDungeonLayoutProvider";
    private static readonly MethodInfo AdjustShimmer = Method(nameof(AdjustShimmerCount));
    private static readonly MethodInfo AdjustLivingTree = Method(nameof(AdjustLivingTreeCount));
    private static readonly MethodInfo AdjustMahogany = Method(nameof(AdjustMahoganyCount));
    private static readonly MethodInfo AdjustBeehive = Method(nameof(AdjustBeehiveCount));
    private static readonly MethodInfo AdjustCrystal = Method(nameof(AdjustCrystalCount));

    private static MethodBase TargetMethod() => ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "ConvertSpecializedRooms");

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        ExpandedWorldDungeonTierPatchUtil.PatchLargeAssignmentSequence(
            instructions,
            original,
            "Dual Dungeon specialized-room quotas",
            new[] { 6, 10, 10, 8, 11, 14 },
            new[] { AdjustShimmer, AdjustLivingTree, AdjustMahogany, null, AdjustBeehive, AdjustCrystal });

    private static MethodInfo Method(string name) =>
        ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldDualDungeonSpecializedRoomsPatch), name);

    private static int AdjustShimmerCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 6, ExpandedWorldDungeonTierMath.SpecializedShimmerRoomCount, "Dual Dungeon Shimmer-room quota");
    private static int AdjustLivingTreeCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 10, ExpandedWorldDungeonTierMath.SpecializedLivingTreeRoomCount, "Dual Dungeon Living Tree room quota");
    private static int AdjustMahoganyCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 10, ExpandedWorldDungeonTierMath.SpecializedMahoganyRoomCount, "Dual Dungeon Living Mahogany room quota");
    private static int AdjustBeehiveCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 11, ExpandedWorldDungeonTierMath.SpecializedBeehiveRoomCount, "Dual Dungeon Beehive room quota");
    private static int AdjustCrystalCount(int v) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(v, 14, ExpandedWorldDungeonTierMath.SpecializedCrystalRoomCount, "Dual Dungeon Crystal room quota");
}

[HarmonyPatch]
internal static class ExpandedWorldDualDungeonSpecializedHallsPatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.LayoutProviders.DualDungeonLayoutProvider";
    private static readonly MethodInfo Adjust = ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldDualDungeonSpecializedHallsPatch), nameof(AdjustCount));
    private static MethodBase TargetMethod() => ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "ConvertSpecializedHalls");

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        ExpandedWorldDungeonTierPatchUtil.InjectAfterTierSwitch(instructions, original, Adjust, "Dual Dungeon specialized-hall quota", new[] { 3, 4, 5 });

    private static int AdjustCount(int value) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(value, 5, ExpandedWorldDungeonTierMath.SpecializedHallCount, "Dual Dungeon specialized-hall quota");
}

[HarmonyPatch]
internal static class ExpandedWorldDualDungeonTrapCountPatch
{
    private const string TypeName = "Terraria.GameContent.Generation.Dungeon.Features.DungeonGlobalTraps";
    private static readonly MethodInfo AdjustBase = ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldDualDungeonTrapCountPatch), nameof(AdjustBaseCount));
    private static readonly MethodInfo AdjustRandom = ExpandedWorldDungeonTierPatchUtil.RequireOwnMethod(typeof(ExpandedWorldDualDungeonTrapCountPatch), nameof(AdjustRandomExclusive));
    private static MethodBase TargetMethod() => ExpandedWorldDungeonTierPatchUtil.RequireMethod(TypeName, "Traps");

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        ExpandedWorldDungeonTierPatchUtil.PatchTrapLargeCase(instructions, original, AdjustBase, AdjustRandom);

    private static int AdjustBaseCount(int value) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(value, 70, ExpandedWorldDungeonTierMath.TempleTrapBase, "Dual Dungeon temple-trap base");
    private static int AdjustRandomExclusive(int value) => ExpandedWorldDungeonTierPatchUtil.ContinueLarge(value, 21, ExpandedWorldDungeonTierMath.TempleTrapRandomExclusive, "Dual Dungeon temple-trap random range");
}
#endif
