using System;
using System.Collections.Generic;
using System.IO;

namespace GLoader
{
    internal static class TargetLocator
    {
        public const string X64RuntimeDirectoryName = "x64-runtime";

        public static string Find(string loaderDirectory, LoaderOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.TargetPath))
                return Validate(options.TargetPath);

            var environmentTarget = Environment.GetEnvironmentVariable("GLOADER_TERRARIA");
            if (!string.IsNullOrWhiteSpace(environmentTarget))
                return Validate(environmentTarget);

            var x64RuntimeDirectory = Path.Combine(loaderDirectory, "gdeps", X64RuntimeDirectoryName);
            foreach (var runtimeName in new[] { "TerrariaRelease.dll", "TerrariaDebug.dll", "Terraria.dll" })
            {
                var runtimeTarget = Path.Combine(x64RuntimeDirectory, runtimeName);
                if (File.Exists(runtimeTarget))
                    return Path.GetFullPath(runtimeTarget);
            }

            var fileName = options.DedicatedServer ? "TerrariaServer.exe" : "Terraria.exe";
            var candidates = new List<string>
            {
                Path.Combine(loaderDirectory, fileName),
                Path.Combine(loaderDirectory, "..", fileName),
                Path.Combine(Environment.CurrentDirectory, fileName)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            throw new FileNotFoundException(
                "Could not find a Terraria target. gloader prefers the 64-bit runtime at " +
                "gdeps\\" + X64RuntimeDirectoryName + "\\TerrariaRelease.dll. " +
                "Stock Terraria.exe can only be used as the source for building that runtime, " +
                "not as an in-process target for 64-bit gloader.");
        }

        private static string Validate(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Terraria target does not exist.", fullPath);

            var extension = Path.GetExtension(fullPath);
            if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Terraria target must be a managed .exe or .dll.", nameof(path));
            }

            return fullPath;
        }
    }
}
