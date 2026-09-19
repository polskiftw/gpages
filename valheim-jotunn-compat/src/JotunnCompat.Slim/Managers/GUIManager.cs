using UnityEngine;

namespace Jotunn.Managers
{
    /// <summary>
    /// Minimal GUI surface for compatibility consumers that only need vanilla sprites.
    /// </summary>
    public sealed class GUIManager
    {
        private static GUIManager instance;
        public static GUIManager Instance => instance ??= new GUIManager();

        private GUIManager() { }

        public Sprite GetSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            // Upstream Jotunn ultimately falls back to PrefabManager.Cache for sprites.
            // The slim build skips Jotunn's full GUI framework/atlases but keeps the
            // generic asset lookup path, which also works before every sprite is loaded.
            return PrefabManager.Cache.GetPrefab<Sprite>(spriteName);
        }
    }
}
