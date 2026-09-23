#if GLOADER
using System;
using System.Reflection;
using HarmonyLib;
using Terraria;

/// <summary>
/// Applies the selected physical canvas through one shared client/server path.
/// Terraria still categorizes every custom width as Large; no fake size enum is
/// introduced and no worldgen pass is replaced.
/// </summary>
internal static class ExpandedWorldDimensions
{
    public static void ValidateVanillaSizeContract()
    {
        RequireLiteralInt(nameof(WorldGen.WorldSizeSmallX), ExpandedWorldMath.SmallWidth);
        RequireLiteralInt(nameof(WorldGen.WorldSizeSmallY), ExpandedWorldMath.SmallHeight);
        RequireLiteralInt(nameof(WorldGen.WorldSizeMediumX), ExpandedWorldMath.MediumWidth);
        RequireLiteralInt(nameof(WorldGen.WorldSizeMediumY), ExpandedWorldMath.MediumHeight);
        RequireLiteralInt(nameof(WorldGen.WorldSizeLargeX), ExpandedWorldMath.LargeWidth);
        RequireLiteralInt(nameof(WorldGen.WorldSizeLargeY), ExpandedWorldMath.LargeHeight);

        for (int i = 0; i < ExpandedWorldMath.ExpandedPresetCount; i++)
        {
            ExpandedWorldDefinition definition = ExpandedWorldMath.DefinitionAt(i);
            if (definition.Width != ExpandedWorldMath.CanonicalWidthForTier(definition.OverallTier) ||
                definition.Height != ExpandedWorldMath.CanonicalHeightForTier(definition.OverallTier))
            {
                throw new InvalidOperationException(
                    "[Expanded Worlds] " + definition.Label +
                    " no longer matches the canonical section cadence.");
            }

            ExpandedWorldMath.HorizontalSections(definition.Width);
            ExpandedWorldMath.VerticalSections(definition.Height);
        }

        int rejectedNextWidth = ExpandedWorldMath.CanonicalWidthForTier(
            ExpandedWorldMath.MaximumSupportedOverallTier + 1);
        if (rejectedNextWidth <= ExpandedWorldMath.SignedCoordinatePositiveMaximum)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] THICC ladder boundary audit no longer stops before Int16 overflow.");
        }
    }

    private static void RequireLiteralInt(string fieldName, int expected)
    {
        FieldInfo field = AccessTools.Field(typeof(WorldGen), fieldName);
        if (field == null || field.FieldType != typeof(int) || !field.IsLiteral)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Terraria.WorldGen." + fieldName +
                " no longer matches the audited Int32 constant shape.");
        }

        object raw = field.GetRawConstantValue();
        if (!(raw is int) || (int)raw != expected)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Terraria.WorldGen." + fieldName +
                " changed from audited value " + expected + " to " + raw + ".");
        }
    }

    public static void ApplyActive(string stage)
    {
        if (!ExpandedWorldGenerationContext.IsActive)
            return;

        ExpandedWorldPreset preset = ExpandedWorldGenerationContext.ActivePreset;
        int width = ExpandedWorldMath.WidthFor(preset);
        int height = ExpandedWorldMath.HeightFor(preset);

        Main.maxTilesX = width;
        Main.maxTilesY = height;

        MethodInfo setWorldSizeDerived = AccessTools.Method(typeof(WorldGen), "setWorldSize", Type.EmptyTypes);
        if (setWorldSizeDerived == null)
            throw new MissingMethodException(typeof(WorldGen).FullName, "setWorldSize()");

        setWorldSizeDerived.Invoke(null, null);

        object worldFileData = Main.ActiveWorldFileData;
        if (worldFileData != null)
        {
            MethodInfo setMetadataSize = AccessTools.Method(
                worldFileData.GetType(),
                "SetWorldSize",
                new[] { typeof(int), typeof(int) });

            if (setMetadataSize == null)
                throw new MissingMethodException(worldFileData.GetType().FullName, "SetWorldSize(int,int)");

            setMetadataSize.Invoke(worldFileData, new object[] { width, height });
        }

        int vanillaTier = WorldGen.GetWorldSize();
        if (vanillaTier != 2)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] Custom world stopped categorizing as vanilla Large; GetWorldSize()=" +
                vanillaTier + ".");
        }

        Console.WriteLine(
            "[Expanded Worlds] " + stage + ": using " + ExpandedWorldMath.LabelFor(preset) +
            " " + width + "x" + height + " (vanilla tier " + vanillaTier + ").");
    }

    public static void VerifyPreset(ExpandedWorldPreset preset, string stage)
    {
        if (preset == ExpandedWorldPreset.None)
            return;

        int expectedWidth = ExpandedWorldMath.WidthFor(preset);
        int expectedHeight = ExpandedWorldMath.HeightFor(preset);
        if (Main.maxTilesX != expectedWidth || Main.maxTilesY != expectedHeight)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + stage + " dimension verification failed. Expected " +
                expectedWidth + "x" + expectedHeight + ", got " +
                Main.maxTilesX + "x" + Main.maxTilesY + ".");
        }

        int vanillaTier = WorldGen.GetWorldSize();
        if (vanillaTier != 2)
        {
            throw new InvalidOperationException(
                "[Expanded Worlds] " + stage + " loaded world is no longer categorically Large; GetWorldSize()=" +
                vanillaTier + ".");
        }

        Console.WriteLine(
            "[Expanded Worlds] " + stage + " verified " + ExpandedWorldMath.LabelFor(preset) + " " +
            Main.maxTilesX + "x" + Main.maxTilesY + " (vanilla tier " + vanillaTier + ").");
    }
}
#endif
