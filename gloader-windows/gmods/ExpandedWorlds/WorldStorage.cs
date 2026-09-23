#if GLOADER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

/// <summary>
/// Terraria 1.4.5.8 keeps several world-sized backing arrays at the dimensions
/// that existed during process startup. Changing Main.maxTilesX/maxTilesY is
/// therefore not sufficient by itself for a world wider or taller than vanilla
/// Large.
///
/// This support is intentionally shared by GLOADER_CLIENT and GLOADER_SERVER:
/// a client needs it for generation, local reload and joining an expanded
/// server; a Host & Play / dedicated-server process needs it while loading the
/// expanded .wld before accepting players.
/// </summary>
internal static class ExpandedWorldBackingStorage
{
    private const string ActiveSectionsTypeName = "Terraria.DataStructures.ActiveSections";
    private const string LeashedEntityTypeName = "Terraria.GameContent.LeashedEntity";
    private const string NetplayTypeName = "Terraria.Netplay";

    private static readonly FieldInfo MainMapField = AccessTools.Field(typeof(Main), "Map");

    public static bool IsSupportedExpandedWorld(int width, int height)
    {
        return ExpandedWorldMath.IsExpandedPresetDimensions(width, height);
    }

    public static int RequiredBackingWidth(int logicalWidth)
    {
        if (logicalWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        return logicalWidth;
    }

    public static int RequiredBackingHeight(int logicalHeight)
    {
        if (logicalHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        return logicalHeight;
    }

    public static int RequiredSectionColumns(int logicalWidth)
    {
        return checked(logicalWidth / ExpandedWorldMath.SectionWidth + 1);
    }

    public static int RequiredSectionRows(int logicalHeight)
    {
        return checked(logicalHeight / ExpandedWorldMath.SectionHeight + 1);
    }

    public static void EnsureForCurrentDimensions(string stage)
    {
        int width = Main.maxTilesX;
        int height = Main.maxTilesY;

        if (!IsSupportedExpandedWorld(width, height))
            return;

        int requiredWidth = RequiredBackingWidth(width);
        int requiredHeight = RequiredBackingHeight(height);
        long tileArea = ExpandedWorldMath.TileArea(requiredWidth, requiredHeight);

        EnsureTileStorage(requiredWidth, requiredHeight, tileArea, stage);
        EnsureRemoteClientSectionStorage(width, height, stage);

#if GLOADER_CLIENT
        // WorldMap owns a fixed MapTile[,] and exposes no resize operation in
        // exact 1.4.5.8. Use reflection so this shared file does not introduce
        // a client-only Terraria.Map compile-time dependency into the server mod.
        EnsureClientMapStorage(requiredWidth, requiredHeight, stage);
#endif
    }

    private static void EnsureTileStorage(int requiredWidth, int requiredHeight, long tileArea, string stage)
    {
        Tile[,] current = Main.tile;
        if (current != null &&
            current.GetLength(0) >= requiredWidth &&
            current.GetLength(1) >= requiredHeight)
        {
            return;
        }

        // clearWorld is about to discard/clear the previous world's tile state,
        // so copying the old canvas is both unnecessary and extremely expensive.
        // The checked Int64 area calculation above keeps the capacity arithmetic
        // valid even though THICC 11 contains 284,400,000 logical tiles.
        Main.tile = new Tile[requiredWidth, requiredHeight];

        Console.WriteLine(
            "[Expanded Worlds] " + stage + ": tile backing storage enlarged to " +
            requiredWidth + "x" + requiredHeight + " (" + tileArea.ToString("N0") + " tiles)." );
    }

    private static void EnsureRemoteClientSectionStorage(int logicalWidth, int logicalHeight, string stage)
    {
        Type netplayType = AccessTools.TypeByName(NetplayTypeName);
        if (netplayType == null)
            throw new TypeLoadException("[Expanded Worlds] Terraria.Netplay was not found.");

        FieldInfo clientsField = AccessTools.Field(netplayType, "Clients");
        if (clientsField == null || !clientsField.IsStatic || !clientsField.FieldType.IsArray)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Terraria.Netplay.Clients no longer matches the audited static array shape.");
        }

        Type remoteClientType = clientsField.FieldType.GetElementType();
        if (remoteClientType == null)
            throw new InvalidOperationException("[Expanded Worlds] Could not resolve Terraria.RemoteClient from Netplay.Clients.");

        FieldInfo sectionsField = AccessTools.Field(remoteClientType, "TileSections");
        FieldInfo sectionTimesField = AccessTools.Field(remoteClientType, "TileSectionsCheckTime");
        if (sectionsField == null || sectionsField.IsStatic || sectionsField.FieldType != typeof(bool[,]) ||
            sectionTimesField == null || sectionTimesField.IsStatic || sectionTimesField.FieldType != typeof(uint[,]))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Terraria.RemoteClient section fields no longer match the audited bool[,] / uint[,] shapes.");
        }

        Array clients = clientsField.GetValue(null) as Array;
        if (clients == null)
            throw new InvalidOperationException("[Expanded Worlds] Terraria.Netplay.Clients is null.");

        int requiredColumns = RequiredSectionColumns(logicalWidth);
        int requiredRows = RequiredSectionRows(logicalHeight);
        int resized = 0;

        for (int i = 0; i < clients.Length; i++)
        {
            object client = clients.GetValue(i);
            if (client == null)
                continue;

            bool[,] sections = sectionsField.GetValue(client) as bool[,];
            uint[,] sectionTimes = sectionTimesField.GetValue(client) as uint[,];
            if (sections == null || sectionTimes == null)
            {
                throw new InvalidOperationException(
                    "[Expanded Worlds] Terraria.RemoteClient section storage is unexpectedly null for client slot " + i + ".");
            }

            bool sectionsTooSmall =
                sections.GetLength(0) < requiredColumns || sections.GetLength(1) < requiredRows;
            bool timesTooSmall =
                sectionTimes.GetLength(0) < requiredColumns || sectionTimes.GetLength(1) < requiredRows;

            if (!sectionsTooSmall && !timesTooSmall)
                continue;

            // clearWorld is a world-transition boundary. Section-send state from
            // the old/startup canvas must not carry into the new world anyway.
            sectionsField.SetValue(client, new bool[requiredColumns, requiredRows]);
            sectionTimesField.SetValue(client, new uint[requiredColumns, requiredRows]);
            resized++;
        }

        if (resized > 0)
        {
            Console.WriteLine(
                "[Expanded Worlds] " + stage + ": resized " + resized +
                " RemoteClient section table(s) to " + requiredColumns + "x" + requiredRows + ".");
        }
    }

#if GLOADER_CLIENT
    private static void EnsureClientMapStorage(int requiredWidth, int requiredHeight, string stage)
    {
        if (MainMapField == null || !MainMapField.IsStatic)
            throw new MissingFieldException(typeof(Main).FullName, "Map");

        object current = MainMapField.GetValue(null);
        Type mapType = MainMapField.FieldType;
        FieldInfo maxWidthField = AccessTools.Field(mapType, "MaxWidth");
        FieldInfo maxHeightField = AccessTools.Field(mapType, "MaxHeight");

        if (maxWidthField == null || maxHeightField == null ||
            maxWidthField.FieldType != typeof(int) ||
            maxHeightField.FieldType != typeof(int))
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Terraria WorldMap no longer exposes the audited MaxWidth/MaxHeight shape.");
        }

        if (current != null)
        {
            int currentWidth = (int)maxWidthField.GetValue(current);
            int currentHeight = (int)maxHeightField.GetValue(current);
            if (currentWidth >= requiredWidth && currentHeight >= requiredHeight)
                return;
        }

        ConstructorInfo constructor = AccessTools.Constructor(mapType, new[] { typeof(int), typeof(int) });
        if (constructor == null)
            throw new MissingMethodException(mapType.FullName, ".ctor(int,int)");

        object replacement = constructor.Invoke(new object[] { requiredWidth, requiredHeight });
        MainMapField.SetValue(null, replacement);

        Console.WriteLine(
            "[Expanded Worlds] " + stage + ": map backing storage enlarged to " +
            requiredWidth + "x" + requiredHeight + ".");
    }
#endif

    public static IEnumerable<MethodBase> GetSectionStorageInitializers()
    {
        string[] typeNames = { ActiveSectionsTypeName, LeashedEntityTypeName };
        for (int i = 0; i < typeNames.Length; i++)
        {
            Type type = AccessTools.TypeByName(typeNames[i]);
            if (type == null)
                throw new TypeLoadException("[Expanded Worlds] Required Terraria type not found: " + typeNames[i]);

            ConstructorInfo initializer = type.TypeInitializer;
            if (initializer == null)
                throw new MissingMethodException(type.FullName, ".cctor");

            yield return initializer;
        }
    }
}

/// <summary>
/// Grow the physical tile/map canvas immediately before Terraria clearWorld()
/// touches it. Priority.Last deliberately runs after the generation-dimension
/// prefix on the client. For .wld loads and multiplayer joins Terraria has
/// already read/received maxTilesX/maxTilesY.
/// </summary>
[HarmonyPatch(typeof(WorldGen), "clearWorld")]
internal static class ExpandedWorldBackingStoragePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static void Prefix()
    {
        ExpandedWorldBackingStorage.EnsureForCurrentDimensions("clearWorld");
    }
}

/// <summary>
/// ActiveSections.LastActiveTime and LeashedEntity.BySection are allocated once
/// during type initialization. Patch both initializers before Terraria's entry
/// point and allocate the retail section formula at THICC 11's maximum supported
/// dimensions: 31,600/200+1 by 9,000/150+1 = 159x61.
/// </summary>
[HarmonyPatch]
internal static class ExpandedWorldSectionStorageInitializerPatch
{
    private static readonly FieldInfo MaxTilesXField = AccessTools.Field(typeof(Main), nameof(Main.maxTilesX));
    private static readonly FieldInfo MaxTilesYField = AccessTools.Field(typeof(Main), nameof(Main.maxTilesY));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        return ExpandedWorldBackingStorage.GetSectionStorageInitializers();
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        if (MaxTilesXField == null || MaxTilesYField == null)
            throw new MissingFieldException(typeof(Main).FullName, "maxTilesX/maxTilesY");

        int widthLoads = 0;
        int heightLoads = 0;

        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, MaxTilesXField))
            {
                instruction.opcode = OpCodes.Ldc_I4;
                instruction.operand = ExpandedWorldMath.MaximumSupportedWidth;
                widthLoads++;
            }
            else if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, MaxTilesYField))
            {
                instruction.opcode = OpCodes.Ldc_I4;
                instruction.operand = ExpandedWorldMath.MaximumSupportedHeight;
                heightLoads++;
            }

            yield return instruction;
        }

        if (widthLoads != 1 || heightLoads != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Section-storage initializer shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "<unknown>") +
                ": expected one maxTilesX and one maxTilesY load, found " +
                widthLoads + " and " + heightLoads + ".");
        }
    }
}

#if GLOADER_CLIENT
/// <summary>
/// Exact 1.4.5.8 MapRenderer allocates 5x2 targets. Each normal target covers
/// 2,000x1,800 tiles, but the final allocated column/row are hard-coded 400x600.
/// THICC 11 logically needs 16x5 targets. Its final physical column is 1,600
/// tiles wide and final row is a full 1,800 tiles, so both require one unused
/// guard target to keep the physical edge on the normal-size path. Maximum
/// backing therefore becomes 17x6 while the final renderable X index is 15.
/// </summary>
internal static class ExpandedWorldMapRendererContract
{
    public const int VanillaTargetColumns = 5;
    public const int VanillaTargetRows = 2;

    public static readonly int BackingTargetColumns =
        ExpandedWorldMapMath.BackingTargetColumns(ExpandedWorldMath.MaximumSupportedWidth);

    public static readonly int BackingTargetRows =
        ExpandedWorldMapMath.BackingTargetRows(ExpandedWorldMath.MaximumSupportedHeight);

    public static readonly int LastRenderableTargetColumn =
        ExpandedWorldMapMath.LastRenderableTargetColumn(ExpandedWorldMath.MaximumSupportedWidth);

    public static Type RequireMapRendererType()
    {
        Type type = AccessTools.TypeByName("Terraria.MapRenderer");
        if (type == null)
            throw new TypeLoadException("[Expanded Worlds] Terraria.MapRenderer was not found.");
        return type;
    }

    public static FieldInfo RequireTargetColumnsField()
    {
        return RequireStaticIntField("numTargetsX");
    }

    public static FieldInfo RequireTargetRowsField()
    {
        return RequireStaticIntField("numTargetsY");
    }

    private static FieldInfo RequireStaticIntField(string fieldName)
    {
        Type type = RequireMapRendererType();
        FieldInfo field = AccessTools.Field(type, fieldName);
        if (field == null || field.FieldType != typeof(int) || !field.IsStatic)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer." + fieldName +
                " no longer matches the audited static Int32 field.");
        }
        return field;
    }

    public static bool IsIntConstant(CodeInstruction instruction, int expected)
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

    public static void ReplaceWithIntConstant(CodeInstruction instruction, int value)
    {
        instruction.opcode = OpCodes.Ldc_I4;
        instruction.operand = value;
    }
}

[HarmonyPatch]
internal static class ExpandedWorldMapRendererInitializerPatch
{
    private static readonly FieldInfo TargetColumnsField =
        ExpandedWorldMapRendererContract.RequireTargetColumnsField();

    private static readonly FieldInfo TargetRowsField =
        ExpandedWorldMapRendererContract.RequireTargetRowsField();

    private static MethodBase TargetMethod()
    {
        Type type = ExpandedWorldMapRendererContract.RequireMapRendererType();
        ConstructorInfo initializer = type.TypeInitializer;
        if (initializer == null)
            throw new MissingMethodException(type.FullName, ".cctor");
        return initializer;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patchedColumns = 0;
        int patchedRows = 0;

        for (int i = 1; i < code.Count; i++)
        {
            if (code[i].opcode != OpCodes.Stsfld)
                continue;

            if (Equals(code[i].operand, TargetColumnsField))
            {
                if (!ExpandedWorldMapRendererContract.IsIntConstant(
                        code[i - 1], ExpandedWorldMapRendererContract.VanillaTargetColumns))
                {
                    throw new InvalidOperationException(
                        "[Expanded Worlds] MapRenderer.numTargetsX initializer no longer stores audited value 5.");
                }

                ExpandedWorldMapRendererContract.ReplaceWithIntConstant(
                    code[i - 1], ExpandedWorldMapRendererContract.BackingTargetColumns);
                patchedColumns++;
            }
            else if (Equals(code[i].operand, TargetRowsField))
            {
                if (!ExpandedWorldMapRendererContract.IsIntConstant(
                        code[i - 1], ExpandedWorldMapRendererContract.VanillaTargetRows))
                {
                    throw new InvalidOperationException(
                        "[Expanded Worlds] MapRenderer.numTargetsY initializer no longer stores audited value 2.");
                }

                ExpandedWorldMapRendererContract.ReplaceWithIntConstant(
                    code[i - 1], ExpandedWorldMapRendererContract.BackingTargetRows);
                patchedRows++;
            }
        }

        if (patchedColumns != 1 || patchedRows != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer initializer shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "Terraria.MapRenderer") +
                ": expected one numTargetsX and one numTargetsY assignment, found " +
                patchedColumns + " and " + patchedRows + ".");
        }

        return code;
    }
}

[HarmonyPatch]
internal static class ExpandedWorldMapRendererDrawPatch
{
    private static MethodBase TargetMethod()
    {
        Type type = ExpandedWorldMapRendererContract.RequireMapRendererType();
        MethodInfo method = AccessTools.Method(
            type,
            "DrawMap",
            new[]
            {
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(byte)
            });
        if (method == null)
        {
            throw new MissingMethodException(
                type.FullName,
                "DrawMap(float,float,float,float,float,float,float,float,float,byte)");
        }
        return method;
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        int patched = 0;

        // Exact retail source contains exactly one integer literal 4 here:
        // `for (int i = 0; i <= 4; i++)`. Continue through THICC 11's final
        // physical X target index 15. The Y loop is already dimension-derived;
        // the backing guard row covers the exact-height floor+1 iteration.
        for (int i = 0; i < code.Count; i++)
        {
            if (!ExpandedWorldMapRendererContract.IsIntConstant(code[i], 4))
                continue;

            ExpandedWorldMapRendererContract.ReplaceWithIntConstant(
                code[i], ExpandedWorldMapRendererContract.LastRenderableTargetColumn);
            patched++;
        }

        if (patched != 1)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] MapRenderer.DrawMap source shape changed in " +
                (__originalMethod?.DeclaringType?.FullName ?? "Terraria.MapRenderer") +
                ": expected exactly one integer literal 4 loop bound, found " + patched + ".");
        }

        return code;
    }
}
#endif
#endif
