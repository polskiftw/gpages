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
        internal object m_asset;
        internal bool CallbackRan;
        private uint remaining;

        public uint ReferenceCount => remaining;

        public HiddenAssetLoader(int id, uint references)
        {
            m_assetID = id;
            m_asset = new object();
            CallbackRan = false;
            remaining = references;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Release()
        {
            if (remaining > 0) remaining--;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InvokeCallbacks(int result)
        {
            CallbackRan = result == 1 && m_asset != null;
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
        internal static readonly object ReplacementAsset = new object();

        private static void CallbackControl(int result)
        {
            if (result != 1) throw new InvalidOperationException("Wrong original callback arg");
            Calls++;
        }

        // Identical to the slim runtime's existing InvokeCallbacks prefix:
        // AssetID is a value-type field injection, while m_asset is a
        // by-reference object field injection on an internal struct.
        private static void CallbackFieldRef(int ___m_assetID, ref object ___m_asset, int result)
        {
            if (result != 1 || ___m_asset == null)
                throw new InvalidOperationException("Invalid callback arguments");
            LastID = ___m_assetID;
            ___m_asset = ReplacementAsset;
            Calls++;
        }

        private static void CallbackStructByRef(ref HiddenAssetLoader __instance, int result)
        {
            if (result != 1 || __instance.m_asset == null)
                throw new InvalidOperationException("Invalid callback args");
            LastID = __instance.m_assetID;
            __instance.m_asset = ReplacementAsset;
            Calls++;
        }

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
                bool prefixPatch = false;
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
                    case "callback-control":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "InvokeCallbacks");
                        postfix = Probes.Hook("CallbackControl");
                        prefixPatch = true;
                        break;
                    case "callback-field-ref":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "InvokeCallbacks");
                        postfix = Probes.Hook("CallbackFieldRef");
                        prefixPatch = true;
                        break;
                    case "callback-struct-byref":
                        target = AccessTools.Method(typeof(HiddenAssetLoader), "InvokeCallbacks");
                        postfix = Probes.Hook("CallbackStructByRef");
                        prefixPatch = true;
                        break;
                    default:
                        throw new ArgumentException("Unknown smoke case " + testName);
                }

                File.AppendAllText(trace, "Patching " + target + " with " + postfix + Environment.NewLine);
                if (prefixPatch)
                    harmony.Patch(target, prefix: new HarmonyMethod(postfix));
                else
                    harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                File.AppendAllText(trace, "Patch installed; invoking method" + Environment.NewLine);

                if (testName == "class-control")
                {
                    new TestClassLoader().Release();
                }
                else if (testName.StartsWith("callback-", StringComparison.Ordinal))
                {
                    var loader = new HiddenAssetLoader(117, 2);
                    loader.InvokeCallbacks(1);
                    if (!loader.CallbackRan)
                        throw new InvalidOperationException("Original callback was not executed");
                    if (testName != "callback-control" &&
                        !ReferenceEquals(loader.m_asset, Probes.ReplacementAsset))
                        throw new InvalidOperationException("Callback did not update struct asset field");
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
                if (Probes.Calls != (testName == "struct-registry" ? 2 : 1))
                {
                    throw new InvalidOperationException("Unexpected postfix invocation count");
                }
                if (testName == "struct-field" || testName == "struct-mixed" ||
                    testName == "struct-byref" || testName == "struct-registry" ||
                    testName == "callback-field-ref" ||
                    testName == "callback-struct-byref")
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
                Console.WriteLine("CASE PASS " + testName + ": hook calls=" + Probes.Calls +
                    ", assetID=" + Probes.LastID +
                    ", referenceCount=" + Probes.LastReferenceCount);
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
