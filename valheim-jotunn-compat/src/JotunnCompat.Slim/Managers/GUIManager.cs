using System.Linq;
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

            // Trophy/item icons are the common compatibility use case. Search already-loaded
            // sprites by exact name without initializing Jotunn's full GUI framework.
            return Resources.FindObjectsOfTypeAll<Sprite>()
                .FirstOrDefault(sprite => sprite && sprite.name == spriteName);
        }
    }
}
