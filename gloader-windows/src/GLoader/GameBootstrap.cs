using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace GLoader
{
    internal static class GameBootstrap
    {
        public static Assembly Load(string targetPath)
        {
            var fullTargetPath = Path.GetFullPath(targetPath);
            ConfigurePrivateFnaTitleLocation(fullTargetPath);

            if (!IsPrivateRuntimeTarget(fullTargetPath))
            {
                Log.Info("Loading managed Terraria assembly into the 64-bit CoreCLR host.");
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(fullTargetPath);
            }

            var runtimeDirectory = Path.GetDirectoryName(fullTargetPath);
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
                throw new InvalidOperationException("Could not determine the private Terraria runtime directory.");

            runtimeDirectory = Path.GetFullPath(runtimeDirectory);
            var originalDirectory = Environment.CurrentDirectory;

            try
            {
                // TerrariaNetCore's MonoLaunch type is marked beforefieldinit, so the CLR
                // is allowed to initialize its static fields earlier than the first field
                // access. Put cwd on the private runtime before loading the assembly at
                // all, then explicitly run MonoLaunch's class constructor while we still
                // hold that cwd. This guarantees NativesDir captures gdeps\x64-runtime.
                Directory.SetCurrentDirectory(runtimeDirectory);
                Log.Info("Loading private Terraria assembly with runtime cwd: " + runtimeDirectory);

                var gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullTargetPath);
                InitializePrivateRuntimeNativeDirectory(gameAssembly, runtimeDirectory);
                return gameAssembly;
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }
        }

        public static int InvokeEntryPoint(Assembly gameAssembly, string[] gameArguments)
        {
            var entryPoint = gameAssembly.EntryPoint;
            if (entryPoint == null)
                throw new MissingMethodException("Terraria assembly has no managed entry point.");

            var parameters = entryPoint.GetParameters();
            object[] invokeArguments;

            if (parameters.Length == 0)
            {
                invokeArguments = null;
            }
            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
            {
                invokeArguments = new object[] { gameArguments ?? Array.Empty<string>() };
            }
            else
            {
                throw new NotSupportedException(
                    "Unsupported Terraria entry point signature: " + entryPoint);
            }

            try
            {
                var result = entryPoint.Invoke(null, invokeArguments);

                if (result is Task<int> intTask)
                    return intTask.GetAwaiter().GetResult();

                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                    return 0;
                }

                return result is int exitCode ? exitCode : 0;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static void ConfigurePrivateFnaTitleLocation(string targetPath)
        {
            if (!IsPrivateRuntimeTarget(targetPath))
                return;

            // FNA normally uses AppDomain.CurrentDomain.BaseDirectory on Windows.
            // gloader deliberately keeps its managed host under gdeps, so that would
            // make FNA look for Content under Terraria\gdeps\Content. Force FNA to use
            // SDL_GetBasePath instead, which resolves to the public gloader.exe beside
            // Terraria.exe and therefore the real Terraria\Content directory.
            //
            // The pinned FNA build copies the legacy SDL2 variable over the newer
            // variable during FNAPlatform initialization, so both must be set.
            Environment.SetEnvironmentVariable("FNA_SDL2_FORCE_BASE_PATH", "1");
            Environment.SetEnvironmentVariable("FNA_SDL_FORCE_BASE_PATH", "1");
            Log.Info("FNA title base forced to SDL executable directory for private x64 runtime.");
        }

        private static void InitializePrivateRuntimeNativeDirectory(Assembly gameAssembly, string runtimeDirectory)
        {
            var monoLaunch = gameAssembly.GetType("Terraria.MonoLaunch", throwOnError: false);
            if (monoLaunch == null)
            {
                Log.Info("Private runtime has no Terraria.MonoLaunch type; native-directory preinitialization skipped.");
                return;
            }

            RuntimeHelpers.RunClassConstructor(monoLaunch.TypeHandle);

            var nativesField = monoLaunch.GetField(
                "NativesDir",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (nativesField == null || nativesField.GetValue(null) is not string nativesDirectory ||
                string.IsNullOrWhiteSpace(nativesDirectory))
            {
                throw new InvalidOperationException(
                    "The private Terraria runtime did not expose MonoLaunch.NativesDir after initialization.");
            }

            nativesDirectory = Path.GetFullPath(nativesDirectory);
            var expectedDirectory = Path.GetFullPath(
                Path.Combine(runtimeDirectory, "Libraries", "Native", "Windows"));

            if (!PathsEqual(nativesDirectory, expectedDirectory))
            {
                throw new InvalidOperationException(
                    "TerrariaNetCore native directory initialized to the wrong root. Expected '" +
                    expectedDirectory + "', got '" + nativesDirectory + "'.");
            }

            Log.Info("TerrariaNetCore native root locked to private runtime: " + nativesDirectory);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrivateRuntimeTarget(string targetPath)
        {
            var directory = Path.GetDirectoryName(targetPath);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                if (string.Equals(
                    Path.GetFileName(directory),
                    TargetLocator.X64RuntimeDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Path.GetDirectoryName(directory);
                    return !string.IsNullOrWhiteSpace(parent) &&
                        string.Equals(
                            Path.GetFileName(parent),
                            "gdeps",
                            StringComparison.OrdinalIgnoreCase);
                }

                var parentDirectory = Path.GetDirectoryName(directory);
                if (string.IsNullOrWhiteSpace(parentDirectory) ||
                    string.Equals(parentDirectory, directory, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                directory = parentDirectory;
            }

            return false;
        }
    }
}
