using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Jotunn.Extensions
{
    public static class TransformExtensions
    {
        public static Transform FindDeepChild(
            this Transform transform,
            string childName,
            global::Utils.IterativeSearchType searchType =
                global::Utils.IterativeSearchType.BreadthFirst)
        {
            if (!transform || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            var queue = new Queue<Transform>();
            queue.Enqueue(transform);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current != transform && current.name == childName)
                {
                    return current;
                }

                foreach (Transform child in current)
                {
                    queue.Enqueue(child);
                }
            }

            return null;
        }
    }
}

namespace Jotunn
{
    public static class ZNetExtension
    {
        public enum ZNetInstanceType
        {
            Local,
            Client,
            Server
        }

        public static bool IsLocalInstance(this ZNet znet)
        {
            return znet && znet.IsServer() && !znet.IsDedicated();
        }

        public static bool IsClientInstance(this ZNet znet)
        {
            return znet && !znet.IsServer() && !znet.IsDedicated();
        }

        public static bool IsServerInstance(this ZNet znet)
        {
            return znet && znet.IsServer() && znet.IsDedicated();
        }

        public static ZNetInstanceType GetInstanceType(this ZNet znet)
        {
            if (znet.IsLocalInstance())
            {
                return ZNetInstanceType.Local;
            }

            if (znet.IsClientInstance())
            {
                return ZNetInstanceType.Client;
            }

            return ZNetInstanceType.Server;
        }

        private static readonly FieldInfo AdminListField =
            AccessTools.Field(typeof(ZNet), "m_adminList");
        private static readonly MethodInfo ListContainsIdMethod =
            AccessTools.Method(typeof(ZNet), "ListContainsId");

        public static bool IsAdmin(this ZNet znet, long uid)
        {
            if (!znet || AdminListField == null ||
                ListContainsIdMethod == null)
            {
                return false;
            }

            var adminList = AdminListField.GetValue(znet);
            if (adminList == null)
            {
                return false;
            }

            var peer = znet.GetPeer(uid);
            if (peer == null || peer.m_socket == null)
            {
                return false;
            }

            var hostname = peer.m_socket.GetHostName();
            if (string.IsNullOrEmpty(hostname))
            {
                return false;
            }

            try
            {
                return (bool)ListContainsIdMethod.Invoke(
                    znet,
                    new[] { adminList, (object)hostname });
            }
            catch
            {
                return false;
            }
        }
    }
}

namespace Jotunn.Managers
{
    public sealed class MinimapManager
    {
        private static MinimapManager instance;
        public static MinimapManager Instance =>
            instance ??= new MinimapManager();

        public static event Action OnVanillaMapDataLoaded;

        private MinimapManager() { }

        internal static void InvokeVanillaMapDataLoaded()
        {
            OnVanillaMapDataLoaded?.Invoke();
        }
    }
}

namespace Jotunn.GUI
{
    /// <summary>
    /// Lightweight runtime color picker used by the slim compatibility layer.
    /// It deliberately uses Unity IMGUI so it does not require Jotunn's bundled UI assets.
    /// </summary>
    public sealed class ColorPicker : MonoBehaviour
    {
        public delegate void ColorEvent(Color c);

        private Color original;
        private Color current;
        private ColorEvent onChanged;
        private ColorEvent onSelected;
        private bool useAlpha;
        private string titleText;
        private Rect window = new Rect(80f, 80f, 360f, 230f);

        internal static void Open(
            Color originalColor,
            string message,
            ColorEvent changed,
            ColorEvent selected,
            bool alpha)
        {
            var obj = new GameObject("JotunnSlimColorPicker");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            var picker = obj.AddComponent<ColorPicker>();
            picker.original = originalColor;
            picker.current = originalColor;
            picker.onChanged = changed;
            picker.onSelected = selected;
            picker.useAlpha = alpha;
            picker.titleText = string.IsNullOrEmpty(message)
                ? "Color"
                : message;
        }

        private void OnGUI()
        {
            window = UnityEngine.GUI.Window(
                GetInstanceID(),
                window,
                DrawWindow,
                titleText);
        }

        private void DrawWindow(int id)
        {
            DrawChannel("R", ref current.r);
            DrawChannel("G", ref current.g);
            DrawChannel("B", ref current.b);
            if (useAlpha)
            {
                DrawChannel("A", ref current.a);
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Done"))
            {
                onSelected?.Invoke(current);
                Destroy(gameObject);
            }

            if (GUILayout.Button("Cancel"))
            {
                current = original;
                onChanged?.Invoke(original);
                onSelected?.Invoke(original);
                Destroy(gameObject);
            }

            GUILayout.EndHorizontal();
            UnityEngine.GUI.DragWindow();
        }

        private void DrawChannel(string label, ref float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(20f));
            var next = GUILayout.HorizontalSlider(value, 0f, 1f);
            GUILayout.Label(
                Mathf.RoundToInt(next * 255f).ToString(),
                GUILayout.Width(38f));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(next, value))
            {
                value = next;
                onChanged?.Invoke(current);
            }
        }
    }
}
