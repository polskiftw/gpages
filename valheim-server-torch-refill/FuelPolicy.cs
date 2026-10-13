using System;

namespace ServerTorchRefillMod
{
    // No Unity dependency: kept separately so threshold and whole-item behavior
    // can be checked in CI as well as in the compiled Valheim plugin.
    internal static class FuelPolicy
    {
        internal static int UnitsNeeded(float currentFuel, float maximumFuel,
            float refillBelowPercent, float refillToPercent)
        {
            if (float.IsNaN(currentFuel) || float.IsInfinity(currentFuel) ||
                float.IsNaN(maximumFuel) || float.IsInfinity(maximumFuel) || maximumFuel <= 0f ||
                float.IsNaN(refillBelowPercent) || float.IsNaN(refillToPercent))
                return 0;

            double current = Math.Max(0d, currentFuel);
            double maximum = maximumFuel;
            double threshold = Math.Max(0d, Math.Min(1d, refillBelowPercent));
            double to = Math.Max(threshold, Math.Max(0d, Math.Min(1d, refillToPercent)));

            if (current / maximum > threshold)
                return 0;

            double missing = maximum * to - current;
            if (missing <= 0d)
                return 0;

            // A whole item must be consumed to fill a fractional remainder.
            // Floor() silently skipped one-capacity torches at fuel 0.1..0.25.
            // An epsilon avoids wasting an item on floating-point roundoff.
            return (int)Math.Min(int.MaxValue, Math.Ceiling(missing - 0.000001d));
        }
    }
}
