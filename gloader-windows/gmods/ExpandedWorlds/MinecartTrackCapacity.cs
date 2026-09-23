#if GLOADER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

/// <summary>
/// Terraria 1.4.5.8 gives TrackGenerator a fixed 4,096-entry path history.
/// LongTrackLength is explicitly WorldWidth-scaled, so THICC 4+ can legitimately
/// request a path beyond that scratch budget. Replace only the constructor's
/// history allocation length. The pathfinder, RNG, requested lengths, obstacle
/// rules, and the source's existing 100-entry tunnel reserve remain untouched.
/// Existing worlds are unaffected because this scratch array exists only during
/// generation and is never serialized.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldMinecartTrackCapacityPatch
{
    private const string TrackGeneratorTypeName = "Terraria.GameContent.Generation.TrackGenerator";

    private static readonly Type TrackGeneratorType =
        AccessTools.TypeByName(TrackGeneratorTypeName)
        ?? throw new TypeLoadException("[Expanded Worlds] " + TrackGeneratorTypeName + " was not found.");

    private static readonly FieldInfo HistoryField = RequireHistoryField();
    private static readonly Type HistoryElementType = HistoryField.FieldType.GetElementType();

    private static readonly MethodInfo CurrentHistoryCapacityMethod =
        AccessTools.Method(typeof(ExpandedWorldMinecartTrackCapacityPatch), nameof(CurrentHistoryCapacity), Type.EmptyTypes)
        ?? throw new MissingMethodException(
            typeof(ExpandedWorldMinecartTrackCapacityPatch).FullName,
            nameof(CurrentHistoryCapacity));

    private static MethodBase TargetMethod()
    {
        ConstructorInfo constructor = TrackGeneratorType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        return constructor
            ?? throw new MissingMethodException(TrackGeneratorType.FullName, ".ctor()");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        for (int i = 2; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Stfld || !Equals(code[i].operand, HistoryField))
                continue;

            if (code[i - 1].opcode != OpCodes.Newarr || !Equals(code[i - 1].operand, HistoryElementType))
            {
                throw new InvalidOperationException(
                    "[Expanded Worlds] TrackGenerator._history constructor shape changed before array allocation.");
            }

            if (code[i - 2].opcode != OpCodes.Ldc_I4 ||
                !(code[i - 2].operand is int vanillaCapacity) ||
                vanillaCapacity != ExpandedWorldMinecartTrackCapacityMath.VanillaHistoryCapacity)
            {
                throw new InvalidOperationException(
                    "[Expanded Worlds] TrackGenerator._history no longer initializes from the audited 4096-entry capacity.");
            }

            code[i - 2].opcode = OpCodes.Call;
            code[i - 2].operand = CurrentHistoryCapacityMethod;
            patched++;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] TrackGenerator._history capacity patch expected exactly one constructor allocation, found " +
                patched + " in " + (__originalMethod?.DeclaringType?.FullName ?? TrackGeneratorTypeName) + ".");
        }

        return code;
    }

    private static int CurrentHistoryCapacity()
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return ExpandedWorldMinecartTrackCapacityMath.VanillaHistoryCapacity;

        int required = ExpandedWorldMinecartTrackCapacityMath.ScratchHistoryCapacity(Main.maxTilesX);
        if (required > ExpandedWorldMinecartTrackCapacityMath.VanillaHistoryCapacity)
        {
            Console.WriteLine(
                "[Expanded Worlds] Expanded minecart TrackGenerator history from " +
                ExpandedWorldMinecartTrackCapacityMath.VanillaHistoryCapacity + " to " + required +
                " for " + Main.maxTilesX + "-tile world width.");
        }

        return required;
    }

    private static FieldInfo RequireHistoryField()
    {
        FieldInfo field = AccessTools.Field(TrackGeneratorType, "_history");
        if (field == null ||
            field.IsStatic ||
            !field.IsInitOnly ||
            !field.FieldType.IsArray ||
            field.FieldType.GetElementType() == null)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + TrackGeneratorTypeName +
                "._history no longer matches the audited readonly instance array shape.");
        }

        return field;
    }
}
#endif
