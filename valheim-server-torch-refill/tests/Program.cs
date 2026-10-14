using System;
using ServerTorchRefillMod;

internal static class Program
{
    private static int _checks;

    private static void Check(string name, float fuel, float max, float below, float to, int expected)
    {
        int actual = FuelPolicy.UnitsNeeded(fuel, max, below, to);
        if (actual != expected)
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
        ++_checks;
    }

    private static void Main()
    {
        Check("fresh ground torch is above default trigger", 2f, 6f, .25f, 1f, 0);
        Check("empty torch with six slots", 0f, 6f, .25f, 1f, 6);
        Check("quarter-full six-slot torch", 1.5f, 6f, .25f, 1f, 5);
        Check("fractional six-slot torch", 1.2f, 6f, .25f, 1f, 5);
        Check("fractional one-slot torch should not be skipped", .2f, 1f, .25f, 1f, 1);
        Check("empty one-slot torch", 0f, 1f, .25f, 1f, 1);
        Check("above quarter full remains unchanged", .26f, 1f, .25f, 1f, 0);
        Check("target below capacity rounds one item", 2f, 6f, .5f, .75f, 3);
        Check("target never below trigger", 1f, 6f, .5f, .2f, 2);
        Check("zero capacity", 0f, 0f, .25f, 1f, 0);
        Check("NaN current fuel", float.NaN, 6f, .25f, 1f, 0);
        Check("at configured target", 6f, 6f, 1f, 1f, 0);
        Check("always-refill fresh ground torch", 2f, 6f, 1f, 1f, 4);
        Check("always-refill one-slot fractional", .8f, 1f, 1f, 1f, 1);
        Check("always-refill almost full braziers", 4.2f, 5f, 1f, 1f, 1);
        Console.WriteLine($"PASS: {_checks} fuel-policy regression checks.");
    }
}
