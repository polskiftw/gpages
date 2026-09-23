#if GLOADER_CLIENT
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

/// <summary>
/// Terraria 1.4.5.8's fullscreen M-key map has a separate decorative map-frame
/// texture path in Main.DrawMap. Retail only scales that texture to world size
/// when maxTilesX is exactly vanilla Large (8,400); wider worlds therefore keep
/// the raw ~928x248 texture-sized black panel even though MapRenderer itself is
/// drawing the expanded world correctly.
///
/// Expanded Worlds continues Large's regular world proportions, so extend the
/// existing retail Large-frame path to every world at least Large width. Small,
/// Medium and vanilla Large retain their exact retail behavior; THICC and above
/// now use the same width-derived frame scaling instead of the fixed-size panel.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldFullscreenMapFrameScalingPatch
{
    private static readonly FieldInfo MaxTilesXField =
        AccessTools.Field(typeof(Main), nameof(Main.maxTilesX));

    private static MethodBase TargetMethod()
    {
        MethodInfo found = null;
        MethodInfo[] methods = typeof(Main).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        for (int i = 0; i < methods.Length; i++)
        {
            if (!string.Equals(methods[i].Name, "DrawMap", StringComparison.Ordinal))
                continue;

            if (found != null)
            {
                throw new InvalidOperationException(
                    "[Expanded Worlds] Terraria.Main.DrawMap is overloaded; the audited 1.4.5.8 method shape changed.");
            }

            found = methods[i];
        }

        if (found == null)
            throw new MissingMethodException(typeof(Main).FullName, "DrawMap");

        return found;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        if (MaxTilesXField == null)
            throw new MissingFieldException(typeof(Main).FullName, nameof(Main.maxTilesX));

        var code = new List<CodeInstruction>(instructions);
        int patched = 0;

        // Exact 1.4.5.8 Main.DrawMap contains one Large-only gate:
        //     if (maxTilesX == 8400) { ...scale TextureAssets.Map... }
        // C# emits this as maxTilesX, 8400, bne.un(.s) -> after the block.
        // Change only the skip comparison to signed "less than" so the same
        // retail block executes for Large and wider worlds.
        for (int i = 0; i + 2 < code.Count; i++)
        {
            CodeInstruction widthLoad = code[i];
            CodeInstruction largeWidth = code[i + 1];
            CodeInstruction skipBranch = code[i + 2];

            if (widthLoad.opcode != OpCodes.Ldsfld ||
                !Equals(widthLoad.operand, MaxTilesXField) ||
                !IsIntConstant(largeWidth, ExpandedWorldMath.LargeWidth))
            {
                continue;
            }

            if (skipBranch.opcode == OpCodes.Bne_Un_S)
            {
                skipBranch.opcode = OpCodes.Blt_S;
                patched++;
            }
            else if (skipBranch.opcode == OpCodes.Bne_Un)
            {
                skipBranch.opcode = OpCodes.Blt;
                patched++;
            }
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Main.DrawMap fullscreen-frame source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "Terraria.Main") +
                ": expected exactly one maxTilesX == 8400 branch, patched " + patched + ".");
        }

        return code;
    }

    private static bool IsIntConstant(CodeInstruction instruction, int expected)
    {
        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int value)
            return value == expected;
        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte shortValue)
            return shortValue == expected;

        return false;
    }
}
#endif
