using System;

/// <summary>
/// Exact non-behavioral scratch-storage bounds from Terraria 1.4.5.8 source.
/// These helpers never decide how much content Terraria generates. They only
/// size fixed record arrays / tracking sentinels that valid expanded generation
/// can exceed while leaving the original placement formulas and RNG untouched.
/// </summary>
internal static class ExpandedWorldCapacityMath
{
    /// <summary>
    /// Floating Islands starts with floor(maxTilesX * 0.0008). Error World can
    /// triple that value. Care Bears can then multiply islands plus sky lakes.
    /// </summary>
    public static int FloatingIslandRecordUpperBound(
        int width,
        int baseSkyLakes,
        bool errorWorldTriple,
        int careBearsMultiplier)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (baseSkyLakes < 0)
            throw new ArgumentOutOfRangeException(nameof(baseSkyLakes));
        if (careBearsMultiplier != 1 && careBearsMultiplier != 2 && careBearsMultiplier != 10)
            throw new ArgumentOutOfRangeException(nameof(careBearsMultiplier));

        int islands = (int)(width * 0.0008d);
        if (errorWorldTriple)
            islands = checked(islands * 3);

        return checked((islands + baseSkyLakes) * careBearsMultiplier);
    }

    public static int FloatingIslandScratchCapacity(int width, int oneBasedWorldTier)
    {
        return FloatingIslandRecordUpperBound(
            width,
            ExpandedWorldTierMath.SkyLakeBaseCount(oneBasedWorldTier),
            errorWorldTriple: true,
            careBearsMultiplier: 10);
    }

    /// <summary>
    /// Corruption/Crimson starts from maxTilesX * 0.00045 attempts. Remix
    /// doubles it and Drunk halves it. The source compares an integer loop index
    /// to the resulting double, so positive fractional values execute ceil(N)
    /// iterations.
    /// </summary>
    public static int CrimsonRegionAttemptUpperBound(int width, bool remixWorld, bool drunkWorld)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        double attempts = width * 0.00045d;
        if (remixWorld)
            attempts *= 2d;
        if (drunkWorld)
            attempts /= 2d;

        return (int)Math.Ceiling(attempts);
    }

    /// <summary>
    /// Each CrimStart rolls Next(5, 9), so at most eight CrimVein calls append
    /// to WorldGen.heartPos per region. CrimEnt does not append there.
    /// </summary>
    public static int CrimsonHeartRecordUpperBound(int width, bool remixWorld, bool drunkWorld)
    {
        return checked(CrimsonRegionAttemptUpperBound(width, remixWorld, drunkWorld) * 8);
    }

    public static int CrimsonHeartScratchCapacity(int width)
    {
        return CrimsonHeartRecordUpperBound(width, remixWorld: true, drunkWorld: false);
    }

    /// <summary>
    /// Mountain Caves uses floor(width * .001), then Remix truncates x1.5.
    /// Every successful attempt writes one mCaveX/mCaveY record and the source
    /// has no capacity check before that write.
    /// </summary>
    public static int MountainCaveRecordUpperBound(int width, bool remixWorld)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        int attempts = (int)(width * 0.001d);
        if (remixWorld)
            attempts = (int)(attempts * 1.5d);
        return attempts;
    }

    public static int MountainCaveScratchCapacity(int width)
    {
        return MountainCaveRecordUpperBound(width, remixWorld: true);
    }

    /// <summary>
    /// Surface Tunnels uses floor(width * .0015), then Remix truncates x1.5.
    /// Vanilla's maxTunnels check is `numTunnels >= maxTunnels - 1`; therefore
    /// the effective sentinel must be one larger than the maximum requested
    /// record count to preserve the source formula instead of clipping it.
    /// </summary>
    public static int SurfaceTunnelRecordUpperBound(int width, bool remixWorld)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        int attempts = (int)(width * 0.0015d);
        if (remixWorld)
            attempts = (int)(attempts * 1.5d);
        return attempts;
    }

    public static int SurfaceTunnelSentinelCapacity(int width)
    {
        return checked(SurfaceTunnelRecordUpperBound(width, remixWorld: true) + 1);
    }

    /// <summary>
    /// Surface Ore chooses Next(width*5/4200, width*10/4200). The upper argument
    /// is exclusive; successful ore patches are tracked until maxOrePatch - 1.
    /// The sentinel returned here preserves every possible source-requested
    /// record at the supported width without changing the count roll.
    /// </summary>
    public static int SurfaceOrePatchRecordUpperBound(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        int exclusiveUpper = checked(width * 10 / 4200);
        return Math.Max(0, exclusiveUpper - 1);
    }

    public static int SurfaceOrePatchSentinelCapacity(int width)
    {
        return checked(SurfaceOrePatchRecordUpperBound(width) + 1);
    }

    // The following audited bounds stay below their vanilla fixed capacities at
    // THICC 11. They are exposed for tests/documentation so a future ladder
    // change cannot silently cross one of those guardrails.
    public static int LakeRecordUpperBound(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        int exclusiveUpper = (int)(((double)width / 4200d) * 6d);
        return Math.Max(0, exclusiveUpper - 1);
    }

    public static int MushroomBiomeRecordUpperBound(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return (int)Math.Ceiling((double)width / 700d);
    }

    public static int OasisRecordUpperBound(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return checked(width / 2100 + 1);
    }

    public static int JungleShrineRecordUpperBound(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        return (int)Math.Ceiling(11d * width / 4200d);
    }

    public static int BeeLarvaRecordUpperBound(int width)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        int maximumHiveCount = (int)(8d * width / 4200d);
        int drunkHiveCount = (int)Math.Ceiling(maximumHiveCount * 0.667d);
        return Math.Max(maximumHiveCount, checked(drunkHiveCount * 2));
    }
}
