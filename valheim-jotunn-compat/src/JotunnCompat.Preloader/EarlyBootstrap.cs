using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using JotunnCompat.Runtime;

namespace JotunnCompat.Preloader
{
    /// <summary>
    /// Installs compatibility hooks at the earliest safe point: immediately after the CLR
    /// loads the assembly named Jotunn, before BepInEx can instantiate dependent plugins.
    /// </summary>
    internal static class EarlyBootstrap
    {
        private const string JotunnAssemblyName = "Jotunn";
        private const string HarmonyId = "claire.valheim.jotunncompat";
        private static readonly object Sync = new object();

        private static bool armed;
        private static bool installed;
        private static ManualLogSource log;

        public static void Arm()
        {
            lock (Sync)
            {
                if (armed || installed)
                {
                    return;
                }

                armed = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

                var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(IsJotunnAssembly);

                if (alreadyLoaded != null)
                {
                    Install(alreadyLoaded);
                }
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (args?.LoadedAssembly == null || !IsJotunnAssembly(args.LoadedAssembly))
            {
                return;
            }

            Install(args.LoadedAssembly);
        }

        private static bool IsJotunnAssembly(Assembly assembly)
        {
            try
            {
                return string.Equals(
                    assembly.GetName().Name,
                    JotunnAssemblyName,
                    StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static void Install(Assembly jotunnAssembly)
        {
            lock (Sync)
            {
                if (installed)
                {
                    return;
                }

                // Mark installed before patching so recursive assembly/type resolution cannot
                // install the same Harmony patches twice.
                installed = true;
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;

                try
                {
                    ValidateJotunnIdentity(jotunnAssembly);

                    var harmony = new Harmony(HarmonyId);

                    PatchRequired(
                        harmony,
                        jotunnAssembly,
                        "Jotunn.Utils.PatchInit",
                        "InitializePatches",
                        nameof(PatchInitPrefix));

                    PatchRequired(
                        harmony,
                        jotunnAssembly,
                        "Jotunn.Utils.AutomaticLocalizationsLoading",
                        "Init",
                        nameof(LocalizationInitPrefix));

                    PrefabCacheFastPath.Install(harmony);
                    ReferenceFastPath.Install(harmony);
                    AssetManagerSafety.Install(harmony, jotunnAssembly);

                    Log.LogInfo(
                        "Early Jotunn compatibility layer installed before dependent plugins.");
                }
                catch (Exception ex)
                {
                    // Fail visibly. Silently running a half-installed compatibility layer is
                    // worse than leaving upstream Jotunn untouched.
                    Log.LogError("Failed to install Jotunn compatibility layer: " + ex);
                    throw;
                }
            }
        }

        private static void ValidateJotunnIdentity(Assembly assembly)
        {
            var name = assembly.GetName();

            if (!string.Equals(name.Name, JotunnAssemblyName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Unexpected assembly identity: " + name.FullName);
            }

            // This build is tested against the pinned compatibility floor. A different Jotunn
            // can have private implementation changes even if the public API looks similar.
            var expected = new Version(2, 30, 1, 0);
            if (name.Version != expected)
            {
                throw new InvalidOperationException(
                    "Jotunn compatibility layer expected assembly version " +
                    expected + " but loaded " + name.Version + ".");
            }
        }

        private static void PatchRequired(
            Harmony harmony,
            Assembly jotunnAssembly,
            string typeName,
            string methodName,
            string prefixName)
        {
            var type = jotunnAssembly.GetType(typeName, false);
            if (type == null)
            {
                throw new TypeLoadException("Required Jotunn type not found: " + typeName);
            }

            var original = AccessTools.Method(type, methodName, Type.EmptyTypes);
            if (original == null)
            {
                throw new MissingMethodException(typeName, methodName);
            }

            var prefix = AccessTools.Method(typeof(EarlyBootstrap), prefixName);
            if (prefix == null)
            {
                throw new MissingMethodException(
                    typeof(EarlyBootstrap).FullName,
                    prefixName);
            }

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }

        private static bool PatchInitPrefix()
        {
            Startup.InitializePatches();
            return false;
        }

        private static bool LocalizationInitPrefix()
        {
            Startup.LoadLocalizations();
            return false;
        }

        private static ManualLogSource Log =>
            log ?? (log = Logger.CreateLogSource("JotunnCompat"));
    }
}
