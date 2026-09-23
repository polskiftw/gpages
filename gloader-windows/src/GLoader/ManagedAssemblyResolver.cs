using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace GLoader
{
    internal sealed class ManagedAssemblyResolver : IDisposable
    {
        private readonly string[] _directories;
        private readonly Assembly _resourceAssembly;
        private readonly AssemblyDependencyResolver _componentResolver;
        private readonly Dictionary<string, string> _managedPaths;
        private readonly Dictionary<string, string> _nativePaths;

        public ManagedAssemblyResolver(params string[] directories)
            : this(null, null, directories)
        {
        }

        public ManagedAssemblyResolver(Assembly resourceAssembly, params string[] directories)
            : this(resourceAssembly, null, directories)
        {
        }

        private ManagedAssemblyResolver(
            Assembly resourceAssembly,
            string componentAssemblyPath,
            params string[] directories)
        {
            _resourceAssembly = resourceAssembly;
            _directories = directories
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (!string.IsNullOrWhiteSpace(componentAssemblyPath))
            {
                try
                {
                    var fullComponentPath = Path.GetFullPath(componentAssemblyPath);
                    if (File.Exists(fullComponentPath))
                        _componentResolver = new AssemblyDependencyResolver(fullComponentPath);
                }
                catch
                {
                    // Some test/fixture assemblies do not have component dependency metadata.
                    // The indexed fallback below remains available for them.
                }
            }

            _managedPaths = BuildIndex(managed: true);
            _nativePaths = BuildIndex(managed: false);

            AssemblyLoadContext.Default.Resolving += Resolve;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveUnmanaged;
        }

        public static ManagedAssemblyResolver ForComponent(
            string componentAssemblyPath,
            Assembly resourceAssembly,
            params string[] directories)
        {
            return new ManagedAssemblyResolver(resourceAssembly, componentAssemblyPath, directories);
        }

        public void Dispose()
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll -= ResolveUnmanaged;
        }

        private Assembly Resolve(AssemblyLoadContext context, AssemblyName requested)
        {
            if (requested == null || string.IsNullOrWhiteSpace(requested.Name))
                return null;

            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly =>
                {
                    try
                    {
                        return string.Equals(
                            assembly.GetName().Name,
                            requested.Name,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

            if (loaded != null)
                return loaded;

            if (_componentResolver != null)
            {
                try
                {
                    var dependencyPath = _componentResolver.ResolveAssemblyToPath(requested);
                    if (!string.IsNullOrWhiteSpace(dependencyPath) && File.Exists(dependencyPath))
                        return context.LoadFromAssemblyPath(Path.GetFullPath(dependencyPath));
                }
                catch (BadImageFormatException)
                {
                    // Fall through to the indexed runtime tree.
                }
                catch (FileLoadException)
                {
                    // Fall through to the indexed runtime tree.
                }
                catch (FileNotFoundException)
                {
                    // Fall through to the indexed runtime tree.
                }
            }

            if (_managedPaths.TryGetValue(requested.Name, out var candidate))
            {
                try
                {
                    return context.LoadFromAssemblyPath(candidate);
                }
                catch (BadImageFormatException)
                {
                    // Indexed file was not a managed assembly after all.
                }
                catch (FileLoadException)
                {
                    // Fall through to embedded resources.
                }
                catch (FileNotFoundException)
                {
                    // Fall through to embedded resources.
                }
            }

            return ResolveEmbedded(context, requested);
        }

        private IntPtr ResolveUnmanaged(Assembly requestingAssembly, string unmanagedDllName)
        {
            if (string.IsNullOrWhiteSpace(unmanagedDllName))
                return IntPtr.Zero;

            if (_componentResolver != null)
            {
                try
                {
                    var dependencyPath = _componentResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                    if (!string.IsNullOrWhiteSpace(dependencyPath) && File.Exists(dependencyPath))
                        return NativeLibrary.Load(Path.GetFullPath(dependencyPath));
                }
                catch
                {
                    // Fall through to the architecture-aware runtime index.
                }
            }

            var key = Path.GetFileNameWithoutExtension(unmanagedDllName);
            if (!_nativePaths.TryGetValue(key, out var candidate))
                return IntPtr.Zero;

            try
            {
                return NativeLibrary.Load(candidate);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private Assembly ResolveEmbedded(AssemblyLoadContext context, AssemblyName requested)
        {
            if (_resourceAssembly == null || string.IsNullOrWhiteSpace(requested.Name))
                return null;

            string[] resourceNames;
            try
            {
                resourceNames = _resourceAssembly.GetManifestResourceNames();
            }
            catch
            {
                return null;
            }

            foreach (var extension in new[] { ".dll", ".exe" })
            {
                var fileName = requested.Name + extension;
                var suffix = "." + fileName;
                var resourceName = resourceNames.FirstOrDefault(name =>
                    string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                    continue;

                try
                {
                    using var stream = _resourceAssembly.GetManifestResourceStream(resourceName);
                    if (stream == null)
                        continue;

                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    memory.Position = 0;
                    if (memory.Length == 0)
                        continue;

                    return context.LoadFromStream(memory);
                }
                catch (BadImageFormatException)
                {
                    // Embedded native library; not a managed assembly candidate.
                }
                catch (FileLoadException)
                {
                    // Continue searching other embedded candidates.
                }
            }

            return null;
        }

        private Dictionary<string, string> BuildIndex(bool managed)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in _directories)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                        .Where(path =>
                            path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    continue;
                }

                foreach (var path in files)
                {
                    if (managed)
                    {
                        try
                        {
                            var name = AssemblyName.GetAssemblyName(path).Name;
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            var fullPath = Path.GetFullPath(path);
                            var score = GetManagedRuntimeScore(fullPath);
                            if (!scores.TryGetValue(name, out var currentScore) || score > currentScore)
                            {
                                scores[name] = score;
                                result[name] = fullPath;
                            }
                        }
                        catch (BadImageFormatException)
                        {
                            // Native file.
                        }
                        catch (FileLoadException)
                        {
                            // Invalid managed file.
                        }
                        catch (FileNotFoundException)
                        {
                            // File disappeared while indexing.
                        }
                    }
                    else
                    {
                        try
                        {
                            AssemblyName.GetAssemblyName(path);
                            continue;
                        }
                        catch (BadImageFormatException)
                        {
                            var name = Path.GetFileNameWithoutExtension(path);
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            var fullPath = Path.GetFullPath(path);
                            var score = GetNativeArchitectureScore(fullPath);

                            if (!scores.TryGetValue(name, out var currentScore) || score > currentScore)
                            {
                                scores[name] = score;
                                result[name] = fullPath;
                            }
                        }
                        catch
                        {
                            // Ignore unreadable files.
                        }
                    }
                }
            }

            return result;
        }

        private static int GetManagedRuntimeScore(string path)
        {
            var normalized = NormalizePath(path);
            var separator = Path.DirectorySeparatorChar.ToString();
            var runtimes = separator + "runtimes" + separator;
            var winX64 = runtimes + "win-x64" + separator;
            var winX86 = runtimes + "win-x86" + separator;
            var win = runtimes + "win" + separator;
            var lib = separator + "lib" + separator;

            if (Environment.Is64BitProcess)
            {
                if (normalized.Contains(winX64, StringComparison.Ordinal))
                    return 400;
                if (normalized.Contains(winX86, StringComparison.Ordinal))
                    return -400;
            }
            else
            {
                if (normalized.Contains(winX86, StringComparison.Ordinal))
                    return 400;
                if (normalized.Contains(winX64, StringComparison.Ordinal))
                    return -400;
            }

            if (normalized.Contains(win, StringComparison.Ordinal))
                return 300;

            if (normalized.Contains(runtimes, StringComparison.Ordinal))
                return -200;

            if (normalized.Contains(lib, StringComparison.Ordinal))
                return 100;

            return 0;
        }

        private static int GetNativeArchitectureScore(string path)
        {
            var normalized = NormalizePath(path);
            var separator = Path.DirectorySeparatorChar.ToString();

            var hasX64 =
                normalized.Contains(separator + "x64" + separator, StringComparison.Ordinal) ||
                normalized.Contains(separator + "win-x64" + separator, StringComparison.Ordinal) ||
                normalized.EndsWith(separator + "x64", StringComparison.Ordinal) ||
                normalized.Contains(separator + "amd64" + separator, StringComparison.Ordinal);
            var hasX86 =
                normalized.Contains(separator + "x86" + separator, StringComparison.Ordinal) ||
                normalized.Contains(separator + "win-x86" + separator, StringComparison.Ordinal) ||
                normalized.EndsWith(separator + "x86", StringComparison.Ordinal);

            if (Environment.Is64BitProcess)
            {
                if (hasX64)
                    return 100;
                if (hasX86)
                    return -100;
            }
            else
            {
                if (hasX86)
                    return 100;
                if (hasX64)
                    return -100;
            }

            return 0;
        }

        private static string NormalizePath(string path)
        {
            return path
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .ToLowerInvariant();
        }
    }
}
