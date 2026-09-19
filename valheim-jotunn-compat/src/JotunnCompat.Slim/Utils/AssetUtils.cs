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

        public static Texture2D LoadTexture(
            string texturePath,
            bool relativePath = true)
        {
            if (string.IsNullOrEmpty(texturePath))
            {
                return null;
            }

            var path = relativePath
                ? Path.Combine(BepInEx.Paths.PluginPath, texturePath)
                : texturePath;

            if (!File.Exists(path))
            {
                return null;
            }

            var extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "LoadTexture can only load png or jpg textures");
            }

            var texture = new Texture2D(2, 2);
            var conversionType = Type.GetType(
                "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            var loadImage = conversionType?.GetMethod(
                "LoadImage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Texture2D), typeof(byte[]) },
                null);

            if (loadImage == null)
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            var loaded = (bool)loadImage.Invoke(
                null,
                new object[] { texture, File.ReadAllBytes(path) });
            if (!loaded)
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            return texture;
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
