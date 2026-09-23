using System;

/// <summary>
/// Terraria 1.4.5.8 world-size continuation model.
///
/// Vanilla physical sizes are exact 200 x 150 network-section grids:
///   Small  =  4200 x 1200 = 21 x  8 sections
///   Medium =  6400 x 1800 = 32 x 12 sections
///   Large  =  8400 x 2400 = 42 x 16 sections
///
/// Expanded Worlds continues that same cadence. Horizontal section deltas repeat
/// +11, +10; vertical sections always add +4. The public expanded ladder is
/// THICC through THICC 11; the next canonical tier would be 33,600 tiles wide,
/// beyond Terraria's signed Int16-positive coordinate boundary (32,767), so the
/// ladder deliberately stops at THICC 11 / overall tier 14.
/// </summary>
internal enum ExpandedWorldPreset
{
    None = 0,
    Thicc = 1,
    Thicc2 = 2,
    Thicc3 = 3,
    Thicc4 = 4,
    Thicc5 = 5,
    Thicc6 = 6,
    Thicc7 = 7,
    Thicc8 = 8,
    Thicc9 = 9,
    Thicc10 = 10,
    Thicc11 = 11,
}

internal readonly struct ExpandedWorldDefinition
{
    public ExpandedWorldDefinition(
        ExpandedWorldPreset preset,
        string label,
        int width,
        int height,
        int overallTier)
    {
        Preset = preset;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Width = width;
        Height = height;
        OverallTier = overallTier;
    }

    public ExpandedWorldPreset Preset { get; }
    public string Label { get; }
    public int Width { get; }
    public int Height { get; }
    public int OverallTier { get; }
}

internal static class ExpandedWorldMath
{
    public const int SectionWidth = 200;
    public const int SectionHeight = 150;
    public const int SignedCoordinatePositiveMaximum = short.MaxValue;

    public const int SmallWidth = 4200;
    public const int SmallHeight = 1200;
    public const int MediumWidth = 6400;
    public const int MediumHeight = 1800;
    public const int LargeWidth = 8400;
    public const int LargeHeight = 2400;

    public const int MaximumSupportedOverallTier = 14;
    public const int MaximumSupportedWidth = 31600;
    public const int MaximumSupportedHeight = 9000;

    private static readonly ExpandedWorldDefinition[] ExpandedDefinitions =
    {
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc,   "THICC",    10600, 3000, 4),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc2,  "THICC 2",  12600, 3600, 5),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc3,  "THICC 3",  14800, 4200, 6),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc4,  "THICC 4",  16800, 4800, 7),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc5,  "THICC 5",  19000, 5400, 8),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc6,  "THICC 6",  21000, 6000, 9),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc7,  "THICC 7",  23200, 6600, 10),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc8,  "THICC 8",  25200, 7200, 11),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc9,  "THICC 9",  27400, 7800, 12),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc10, "THICC 10", 29400, 8400, 13),
        new ExpandedWorldDefinition(ExpandedWorldPreset.Thicc11, "THICC 11", 31600, 9000, 14),
    };

    public static int ExpandedPresetCount => ExpandedDefinitions.Length;

    public static ExpandedWorldDefinition DefinitionAt(int expandedIndex)
    {
        if (expandedIndex < 0 || expandedIndex >= ExpandedDefinitions.Length)
            throw new ArgumentOutOfRangeException(nameof(expandedIndex));
        return ExpandedDefinitions[expandedIndex];
    }

    public static ExpandedWorldDefinition DefinitionFor(ExpandedWorldPreset preset)
    {
        int index = (int)preset - 1;
        if (index < 0 || index >= ExpandedDefinitions.Length || ExpandedDefinitions[index].Preset != preset)
            throw new ArgumentOutOfRangeException(nameof(preset));
        return ExpandedDefinitions[index];
    }

    public static bool TryGetPresetByDimensions(int width, int height, out ExpandedWorldPreset preset)
    {
        for (int i = 0; i < ExpandedDefinitions.Length; i++)
        {
            ExpandedWorldDefinition definition = ExpandedDefinitions[i];
            if (definition.Width == width && definition.Height == height)
            {
                preset = definition.Preset;
                return true;
            }
        }

        preset = ExpandedWorldPreset.None;
        return false;
    }

    public static bool TryParsePreset(string raw, out ExpandedWorldPreset preset)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            preset = ExpandedWorldPreset.None;
            return false;
        }

        string normalized = raw.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        if (normalized == "THICC")
        {
            preset = ExpandedWorldPreset.Thicc;
            return true;
        }

        if (normalized.StartsWith("THICC", StringComparison.Ordinal) &&
            int.TryParse(normalized.Substring(5), out int suffix) &&
            suffix >= 2 && suffix <= ExpandedDefinitions.Length)
        {
            preset = (ExpandedWorldPreset)suffix;
            return true;
        }

        preset = ExpandedWorldPreset.None;
        return false;
    }

    public static bool IsExpandedPresetDimensions(int width, int height)
    {
        return TryGetPresetByDimensions(width, height, out _);
    }

    public static int WidthFor(ExpandedWorldPreset preset)
    {
        return preset == ExpandedWorldPreset.None ? LargeWidth : DefinitionFor(preset).Width;
    }

    public static int HeightFor(ExpandedWorldPreset preset)
    {
        return preset == ExpandedWorldPreset.None ? LargeHeight : DefinitionFor(preset).Height;
    }

    public static int TierFor(ExpandedWorldPreset preset)
    {
        return preset == ExpandedWorldPreset.None ? 3 : DefinitionFor(preset).OverallTier;
    }

    public static string LabelFor(ExpandedWorldPreset preset)
    {
        return preset == ExpandedWorldPreset.None ? "Vanilla" : DefinitionFor(preset).Label;
    }

    public static int CanonicalHorizontalSectionsForTier(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));

        int steps = oneBasedWorldTier - 1;
        return checked(21 + 10 * steps + (steps + 1) / 2);
    }

    public static int CanonicalVerticalSectionsForTier(int oneBasedWorldTier)
    {
        if (oneBasedWorldTier < 1)
            throw new ArgumentOutOfRangeException(nameof(oneBasedWorldTier));
        return checked(8 + 4 * (oneBasedWorldTier - 1));
    }

    public static int CanonicalWidthForTier(int oneBasedWorldTier)
    {
        return checked(CanonicalHorizontalSectionsForTier(oneBasedWorldTier) * SectionWidth);
    }

    public static int CanonicalHeightForTier(int oneBasedWorldTier)
    {
        return checked(CanonicalVerticalSectionsForTier(oneBasedWorldTier) * SectionHeight);
    }

    public static int HorizontalSections(int width)
    {
        if (width <= 0 || width % SectionWidth != 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return width / SectionWidth;
    }

    public static int VerticalSections(int height)
    {
        if (height <= 0 || height % SectionHeight != 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        return height / SectionHeight;
    }

    public static long TileArea(int width, int height)
    {
        return checked((long)width * height);
    }
}

/// <summary>
/// Pure map-target math for Terraria 1.4.5.8's MapRenderer. Logical target counts
/// are ceil(width/2000) x ceil(height/1800). The retail renderer makes its final
/// allocated column only 400 pixels wide and final row only 600 high, so a guard
/// column/row is required whenever the physical final target does not have that
/// exact special-tail extent. Guard targets preserve retail checkMap semantics;
/// they do not add world tiles.
/// </summary>
internal static class ExpandedWorldMapMath
{
    public const int TextureMaxWidth = 2000;
    public const int TextureMaxHeight = 1800;
    public const int RetailFinalColumnWidth = 400;
    public const int RetailFinalRowHeight = 600;

    public static int LogicalTargetColumns(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return checked((width - 1) / TextureMaxWidth + 1);
    }

    public static int LogicalTargetRows(int height)
    {
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        return checked((height - 1) / TextureMaxHeight + 1);
    }

    public static int PhysicalFinalColumnWidth(int width)
    {
        int remainder = width % TextureMaxWidth;
        return remainder == 0 ? TextureMaxWidth : remainder;
    }

    public static int PhysicalFinalRowHeight(int height)
    {
        int remainder = height % TextureMaxHeight;
        return remainder == 0 ? TextureMaxHeight : remainder;
    }

    public static bool NeedsGuardColumn(int width)
    {
        return PhysicalFinalColumnWidth(width) != RetailFinalColumnWidth;
    }

    public static bool NeedsGuardRow(int height)
    {
        return PhysicalFinalRowHeight(height) != RetailFinalRowHeight;
    }

    public static int BackingTargetColumns(int width)
    {
        return checked(LogicalTargetColumns(width) + (NeedsGuardColumn(width) ? 1 : 0));
    }

    public static int BackingTargetRows(int height)
    {
        return checked(LogicalTargetRows(height) + (NeedsGuardRow(height) ? 1 : 0));
    }

    public static int LastRenderableTargetColumn(int width)
    {
        return LogicalTargetColumns(width) - 1;
    }
}

/// <summary>
/// One generation context is shared by client and server builds. Every
/// source-backed discrete continuation and capacity guard therefore sees the
/// same selected physical tier regardless of how Terraria was launched.
/// </summary>
internal static class ExpandedWorldGenerationContext
{
    public static ExpandedWorldPreset ActivePreset { get; private set; }
    public static bool IsActive => ActivePreset != ExpandedWorldPreset.None;
    public static int ActiveTier => ExpandedWorldMath.TierFor(ActivePreset);

    public static void Begin(ExpandedWorldPreset preset)
    {
        if (preset == ExpandedWorldPreset.None)
            throw new ArgumentOutOfRangeException(nameof(preset));

        ExpandedWorldMath.DefinitionFor(preset);
        ActivePreset = preset;
    }

    public static void End()
    {
        ActivePreset = ExpandedWorldPreset.None;
    }
}

internal readonly struct IntRange : IEquatable<IntRange>
{
    public int Minimum { get; }
    public int Maximum { get; }

    public IntRange(int minimum, int maximum)
    {
        if (maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        Minimum = minimum;
        Maximum = maximum;
    }

    public bool Equals(IntRange other)
    {
        return Minimum == other.Minimum && Maximum == other.Maximum;
    }

    public override bool Equals(object obj)
    {
        return obj is IntRange other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Minimum * 397) ^ Maximum;
        }
    }

    public override string ToString()
    {
        return Minimum == Maximum
            ? Minimum.ToString()
            : Minimum + "-" + Maximum;
    }
}
