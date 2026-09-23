using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GLoader
{
    internal sealed class ManagedMod
    {
        public ManagedMod(string name, string directoryPath, bool enabled, bool hasConflict)
        {
            Name = name;
            DirectoryPath = directoryPath;
            Enabled = enabled;
            HasConflict = hasConflict;
        }

        public string Name { get; }
        public string DirectoryPath { get; set; }
        public bool Enabled { get; set; }
        public bool HasConflict { get; }
    }

    internal static class ModManager
    {
        private const string DisabledSuffix = ".disabled";

        public static IReadOnlyList<ManagedMod> Discover(string modsDirectory)
        {
            Directory.CreateDirectory(modsDirectory);

            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.EnumerateDirectories(modsDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!ContainsSource(directory))
                {
                    continue;
                }

                var folderName = Path.GetFileName(directory);
                var name = IsDisabledFolder(folderName)
                    ? folderName.Substring(0, folderName.Length - DisabledSuffix.Length)
                    : folderName;

                if (!groups.TryGetValue(name, out var paths))
                {
                    paths = new List<string>();
                    groups.Add(name, paths);
                }

                paths.Add(directory);
            }

            var mods = new List<ManagedMod>();
            foreach (var pair in groups.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var enabledPath = pair.Value.FirstOrDefault(path => !IsDisabledFolder(Path.GetFileName(path)));
                var disabledPath = pair.Value.FirstOrDefault(path => IsDisabledFolder(Path.GetFileName(path)));
                var hasConflict = enabledPath != null && disabledPath != null;
                var selectedPath = enabledPath ?? disabledPath;

                mods.Add(new ManagedMod(
                    pair.Key,
                    selectedPath,
                    enabledPath != null,
                    hasConflict));
            }

            return mods;
        }

        public static void SetEnabled(string modsDirectory, ManagedMod mod, bool enabled)
        {
            if (mod == null)
                throw new ArgumentNullException(nameof(mod));

            if (mod.HasConflict)
            {
                throw new InvalidOperationException(
                    "Both enabled and disabled folders exist for " + mod.Name + ". Resolve the duplicate folders first.");
            }

            if (mod.Enabled == enabled)
            {
                return;
            }

            var targetName = enabled ? mod.Name : mod.Name + DisabledSuffix;
            var targetPath = Path.Combine(modsDirectory, targetName);

            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                throw new IOException("Cannot change " + mod.Name + " because this path already exists: " + targetPath);
            }

            Directory.Move(mod.DirectoryPath, targetPath);
            mod.DirectoryPath = targetPath;
            mod.Enabled = enabled;
        }

        public static IReadOnlyList<string> GetConfigurationFiles(ManagedMod mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.DirectoryPath) || !Directory.Exists(mod.DirectoryPath))
            {
                return Array.Empty<string>();
            }

            return Directory
                .EnumerateFiles(mod.DirectoryPath, "*.ini", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool ContainsSource(string directory)
        {
            try
            {
                return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Any();
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsDisabledFolder(string folderName)
        {
            return folderName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
