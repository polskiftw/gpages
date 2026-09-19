using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;

namespace JotunnCompat.FastPath
{
    /// <summary>
    /// Startup replacements injected into the official Jotunn binary.
    ///
    /// This assembly deliberately does not reference Jotunn at compile time. That keeps the
    /// fast paths independent from Valheim game assemblies and lets the compatibility build
    /// preserve Jotunn's exact public API surface.
    /// </summary>
    public static class Startup
    {
        private const string JotunnGuid = "com.jotunn.jotunn";
        private const string PatchInitAttributeFullName = "Jotunn.Utils.PatchInitAttribute";
        private const string PatchInitAttributeName = "PatchInitAttribute";
        private const string TranslationsFolderName = "Translations";
        private const string CommunityTranslationFileName = "community_translation.json";

        private static ManualLogSource log;

        private static ManualLogSource Log
        {
            get
            {
                if (log == null)
                {
                    log = BepInEx.Logging.Logger.CreateLogSource("JotunnCompat");
                }

                return log;
            }
        }

        /// <summary>
        /// Compatibility-preserving replacement for Jotunn.Utils.PatchInit.InitializePatches.
        ///
        /// The upstream implementation reflects every type and every public static method in
        /// every Jotunn-dependent mod. PatchInitAttribute has been obsolete for years, so most
        /// modern mods do not contain it at all. We cheaply preflight the assembly metadata and
        /// only pay the reflection cost for assemblies that can actually contain the attribute.
        /// </summary>
        public static void InitializePatches()
        {
            var candidates = new List<PatchCandidate>();
            var searchedAssemblies = new HashSet<Assembly>();
            var sequence = 0;

            foreach (var pluginInfo in Chainloader.PluginInfos.Values)
            {
                try
                {
                    if (pluginInfo == null || pluginInfo.Instance == null || pluginInfo.Metadata == null)
                    {
                        continue;
                    }

                    if (string.Equals(pluginInfo.Metadata.GUID, JotunnGuid, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var pluginType = pluginInfo.Instance.GetType();
                    if (!DependsOnJotunn(pluginType))
                    {
                        continue;
                    }

                    var assembly = pluginType.Assembly;
                    if (!searchedAssemblies.Add(assembly))
                    {
                        continue;
                    }

                    if (!MightContainPatchInitAttribute(assembly))
                    {
                        continue;
                    }

                    foreach (var type in SafeGetTypes(assembly))
                    {
                        try
                        {
                            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                            {
                                var attribute = FindPatchInitAttribute(method);
                                if (attribute == null)
                                {
                                    continue;
                                }

                                candidates.Add(new PatchCandidate
                                {
                                    Method = method,
                                    Priority = GetPatchPriority(attribute),
                                    Sequence = sequence++
                                });
                            }
                        }
                        catch (Exception)
                        {
                            // Match upstream behavior: one malformed type must not prevent
                            // PatchInit discovery in the rest of the assembly.
                        }
                    }
                }
                catch (Exception)
                {
                    // Match upstream behavior for malformed plugin assemblies.
                }
            }

            foreach (var candidate in candidates.OrderBy(x => x.Priority).ThenBy(x => x.Sequence))
            {
                candidate.Method.Invoke(null, null);
            }
        }

        /// <summary>
        /// Compatibility-preserving replacement for Jotunn.Utils.AutomaticLocalizationsLoading.Init.
        ///
        /// Upstream recursively walks the entire BepInEx plugin tree five separate times.
        /// This performs one recursive walk, classifies the files in memory, and then feeds the
        /// same files into Jotunn's existing CustomLocalization implementation.
        /// </summary>
        public static void LoadLocalizations()
        {
            var root = BepInEx.Paths.PluginPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }

            string[] allFiles;
            try
            {
                allFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Failed to scan Jotunn translation files: " + ex);
                return;
            }

            var communityJson = new List<string>();
            var json = new List<string>();
            var unity = new List<string>();
            var yaml = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in allFiles)
            {
                FileInfo file;
                try
                {
                    file = new FileInfo(path);
                }
                catch (Exception)
                {
                    continue;
                }

                if (file.Directory == null ||
                    file.Directory.Parent == null ||
                    !string.Equals(file.Directory.Parent.Name, TranslationsFolderName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!seen.Add(file.FullName))
                {
                    continue;
                }

                var extension = file.Extension;
                if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(file.Name, CommunityTranslationFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        communityJson.Add(file.FullName);
                    }
                    else
                    {
                        json.Add(file.FullName);
                    }
                }
                else if (string.Equals(extension, ".language", StringComparison.OrdinalIgnoreCase))
                {
                    unity.Add(file.FullName);
                }
                else if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
                {
                    yaml.Add(file.FullName);
                }
            }

            // Avoid initializing LocalizationManager at all when no automatic translations exist.
            if (communityJson.Count == 0 && json.Count == 0 && unity.Count == 0 && yaml.Count == 0)
            {
                return;
            }

            LocalizationBridge bridge;
            try
            {
                bridge = new LocalizationBridge();
            }
            catch (Exception ex)
            {
                Log.LogWarning("Failed to initialize Jotunn localization bridge: " + ex);
                return;
            }

            foreach (var path in communityJson)
            {
                AddLocalizationFile(bridge, path, true);
            }

            foreach (var path in json)
            {
                AddLocalizationFile(bridge, path, true);
            }

            foreach (var path in unity)
            {
                AddLocalizationFile(bridge, path, false);
            }

            if (yaml.Count > 0 && !IsYamlDotNetAvailable())
            {
                Log.LogWarning(
                    "Found " + yaml.Count +
                    " YAML localization file(s) but YamlDotNet is not loaded. " +
                    "Mods using .yaml/.yml localization must include YamlDotNet.dll as a dependency.");
                return;
            }

            foreach (var path in yaml)
            {
                AddLocalizationFile(bridge, path, false);
            }
        }

        private static void AddLocalizationFile(LocalizationBridge bridge, string path, bool isJson)
        {
            try
            {
                var metadata = GetPluginMetadataForPath(path) ?? bridge.JotunnMetadata;
                bridge.AddFile(metadata, path, isJson);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Exception caught while loading localization file " + path + ": " + ex);
            }
        }

        private static bool DependsOnJotunn(Type pluginType)
        {
            try
            {
                foreach (var attribute in CustomAttributeData.GetCustomAttributes(pluginType))
                {
                    if (!string.Equals(attribute.AttributeType.FullName, typeof(BepInDependency).FullName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (attribute.ConstructorArguments.Count > 0 &&
                        string.Equals(attribute.ConstructorArguments[0].Value as string, JotunnGuid, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Fall through to the slower compatibility path below.
            }

            try
            {
                foreach (BepInDependency dependency in pluginType.GetCustomAttributes(typeof(BepInDependency), false))
                {
                    if (string.Equals(dependency.DependencyGUID, JotunnGuid, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(x => x != null);
            }
        }

        private static CustomAttributeData FindPatchInitAttribute(MethodInfo method)
        {
            try
            {
                foreach (var attribute in CustomAttributeData.GetCustomAttributes(method))
                {
                    if (string.Equals(attribute.AttributeType.FullName, PatchInitAttributeFullName, StringComparison.Ordinal))
                    {
                        return attribute;
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static int GetPatchPriority(CustomAttributeData attribute)
        {
            var priority = 0;

            if (attribute.ConstructorArguments.Count > 0 &&
                attribute.ConstructorArguments[0].Value is int constructorPriority)
            {
                priority = constructorPriority;
            }

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (string.Equals(namedArgument.MemberName, "Priority", StringComparison.Ordinal) &&
                    namedArgument.TypedValue.Value is int namedPriority)
                {
                    priority = namedPriority;
                }
            }

            return priority;
        }

        private static bool MightContainPatchInitAttribute(Assembly assembly)
        {
            string location;
            try
            {
                location = assembly.Location;
            }
            catch (Exception)
            {
                return true;
            }

            if (string.IsNullOrEmpty(location) || !File.Exists(location))
            {
                return true;
            }

            try
            {
                return FileContainsAscii(location, PatchInitAttributeName);
            }
            catch (Exception)
            {
                // Never trade compatibility for the optimization.
                return true;
            }
        }

        private static bool FileContainsAscii(string path, string text)
        {
            var needle = Encoding.ASCII.GetBytes(text);
            var buffer = new byte[16384 + needle.Length];

            using (var stream = File.OpenRead(path))
            {
                var carry = 0;

                while (true)
                {
                    var read = stream.Read(buffer, carry, 16384);
                    if (read <= 0)
                    {
                        return false;
                    }

                    var total = carry + read;
                    if (IndexOf(buffer, total, needle) >= 0)
                    {
                        return true;
                    }

                    carry = Math.Min(needle.Length - 1, total);
                    Buffer.BlockCopy(buffer, total - carry, buffer, 0, carry);
                }
            }
        }

        private static int IndexOf(byte[] haystack, int haystackLength, byte[] needle)
        {
            if (needle.Length == 0)
            {
                return 0;
            }

            var last = haystackLength - needle.Length;
            for (var i = 0; i <= last; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsYamlDotNetAvailable()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => string.Equals(x.GetName().Name, "YamlDotNet", StringComparison.Ordinal));
        }

        private static BepInPlugin GetPluginMetadataForPath(string path)
        {
            string fileDirectory;
            try
            {
                fileDirectory = NormalizeDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            }
            catch (Exception)
            {
                return null;
            }

            var pluginRoot = NormalizeDirectory(BepInEx.Paths.PluginPath);

            foreach (var pluginInfo in Chainloader.PluginInfos.Values)
            {
                try
                {
                    if (pluginInfo == null ||
                        pluginInfo.Metadata == null ||
                        string.IsNullOrEmpty(pluginInfo.Location))
                    {
                        continue;
                    }

                    var pluginDirectory = NormalizeDirectory(Path.GetDirectoryName(Path.GetFullPath(pluginInfo.Location)));
                    if (string.Equals(pluginDirectory, pluginRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        // Match upstream behavior: a plugin DLL placed directly in the BepInEx
                        // plugin root does not claim arbitrary translation files in that root.
                        continue;
                    }

                    if (IsSameOrChildDirectory(fileDirectory, pluginDirectory))
                    {
                        return pluginInfo.Metadata;
                    }
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsSameOrChildDirectory(string path, string parent)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(parent))
            {
                return false;
            }

            if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = parent + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class PatchCandidate
        {
            public MethodInfo Method;
            public int Priority;
            public int Sequence;
        }

        private sealed class LocalizationBridge
        {
            private readonly object localizationManager;
            private readonly MethodInfo getLocalization;
            private readonly MethodInfo addFileByPath;

            public BepInPlugin JotunnMetadata { get; private set; }

            public LocalizationBridge()
            {
                var jotunnAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(x => string.Equals(x.GetName().Name, "Jotunn", StringComparison.Ordinal));

                if (jotunnAssembly == null)
                {
                    throw new InvalidOperationException("Jotunn assembly is not loaded");
                }

                var managerType = jotunnAssembly.GetType("Jotunn.Managers.LocalizationManager", true);
                var customLocalizationType = jotunnAssembly.GetType("Jotunn.Entities.CustomLocalization", true);

                var instanceProperty = managerType.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static);

                if (instanceProperty == null)
                {
                    throw new MissingMemberException(managerType.FullName, "Instance");
                }

                localizationManager = instanceProperty.GetValue(null, null);

                getLocalization = managerType.GetMethod(
                    "GetLocalization",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(BepInPlugin) },
                    null);

                if (getLocalization == null)
                {
                    throw new MissingMethodException(managerType.FullName, "GetLocalization(BepInPlugin)");
                }

                addFileByPath = customLocalizationType.GetMethod(
                    "AddFileByPath",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string), typeof(bool) },
                    null);

                if (addFileByPath == null)
                {
                    throw new MissingMethodException(customLocalizationType.FullName, "AddFileByPath(string, bool)");
                }

                if (Chainloader.PluginInfos.TryGetValue(JotunnGuid, out var jotunnInfo) &&
                    jotunnInfo != null &&
                    jotunnInfo.Metadata != null)
                {
                    JotunnMetadata = jotunnInfo.Metadata;
                }

                if (JotunnMetadata == null)
                {
                    throw new InvalidOperationException("Could not resolve Jotunn plugin metadata");
                }
            }

            public void AddFile(BepInPlugin metadata, string path, bool isJson)
            {
                var customLocalization = getLocalization.Invoke(localizationManager, new object[] { metadata });
                addFileByPath.Invoke(customLocalization, new object[] { path, isJson });
            }
        }
    }
}
