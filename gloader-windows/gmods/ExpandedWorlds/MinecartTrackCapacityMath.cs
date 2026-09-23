using System;

/// <summary>
/// Exact scratch-storage math for Terraria 1.4.5.8 minecart-track generation.
/// LongTrackLength scales from a retail maximum of 1000 with WorldWidth, while
/// TrackGenerator reserves the final 100 history slots for tunneling/rewrites.
/// This class sizes storage only; it does not change track counts, lengths, RNG,
/// placement, or collision rules.
/// </summary>
internal static class ExpandedWorldMinecartTrackCapacityMath
{
    public const int VanillaHistoryCapacity = 4096;
    public const int VanillaTunnelReserve = 100;

    private const int VanillaWorldWidth = 4200;
    private const int VanillaLongTrackMaximum = 1000;

    public static int LongTrackLengthMaximum(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        // Mirrors WorldGenRange WorldWidth scaling: (int)((width / 4200.0) * value).
        return (int)(((double)width / VanillaWorldWidth) * VanillaLongTrackMaximum);
    }

    public static int ScratchHistoryCapacity(int width)
    {
        int requestedMaximum = LongTrackLengthMaximum(width);
        int sourceDerivedRequirement = checked(requestedMaximum + VanillaTunnelReserve);
        return Math.Max(VanillaHistoryCapacity, sourceDerivedRequirement);
    }
}
