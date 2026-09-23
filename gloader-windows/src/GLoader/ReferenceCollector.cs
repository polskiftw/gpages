using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace GLoader
{
    internal static class ReferenceCollector
    {
        public static IReadOnlyList<MetadataReference> Collect(
            Assembly gameAssembly,
            string gameDirectory,
            string runtimeDirectory,
            string supportDirectory)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            AddTrustedPlatformAssemblies(paths);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                AddAssemblyLocation(paths, assembly, overwrite: false);

            AddManagedFiles(paths, supportDirectory, overwrite: false, recursive: false);

            // The game root contains Content and the original Steam files. The private
            // CoreCLR/FNA runtime can live elsewhere, so scan both roots independently.
            AddManagedFiles(paths, gameDirectory, overwrite: false, recursive: false);
            AddManagedFiles(paths, Path.Combine(gameDirectory, "Libraries"), overwrite: false, recursive: true);

            if (!PathsEqual(gameDirectory, runtimeDirectory))
            {
                AddManagedFiles(paths, runtimeDirectory, overwrite: false, recursive: false);
                AddManagedFiles(paths, Path.Combine(runtimeDirectory, "Libraries"), overwrite: false, recursive: true);
                AddManagedFiles(paths, Path.Combine(runtimeDirectory, "runtimes"), overwrite: false, recursive: true);
            }

            // The Steam client, TerrariaNetCore client variants, and dedicated server
            // all define the Terraria namespace. Feed Roslyn only the exact client or
            // server assembly that is actually running or every Terraria type becomes
            // ambiguous (for example Terraria.Main in Terraria.dll + TerrariaRelease.dll).
            RemoveOtherTerrariaAssemblies(paths, gameAssembly);

            // TerrariaNetCore/FNA replaces the legacy XNA implementation while keeping
            // the Microsoft.Xna.Framework namespaces. If the selected game assembly
            // targets FNA, never also reference the original Steam XNA assemblies or
            // mod source can see duplicate Color/Vector2/etc. definitions.
            RemoveLegacyXnaAssembliesForFna(paths, gameAssembly);

            // The exact Terraria assembly selected by the user always wins over a
            // same-named assembly that might already have been visible elsewhere.
            AddAssemblyLocation(paths, gameAssembly, overwrite: true);

            var references = new List<MetadataReference>();
            foreach (var path in paths.Values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
                catch (BadImageFormatException)
                {
                    // Ignore native binaries.
                }
                catch (IOException ex)
                {
                    Log.Warn("Could not use compiler reference " + path + ": " + ex.Message);
                }
            }

            return references;
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void AddTrustedPlatformAssemblies(IDictionary<string, string> paths)
        {
            var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trusted))
                return;

            foreach (var path in trusted.Split(Path.PathSeparator))
                AddManagedPath(paths, path, overwrite: false);
        }

        private static void RemoveOtherTerrariaAssemblies(
            IDictionary<string, string> paths,
            Assembly gameAssembly)
        {
            var targetName = gameAssembly.GetName().Name;
            var knownTerrariaAssemblies = new[]
            {
                "Terraria",
                "TerrariaRelease",
                "TerrariaDebug",
                "TerrariaServer"
            };

            foreach (var assemblyName in knownTerrariaAssemblies)
            {
                if (!string.Equals(assemblyName, targetName, StringComparison.OrdinalIgnoreCase))
                    paths.Remove(assemblyName);
            }
        }

        private static void RemoveLegacyXnaAssembliesForFna(
            IDictionary<string, string> paths,
            Assembly gameAssembly)
        {
            var usesFna = gameAssembly
                .GetReferencedAssemblies()
                .Any(reference => string.Equals(reference.Name, "FNA", StringComparison.OrdinalIgnoreCase));

            if (!usesFna)
                return;

            var legacyXnaNames = paths.Keys
                .Where(name =>
                    name.Equals("Microsoft.Xna.Framework", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Microsoft.Xna.Framework.", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var name in legacyXnaNames)
                paths.Remove(name);
        }

        private static void AddManagedFiles(
            IDictionary<string, string> paths,
            string directory,
            bool overwrite,
            bool recursive)
        {
            if (!Directory.Exists(directory))
                return;

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", searchOption))
                AddManagedPath(paths, path, overwrite);

            foreach (var path in Directory.EnumerateFiles(directory, "*.exe", searchOption))
                AddManagedPath(paths, path, overwrite);
        }

        private static void AddAssemblyLocation(
            IDictionary<string, string> paths,
            Assembly assembly,
            bool overwrite)
        {
            try
            {
                if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                    return;

                AddManagedPath(paths, assembly.Location, overwrite);
            }
            catch (NotSupportedException)
            {
                // Dynamic or byte-loaded assembly with no usable location.
            }
        }

        private static void AddManagedPath(
            IDictionary<string, string> paths,
            string path,
            bool overwrite)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return;

                var fullPath = Path.GetFullPath(path);
                var name = AssemblyName.GetAssemblyName(fullPath).Name;

                if (overwrite || !paths.ContainsKey(name))
                    paths[name] = fullPath;
            }
            catch (BadImageFormatException)
            {
                // Native DLL/exe.
            }
            catch (FileLoadException)
            {
                // Not a usable managed reference.
            }
            catch (FileNotFoundException)
            {
                // File disappeared while scanning; ignore it.
            }
        }
    }
}
