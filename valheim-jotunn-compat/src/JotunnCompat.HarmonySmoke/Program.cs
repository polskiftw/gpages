using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace JotunnCompat.HarmonySmoke
{
    // Synthetic analogue of the game's internal AssetLoader value type.
    // This smoke test verifies Harmony's runtime injection mechanics on
    // Windows/.NET Framework without distributing or executing Valheim.
    internal struct HiddenAssetLoader
    {
        internal int m_assetID;
        private uint remaining;

        public uint ReferenceCount => remaining;

        public HiddenAssetLoader(int id, uint references)
        {
            m_assetID = id;
            remaining = references;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Release()
        {
            if (remaining > 0)
            {
                remaining--;
            }
        }
    }

    [HarmonyPatch]
    internal static class ReleasePatch
    {
        internal static int Calls;
        internal static int ReleasedCount;
        internal static uint LastRemaining = uint.MaxValue;

        private static readonly MethodInfo CountGetter =
            AccessTools.PropertyGetter(typeof(HiddenAssetLoader), "ReferenceCount");

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(HiddenAssetLoader), "Release");

        [HarmonyPostfix]
        private static void Released(int ___m_assetID, object __instance)
        {
            if (___m_assetID != 117)
            {
                throw new InvalidOperationException("Harmony read the wrong internal struct field");
            }

            if (__instance == null)
            {
                throw new InvalidOperationException("Harmony did not box the internal struct");
            }

            var value = CountGetter.Invoke(__instance, null);
            if (!(value is uint remaining))
            {
                throw new InvalidOperationException("Reflected reference count was not uint");
            }

            LastRemaining = remaining;
            Calls++;
            if (remaining == 0)
            {
                ReleasedCount++;
            }
        }
    }

    internal static class Program
    {
        private static int Main()
        {
            string tracePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "harmony-smoke-trace.txt");
            File.WriteAllText(tracePath, "Main entered" + Environment.NewLine);
            Console.WriteLine("Harmony smoke: entered Main");
            try
            {
                File.AppendAllText(tracePath, "Creating Harmony instance" + Environment.NewLine);
                var harmony = new Harmony("test.jotunn.assetloader.release");
                File.AppendAllText(tracePath, "Installing synthetic release postfix" + Environment.NewLine);
                harmony.PatchAll(typeof(ReleasePatch));
                File.AppendAllText(tracePath, "Harmony patch installed" + Environment.NewLine);

                var loader = new HiddenAssetLoader(117, 2);
                loader.Release();
                if (ReleasePatch.Calls != 1 ||
                    ReleasePatch.ReleasedCount != 0 ||
                    ReleasePatch.LastRemaining != 1)
                {
                    throw new InvalidOperationException(
                        "First release must retain the asset with reference count 1");
                }

                loader.Release();
                if (ReleasePatch.Calls != 2 ||
                    ReleasePatch.ReleasedCount != 1 ||
                    ReleasePatch.LastRemaining != 0)
                {
                    throw new InvalidOperationException(
                        "Second release must clear the asset at reference count 0");
                }

                Console.WriteLine(
                    "Harmony synthetic internal-value-type release hook passed: " +
                    "postfix, injected field, boxed __instance and uint reference count.");
                return 0;
            }
            catch (Exception error)
            {
                File.AppendAllText(tracePath, "EXCEPTION: " + error + Environment.NewLine);
                Console.Error.WriteLine("Harmony smoke test FAILED: " + error);
                return 1;
            }
        }
    }
}
