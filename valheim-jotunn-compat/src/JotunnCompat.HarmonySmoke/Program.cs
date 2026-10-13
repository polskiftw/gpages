using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace JotunnCompat.HarmonySmoke
{
    internal sealed class TestClassLoader
    {
        internal int m_assetID = 117;
        private uint remaining = 2;

        public uint ReferenceCount => remaining;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Release()
        {
            if (remaining > 0) remaining--;
        }
    }

    // Identical visibility and value-type shape to the game's internal
    // SoftReferenceableAssets.AssetLoader. This does NOT run Unity or Mono.
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
            if (remaining > 0) remaining--;
        }
    }

    // Mirrors AssetBundleLoader's actual array-and-index registry. The
    // registry, not Harmony's __instance argument, owns the value type.
    internal sealed class LoaderRegistry
    {
        internal readonly Dictionary<int, int> m_assetIDToLoaderIndex =
            new Dictionary<int, int>();
        internal HiddenAssetLoader[] m_assetLoaders;
    }

    internal static class Probes
    {
        internal static int Calls;
        internal static int LastID;
        internal static uint LastReferenceCount = uint.MaxValue;
        internal static readonly List<uint> RegistryCounts = new List<uint>();
        internal static LoaderRegistry Registry;

        // These postfix signatures are intentionally minimal and independent.
        // Each is installed into a NEW process to isolate a native crash.
        private static void NoArguments()
        {
            Calls++;
        }

        private static void BoxedInstance(object __instance)
        {
            if (__instance == null)
                throw new InvalidOperationException("Harmony returned a null instance");

            var getCount = AccessTools.PropertyGetter(
                __instance.GetType(), "ReferenceCount");
            LastReferenceCount = (uint)getCount.Invoke(__instance, null);
            Calls++;
        }

        private static void InjectedField(int ___m_assetID)
        {
            LastID = ___m_assetID;
            Calls++;
        }

        private static void FieldAndBox(int ___m_assetID, object __instance)
        {
            LastID = ___m_assetID;
            BoxedInstance(__instance);
        }

        private static void RegistryLookup(int ___m_assetID)
        {
            // Exercise the exact reflection strategy used by the real release
            // hook. Importantly, the postfix never receives object __instance.
            var registry = (object)Registry;
            var type = registry.GetType();
            var indexMap = AccessTools.Field(type, "m_assetIDToLoaderIndex")
                .GetValue(registry) as IDictionary;
            var loaders = AccessTools.Field(type, "m_assetLoaders")
                .GetValue(registry) as Array;
            var index = (int)indexMap[___m_assetID];
            var snapshot = loaders.GetValue(index);
            var getter = AccessTools.PropertyGetter(
                snapshot.GetType(), "ReferenceCount");
            LastReferenceCount = (uint)getter.Invoke(snapshot, null);
            LastID = ___m_assetID;
            RegistryCounts.Add(LastReferenceCount);
            Calls++;
        }

        private static void StructByReference(ref HiddenAssetLoader __instance)
        {
            LastID = __instance.m_assetID;
            LastReferenceCount = __instance.ReferenceCount;
            Calls++;
        }

        internal static MethodInfo Hook(string name) =>
            AccessTools.Method(typeof(Probes), name);
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            var testName = args.Length > 0 ? args[0] : "struct-mixed";
            var trace = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "harmony-smoke-" + testName + ".txt");
            File.WriteAllText(trace, "Entered Main; case=" + testName + Environment.NewLine);
            Console.WriteLine("Starting Harmony scenario " + testName);
            try
            {
                var harmony = new Harmony("test.jotunn.assetloader." + testName);
                MethodInfo target;
                MethodInfo postfix;
                switch (testName)
                {
                    case "class-control":
                        target = AccessTools.Method(typeof(TestClassLoader), "Release");
                        postfix = Probes.Hook("NoArguments");
                        break;
                    case "struct-control":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "Release");
                        postfix = Probes.Hook("NoArguments");
                        break;
                    case "struct-instance":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "Release");
                        postfix = Probes.Hook("BoxedInstance");
                        break;
                    case "struct-field":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "Release");
                        postfix = Probes.Hook("InjectedField");
                        break;
                    case "struct-mixed":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "Release");
                        postfix = Probes.Hook("FieldAndBox");
                        break;
                    case "struct-byref":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "Release");
                        postfix = Probes.Hook("StructByReference");
                        break;
                    case "struct-registry":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "Release");
                        postfix = Probes.Hook("RegistryLookup");
                        break;
                    default:
                        throw new ArgumentException("Unknown smoke case " + testName);
                }

                File.AppendAllText(trace, "Patching " + target + " with " + postfix + Environment.NewLine);
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                File.AppendAllText(trace, "Patch installed; invoking method" + Environment.NewLine);

                if (testName == "class-control")
                {
                    new TestClassLoader().Release();
                }
                else if (testName == "struct-registry")
                {
                    var registry = new LoaderRegistry
                    {
                        m_assetLoaders = new[] { new HiddenAssetLoader(117, 2) }
                    };
                    registry.m_assetIDToLoaderIndex.Add(117, 0);
                    Probes.Registry = registry;
                    // Operate on the array element by reference, just as the
                    // game's owning asset-bundle registry does.
                    registry.m_assetLoaders[0].Release();
                    registry.m_assetLoaders[0].Release();
                }
                else
                {
                    var loader = new HiddenAssetLoader(117, 2);
                    loader.Release();
                }

                File.AppendAllText(trace, "Method returned; hooks=" + Probes.Calls + Environment.NewLine);
                Console.WriteLine("CASE PASS " + testName + ": hook calls=" + Probes.Calls +
                    ", assetID=" + Probes.LastID +
                    ", referenceCount=" + Probes.LastReferenceCount);
                if (Probes.Calls != (testName == "struct-registry" ? 2 : 1))
                {
                    throw new InvalidOperationException("Unexpected postfix invocation count");
                }
                if (testName == "struct-field" || testName == "struct-mixed" ||
                    testName == "struct-byref" || testName == "struct-registry")
                {
                    if (Probes.LastID != 117)
                        throw new InvalidOperationException("Wrong injected asset id");
                }
                if (testName == "struct-instance" ||
                    testName == "struct-mixed" || testName == "struct-byref")
                {
                    if (Probes.LastReferenceCount != 1)
                        throw new InvalidOperationException("Post-release reference count should equal 1");
                }
                if (testName == "struct-registry")
                {
                    if (Probes.RegistryCounts.Count != 2 ||
                        Probes.RegistryCounts[0] != 1 ||
                        Probes.RegistryCounts[1] != 0)
                        throw new InvalidOperationException(
                            "Registry lookup must reflect both 1->0 release transitions");
                }
                return 0;
            }
            catch (Exception e)
            {
                File.AppendAllText(trace, "EXCEPTION " + e + Environment.NewLine);
                Console.Error.WriteLine("CASE FAIL " + testName + ": " + e);
                return 1;
            }
        }
    }
}
