using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Jotunn.Utils
{
    public static class AssetUtils
    {
        public static AssetBundle LoadAssetBundleFromResources(string bundleName, Assembly resourceAssembly)
        {
            if (resourceAssembly == null) throw new ArgumentNullException(nameof(resourceAssembly));
            var resourceName = resourceAssembly.GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith(bundleName, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return null;
            using (var stream = resourceAssembly.GetManifestResourceStream(resourceName))
                return AssetBundle.LoadFromStream(stream);
        }

        public static string LoadTextFromResources(string fileName)
        {
            return LoadTextFromResources(fileName, Assembly.GetCallingAssembly());
        }

        private static string LoadTextFromResources(string fileName, Assembly assembly)
        {
            if (assembly == null) return null;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return null;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }
}
