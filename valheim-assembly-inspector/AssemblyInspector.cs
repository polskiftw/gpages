using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace AssemblyInspectorMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class AssemblyInspector : BaseUnityPlugin
    {
        public const string PluginGuid = "claire.valheim.assemblyinspector";
        public const string PluginName = "Assembly Inspector";
        public const string PluginVersion = "1.1.1";
        private const string HarmonyId = PluginGuid + ".live-overrides";
        private const string UiHarmonyId = PluginGuid + ".ui-input";
        private const int WindowId = 845112;

        private static AssemblyInspector _instance;
        private static bool _uiCapturingInput;

        private static readonly BindingFlags DeclaredMembers =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly BindingFlags InheritedMembers =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

        private static readonly object OverrideLock = new object();
        private static readonly Dictionary<MethodBase, object> RuntimeOverrides =
            new Dictionary<MethodBase, object>();

        private readonly HashSet<MethodInfo> _livePatchedMethods =
            new HashSet<MethodInfo>();

        private ConfigEntry<KeyCode> _toggleKey;
        private ConfigEntry<string> _assemblyName;
        private ConfigEntry<int> _fontSize;

        private Harmony _harmony;
        private Harmony _uiHarmony;
        private Assembly _targetAssembly;
        private List<Type> _types = new List<Type>();

        private Type _selectedType;
        private MethodInfo _selectedMethod;
        private PropertyInfo _selectedProperty;
        private FieldInfo _selectedField;

        private string _typeSearch = string.Empty;
        private string _memberSearch = string.Empty;
        private string _returnSearch = string.Empty;
        private string _overrideText = string.Empty;
        private string _status = "Open in game after Valheim has loaded its managed assemblies.";

        private bool _visible;
        private bool _includeInherited;
        private bool _showSpecialMethods;
        private MemberTab _tab = MemberTab.Methods;

        private Rect _windowRect = new Rect(24f, 24f, 1650f, 960f);
        private Vector2 _typeScroll;
        private Vector2 _memberScroll;
        private Vector2 _detailScroll;

        private bool _cursorSaved;
        private bool _savedCursorVisible;
        private CursorLockMode _savedCursorLock;

        private Type _zCursorType;
        private PropertyInfo _zCursorLockState;
        private PropertyInfo _zCursorIsRequested;
        private PropertyInfo _zCursorIsVisible;
        private MethodInfo _zCursorShow;
        private MethodInfo _zCursorSetRequested;
        private MethodInfo _zCursorSetVisible;
        private MethodInfo _zInputResetAllButtonStates;
        private object _savedZCursorLock;
        private bool _savedZCursorRequested;
        private bool _savedZCursorVisible;
        private bool _savedZCursorState;

        private GUISkin _inspectorSkin;
        private int _skinFontSize;
        private Texture2D _windowTexture;
        private Texture2D _buttonTexture;
        private Texture2D _buttonHoverTexture;
        private Texture2D _buttonActiveTexture;
        private Texture2D _fieldTexture;
        private Texture2D _fieldFocusedTexture;

        private enum MemberTab
        {
            Methods,
            Properties,
            Fields
        }

        private void Awake()
        {
            _instance = this;

            _toggleKey = Config.Bind(
                "General",
                "ToggleKey",
                KeyCode.F9,
                "Key used to open/close Assembly Inspector.");

            _assemblyName = Config.Bind(
                "General",
                "AssemblyName",
                "assembly_valheim",
                "Managed assembly to browse by simple assembly name.");

            _fontSize = Config.Bind(
                "Interface",
                "FontSize",
                16,
                "Assembly Inspector font size. Values from 12 to 28 are used.");

            _harmony = new Harmony(HarmonyId);
            _uiHarmony = new Harmony(UiHarmonyId);

            ResolveAssembly();
            ResolveValheimUiIntegration();
            PatchValheimUiInput();

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded. Toggle: " + _toggleKey.Value + ".");
        }

        private void OnDestroy()
        {
            _uiCapturingInput = false;

            try
            {
                _uiHarmony?.UnpatchSelf();
            }
            catch
            {
                // Best effort during shutdown.
            }

            try
            {
                _harmony?.UnpatchSelf();
            }
            catch
            {
                // Best effort during shutdown.
            }

            lock (OverrideLock)
                RuntimeOverrides.Clear();

            RestoreCursor();
            DestroyInspectorSkin();

            if (ReferenceEquals(_instance, this))
                _instance = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey.Value))
                SetVisible(!_visible);

            if (_visible)
                ForceCursor();
        }

        private void LateUpdate()
        {
            if (_visible)
                ForceCursor();
        }

        private void OnGUI()
        {
            if (!_visible)
                return;

            ForceCursor();
            EnsureInspectorSkin();

            GUISkin previousSkin = GUI.skin;
            GUI.skin = _inspectorSkin;

            try
            {
                float maxWidth = Mathf.Max(760f, Screen.width - 20f);
                float maxHeight = Mathf.Max(560f, Screen.height - 20f);
                _windowRect.width = Mathf.Min(_windowRect.width, maxWidth);
                _windowRect.height = Mathf.Min(_windowRect.height, maxHeight);
                _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
                _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));

                _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, PluginName + " " + PluginVersion);
            }
            finally
            {
                GUI.skin = previousSkin;
            }
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;

            _visible = visible;
            _uiCapturingInput = visible;

            if (_visible)
            {
                SaveCursor();
                ResetGameInputState();
                ForceCursor();
                ResolveAssembly();
            }
            else
            {
                ResetGameInputState();
                RestoreCursor();
            }
        }

        private void SaveCursor()
        {
            if (_cursorSaved)
                return;

            _savedCursorVisible = Cursor.visible;
            _savedCursorLock = Cursor.lockState;

            try
            {
                if (_zCursorType != null)
                {
                    _savedZCursorLock = _zCursorLockState?.GetValue(null, null);
                    _savedZCursorRequested = ReadStaticBool(_zCursorIsRequested);
                    _savedZCursorVisible = ReadStaticBool(_zCursorIsVisible);
                    _savedZCursorState = true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Could not snapshot ZCursor state: " + ex.Message);
                _savedZCursorState = false;
            }

            _cursorSaved = true;
        }

        private void ForceCursor()
        {
            try
            {
                if (_zCursorType != null)
                {
                    _zCursorLockState?.SetValue(null, CursorLockMode.None, null);
                    _zCursorShow?.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Could not apply ZCursor override: " + ex.Message);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestoreCursor()
        {
            if (!_cursorSaved)
                return;

            _uiCapturingInput = false;

            if (!RestoreCurrentSceneCursor())
            {
                try
                {
                    if (_savedZCursorState && _zCursorType != null)
                    {
                        if (_savedZCursorLock != null)
                            _zCursorLockState?.SetValue(null, _savedZCursorLock, null);
                        _zCursorSetRequested?.Invoke(null, new object[] { _savedZCursorRequested });
                        _zCursorSetVisible?.Invoke(null, new object[] { _savedZCursorVisible });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug("Could not restore ZCursor state: " + ex.Message);
                }

                Cursor.lockState = _savedCursorLock;
                Cursor.visible = _savedCursorVisible;
            }

            _savedZCursorState = false;
            _cursorSaved = false;
        }

        private void ResolveValheimUiIntegration()
        {
            _zCursorType = FindLoadedType("ZCursor");
            if (_zCursorType != null)
            {
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _zCursorLockState = _zCursorType.GetProperty("LockState", flags);
                _zCursorIsRequested = _zCursorType.GetProperty("IsRequested", flags);
                _zCursorIsVisible = _zCursorType.GetProperty("IsVisible", flags);
                _zCursorShow = _zCursorType.GetMethod("Show", flags, null, Type.EmptyTypes, null);
                _zCursorSetRequested = _zCursorType.GetMethod("SetRequested", flags, null, new[] { typeof(bool) }, null);
                _zCursorSetVisible = _zCursorType.GetMethod("SetVisible", flags, null, new[] { typeof(bool) }, null);
            }

            Type zInput = FindLoadedType("ZInput");
            if (zInput != null)
            {
                _zInputResetAllButtonStates = zInput.GetMethod(
                    "ResetAllButtonStates",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
            }
        }

        private void PatchValheimUiInput()
        {
            try
            {
                HarmonyMethod cursorPrefix = new HarmonyMethod(
                    typeof(AssemblyInspector).GetMethod(nameof(CursorOwnerPrefix), BindingFlags.Static | BindingFlags.NonPublic));
                HarmonyMethod takeInputPostfix = new HarmonyMethod(
                    typeof(AssemblyInspector).GetMethod(nameof(TakeInputPostfix), BindingFlags.Static | BindingFlags.NonPublic));
                HarmonyMethod textInputPostfix = new HarmonyMethod(
                    typeof(AssemblyInspector).GetMethod(nameof(TextInputVisiblePostfix), BindingFlags.Static | BindingFlags.NonPublic));
                HarmonyMethod mouseDeltaPostfix = new HarmonyMethod(
                    typeof(AssemblyInspector).GetMethod(nameof(MouseDeltaPostfix), BindingFlags.Static | BindingFlags.NonPublic));
                HarmonyMethod mouseFloatPostfix = new HarmonyMethod(
                    typeof(AssemblyInspector).GetMethod(nameof(MouseFloatPostfix), BindingFlags.Static | BindingFlags.NonPublic));

                PatchNamedMethods("GameCamera", "UpdateMouseCapture", cursorPrefix, null);
                PatchNamedMethods("Menu", "UpdateCursor", cursorPrefix, null);
                PatchNamedMethods("FejdStartup", "UpdateCursor", cursorPrefix, null);

                PatchNamedMethods("PlayerController", "TakeInput", null, takeInputPostfix, typeof(bool));
                PatchNamedMethods("Player", "TakeInput", null, takeInputPostfix, typeof(bool));
                PatchNamedMethods("TextInput", "IsVisible", null, textInputPostfix, typeof(bool));
                PatchNamedMethods("ZInput", "GetMouseDelta", null, mouseDeltaPostfix, typeof(Vector2));
                PatchNamedMethods("ZInput", "GetMouseScrollWheel", null, mouseFloatPostfix, typeof(float));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not install Assembly Inspector UI input patches: " + ex);
            }
        }

        private void PatchNamedMethods(
            string typeName,
            string methodName,
            HarmonyMethod prefix,
            HarmonyMethod postfix,
            Type returnType = null)
        {
            Type type = FindLoadedType(typeName);
            if (type == null)
            {
                Logger.LogDebug("UI integration type not found: " + typeName);
                return;
            }

            MethodInfo[] methods = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method =>
                    method.Name == methodName &&
                    (returnType == null || method.ReturnType == returnType))
                .ToArray();

            foreach (MethodInfo method in methods)
                _uiHarmony.Patch(method, prefix, postfix);

            if (methods.Length == 0)
                Logger.LogDebug("UI integration method not found: " + typeName + "." + methodName);
        }

        [HarmonyPriority(Priority.First)]
        private static bool CursorOwnerPrefix()
        {
            if (!_uiCapturingInput || !Application.isFocused)
                return true;

            _instance?.ForceCursor();
            return false;
        }

        [HarmonyPriority(Priority.Last)]
        private static void TakeInputPostfix(ref bool __result)
        {
            if (_uiCapturingInput)
                __result = false;
        }

        [HarmonyPriority(Priority.Last)]
        private static void TextInputVisiblePostfix(ref bool __result)
        {
            if (_uiCapturingInput)
                __result = true;
        }

        [HarmonyPriority(Priority.Last)]
        private static void MouseDeltaPostfix(ref Vector2 __result)
        {
            if (_uiCapturingInput)
                __result = Vector2.zero;
        }

        [HarmonyPriority(Priority.Last)]
        private static void MouseFloatPostfix(ref float __result)
        {
            if (_uiCapturingInput)
                __result = 0f;
        }

        private void ResetGameInputState()
        {
            try
            {
                _zInputResetAllButtonStates?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Could not reset ZInput button states: " + ex.Message);
            }
        }

        private bool RestoreCurrentSceneCursor()
        {
            bool handled = false;

            try
            {
                object gameCamera = GetStaticInstance("GameCamera");
                if (IsAliveUnityObject(gameCamera))
                {
                    InvokeInstanceMethod(gameCamera, "UpdateMouseCapture");
                    handled = true;

                    object menu = GetStaticInstance("Menu");
                    if (IsAliveUnityObject(menu) && InvokeStaticBool("Menu", "IsActive"))
                        InvokeInstanceMethod(menu, "UpdateCursor");

                    return true;
                }

                object fejdStartup = GetStaticInstance("FejdStartup");
                if (IsAliveUnityObject(fejdStartup))
                {
                    InvokeInstanceMethod(fejdStartup, "UpdateCursor");
                    return true;
                }

                object activeMenu = GetStaticInstance("Menu");
                if (IsAliveUnityObject(activeMenu) && InvokeStaticBool("Menu", "IsActive"))
                {
                    InvokeInstanceMethod(activeMenu, "UpdateCursor");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Could not hand cursor back to Valheim: " + ex.Message);
            }

            return handled;
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static object GetStaticInstance(string typeName)
        {
            Type type = FindLoadedType(typeName);
            if (type == null)
                return null;

            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = type.GetProperty("instance", flags);
            if (property != null)
                return property.GetValue(null, null);

            FieldInfo field = type.GetField("instance", flags);
            return field?.GetValue(null);
        }

        private static bool InvokeStaticBool(string typeName, string methodName)
        {
            Type type = FindLoadedType(typeName);
            MethodInfo method = type?.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            return method != null && Convert.ToBoolean(method.Invoke(null, null), CultureInfo.InvariantCulture);
        }

        private static void InvokeInstanceMethod(object instance, string methodName)
        {
            if (instance == null)
                return;

            MethodInfo method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            method?.Invoke(instance, null);
        }

        private static bool IsAliveUnityObject(object value)
        {
            if (value == null)
                return false;

            UnityEngine.Object unityObject = value as UnityEngine.Object;
            return unityObject == null || unityObject;
        }

        private static bool ReadStaticBool(PropertyInfo property)
        {
            return property != null &&
                   Convert.ToBoolean(property.GetValue(null, null), CultureInfo.InvariantCulture);
        }

        private void EnsureInspectorSkin()
        {
            int fontSize = Mathf.Clamp(_fontSize == null ? 16 : _fontSize.Value, 12, 28);
            if (_inspectorSkin != null && _skinFontSize == fontSize)
                return;

            DestroyInspectorSkin();

            _skinFontSize = fontSize;
            _inspectorSkin = UnityEngine.Object.Instantiate(GUI.skin);
            _inspectorSkin.hideFlags = HideFlags.HideAndDontSave;

            _windowTexture = MakeTexture(new Color(0.045f, 0.047f, 0.055f, 0.985f));
            _buttonTexture = MakeTexture(new Color(0.16f, 0.17f, 0.20f, 1f));
            _buttonHoverTexture = MakeTexture(new Color(0.24f, 0.26f, 0.31f, 1f));
            _buttonActiveTexture = MakeTexture(new Color(0.10f, 0.40f, 0.72f, 1f));
            _fieldTexture = MakeTexture(new Color(0.09f, 0.095f, 0.11f, 1f));
            _fieldFocusedTexture = MakeTexture(new Color(0.13f, 0.14f, 0.17f, 1f));

            Color text = new Color(0.96f, 0.97f, 0.99f, 1f);
            Color muted = new Color(0.82f, 0.84f, 0.88f, 1f);

            _inspectorSkin.window.fontSize = fontSize + 2;
            _inspectorSkin.window.normal.textColor = text;
            _inspectorSkin.window.normal.background = _windowTexture;
            _inspectorSkin.window.padding = new RectOffset(14, 14, 31, 14);

            _inspectorSkin.label.fontSize = fontSize;
            _inspectorSkin.label.normal.textColor = text;

            _inspectorSkin.button.fontSize = fontSize;
            _inspectorSkin.button.normal.textColor = text;
            _inspectorSkin.button.hover.textColor = Color.white;
            _inspectorSkin.button.active.textColor = Color.white;
            _inspectorSkin.button.normal.background = _buttonTexture;
            _inspectorSkin.button.hover.background = _buttonHoverTexture;
            _inspectorSkin.button.active.background = _buttonActiveTexture;
            _inspectorSkin.button.padding = new RectOffset(8, 8, 6, 6);

            _inspectorSkin.textField.fontSize = fontSize;
            _inspectorSkin.textField.normal.textColor = text;
            _inspectorSkin.textField.focused.textColor = Color.white;
            _inspectorSkin.textField.hover.textColor = Color.white;
            _inspectorSkin.textField.normal.background = _fieldTexture;
            _inspectorSkin.textField.focused.background = _fieldFocusedTexture;
            _inspectorSkin.textField.hover.background = _fieldFocusedTexture;
            _inspectorSkin.textField.padding = new RectOffset(7, 7, 5, 5);

            _inspectorSkin.toggle.fontSize = fontSize;
            _inspectorSkin.toggle.normal.textColor = muted;
            _inspectorSkin.toggle.hover.textColor = Color.white;
            _inspectorSkin.toggle.onNormal.textColor = Color.white;
            _inspectorSkin.toggle.onHover.textColor = Color.white;

            _inspectorSkin.verticalScrollbar.fixedWidth = 18f;
            _inspectorSkin.horizontalScrollbar.fixedHeight = 18f;
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private void DestroyInspectorSkin()
        {
            DestroyTexture(ref _windowTexture);
            DestroyTexture(ref _buttonTexture);
            DestroyTexture(ref _buttonHoverTexture);
            DestroyTexture(ref _buttonActiveTexture);
            DestroyTexture(ref _fieldTexture);
            DestroyTexture(ref _fieldFocusedTexture);

            if (_inspectorSkin != null)
            {
                UnityEngine.Object.Destroy(_inspectorSkin);
                _inspectorSkin = null;
            }
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
                return;

            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Assembly:", GUILayout.Width(66f));
            string assemblyName = GUILayout.TextField(_assemblyName.Value, GUILayout.Width(180f));
            if (!string.Equals(assemblyName, _assemblyName.Value, StringComparison.Ordinal))
                _assemblyName.Value = assemblyName;

            if (GUILayout.Button("Reload", GUILayout.Width(74f)))
                ResolveAssembly();

            if (GUILayout.Button("Clear all live overrides", GUILayout.Width(165f)))
                ClearAllOverrides();

            GUILayout.FlexibleSpace();
            GUILayout.Label(_status ?? string.Empty);
            GUILayout.Space(8f);
            if (GUILayout.Button("Close", GUILayout.Width(68f)))
                SetVisible(false);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();

            DrawTypesColumn();
            GUILayout.Space(7f);
            DrawMembersColumn();
            GUILayout.Space(7f);
            DrawDetailsColumn();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 23f));
        }

        private void DrawTypesColumn()
        {
            GUILayout.BeginVertical(GUILayout.Width(360f));

            GUILayout.Label("Classes");
            string nextSearch = GUILayout.TextField(_typeSearch ?? string.Empty);
            if (!string.Equals(nextSearch, _typeSearch, StringComparison.Ordinal))
            {
                _typeSearch = nextSearch;
                _typeScroll = Vector2.zero;
            }

            GUILayout.Space(4f);
            _typeScroll = GUILayout.BeginScrollView(_typeScroll);

            int shown = 0;
            foreach (Type type in _types)
            {
                if (!MatchesType(type))
                    continue;

                shown++;
                string label = ReferenceEquals(type, _selectedType) ? "> " + DisplayTypeName(type) : DisplayTypeName(type);
                if (GUILayout.Button(label, GUILayout.Height(36f)))
                    SelectType(type);
            }

            GUILayout.EndScrollView();
            GUILayout.Label("Showing " + shown + " / " + _types.Count);

            GUILayout.EndVertical();
        }

        private void DrawMembersColumn()
        {
            GUILayout.BeginVertical(GUILayout.Width(560f));

            if (_selectedType == null)
            {
                GUILayout.Label("Select a class.");
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label("Class: " + FriendlyType(_selectedType));
            GUILayout.Label("Base: " + FriendlyType(_selectedType.BaseType));

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_tab == MemberTab.Methods, "Methods", GUI.skin.button))
                SetTab(MemberTab.Methods);
            if (GUILayout.Toggle(_tab == MemberTab.Properties, "Properties", GUI.skin.button))
                SetTab(MemberTab.Properties);
            if (GUILayout.Toggle(_tab == MemberTab.Fields, "Fields", GUI.skin.button))
                SetTab(MemberTab.Fields);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Find:", GUILayout.Width(42f));
            _memberSearch = GUILayout.TextField(_memberSearch ?? string.Empty);
            GUILayout.EndHorizontal();

            if (_tab != MemberTab.Fields)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Returns:", GUILayout.Width(58f));
                _returnSearch = GUILayout.TextField(_returnSearch ?? string.Empty);
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            _includeInherited = GUILayout.Toggle(_includeInherited, "Include inherited");
            if (_tab == MemberTab.Methods)
                _showSpecialMethods = GUILayout.Toggle(_showSpecialMethods, "Show getters/setters");
            GUILayout.EndHorizontal();

            _memberScroll = GUILayout.BeginScrollView(_memberScroll);
            switch (_tab)
            {
                case MemberTab.Methods:
                    DrawMethodList();
                    break;
                case MemberTab.Properties:
                    DrawPropertyList();
                    break;
                case MemberTab.Fields:
                    DrawFieldList();
                    break;
            }
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        private void DrawMethodList()
        {
            foreach (MethodInfo method in GetMethods(_selectedType))
            {
                if (!_showSpecialMethods && method.IsSpecialName)
                    continue;
                if (!MatchesMember(method.Name))
                    continue;
                if (!MatchesReturn(method.ReturnType))
                    continue;

                string label = FormatMethodShort(method);
                if (ReferenceEquals(method, _selectedMethod))
                    label = "> " + label;

                if (GUILayout.Button(label, GUILayout.Height(42f)))
                    SelectMethod(method);
            }
        }

        private void DrawPropertyList()
        {
            foreach (PropertyInfo property in GetProperties(_selectedType))
            {
                if (!MatchesMember(property.Name))
                    continue;
                if (!MatchesReturn(property.PropertyType))
                    continue;

                MethodInfo getter = property.GetGetMethod(true);
                string access = getter == null ? "write-only" : "get";
                string label = property.Name + " -> " + FriendlyType(property.PropertyType) + " [" + access + "]";
                if (ReferenceEquals(property, _selectedProperty))
                    label = "> " + label;

                if (GUILayout.Button(label, GUILayout.Height(40f)))
                    SelectProperty(property);
            }
        }

        private void DrawFieldList()
        {
            foreach (FieldInfo field in GetFields(_selectedType))
            {
                if (!MatchesMember(field.Name))
                    continue;

                string label = field.Name + " : " + FriendlyType(field.FieldType);
                if (ReferenceEquals(field, _selectedField))
                    label = "> " + label;

                if (GUILayout.Button(label, GUILayout.Height(40f)))
                    SelectField(field);
            }
        }

        private void DrawDetailsColumn()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("Details");
            _detailScroll = GUILayout.BeginScrollView(_detailScroll);

            if (_selectedMethod != null)
                DrawMethodDetails(_selectedMethod, _selectedProperty);
            else if (_selectedField != null)
                DrawFieldDetails(_selectedField);
            else
                GUILayout.Label("Select a method, property, or field.");

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawMethodDetails(MethodInfo method, PropertyInfo property)
        {
            if (property != null)
            {
                GUILayout.Label("PROPERTY");
                DetailLine("Name", property.Name);
                DetailLine("Type", FriendlyType(property.PropertyType));
                DetailLine("Getter", method.Name);
                GUILayout.Space(8f);
            }
            else
            {
                GUILayout.Label("METHOD");
            }

            DetailLine("Class", FriendlyType(method.DeclaringType));
            DetailLine("Signature", FormatMethodSignature(method));
            DetailLine("Return type", FriendlyType(method.ReturnType));
            DetailLine("Visibility", Visibility(method));
            DetailLine("Static", method.IsStatic ? "yes" : "no");
            DetailLine("Virtual", method.IsVirtual ? "yes" : "no");
            DetailLine("Abstract", method.IsAbstract ? "yes" : "no");
            DetailLine("Generic args", method.IsGenericMethod ? method.GetGenericArguments().Length.ToString() : "0");
            DetailLine("Metadata token", SafeMetadataToken(method));

            GUILayout.Space(8f);
            GUILayout.Label("Parameters");
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                GUILayout.Label("(none)");
            }
            else
            {
                foreach (ParameterInfo parameter in parameters)
                {
                    string modifier = parameter.ParameterType.IsByRef ? "ref " : string.Empty;
                    GUILayout.Label(modifier + FriendlyType(parameter.ParameterType) + " " + parameter.Name);
                }
            }

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy signature"))
            {
                GUIUtility.systemCopyBuffer = FormatMethodSignature(method);
                _status = "Signature copied.";
            }
            if (GUILayout.Button("Copy reflection target"))
            {
                GUIUtility.systemCopyBuffer = BuildReflectionTargetSnippet(method);
                _status = "Reflection target copied.";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(14f);
            GUILayout.Label("Return override");

            if (method.ReturnType == typeof(void))
            {
                GUILayout.Label("This method returns void, so there is no return value to override.");
                return;
            }

            string unsupportedReason;
            bool canLiveOverride = CanOverride(method, out unsupportedReason);
            if (!canLiveOverride)
            {
                GUILayout.Label("Live override unavailable: " + unsupportedReason);
            }
            else
            {
                DrawOverrideEditor(method);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Generated mod source");
            if (CanGenerateScalarPatch(method.ReturnType))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy postfix mod"))
                {
                    object value;
                    string error;
                    if (TryParseOverride(method.ReturnType, out value, out error))
                    {
                        GUIUtility.systemCopyBuffer = BuildGeneratedMod(method, value, false);
                        _status = "Postfix override mod copied. Original method still runs.";
                    }
                    else
                    {
                        _status = error;
                    }
                }

                if (GUILayout.Button("Copy hard override mod"))
                {
                    object value;
                    string error;
                    if (TryParseOverride(method.ReturnType, out value, out error))
                    {
                        GUIUtility.systemCopyBuffer = BuildGeneratedMod(method, value, true);
                        _status = "Hard override mod copied. Original method is skipped.";
                    }
                    else
                    {
                        _status = error;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Label("Postfix is the safer default: Valheim runs the original method, then the return value is replaced. Hard override sets the result and skips the original method completely.");
            }
            else
            {
                GUILayout.Label("Automatic ready-to-build source is limited to bool, string, char, enum, decimal, and primitive numeric return types.");
            }
        }

        private void DrawOverrideEditor(MethodInfo method)
        {
            bool active = IsOverrideActive(method);
            if (active)
            {
                object current = GetCurrentOverride(method);
                GUILayout.Label("LIVE OVERRIDE ACTIVE: " + FormatValue(current));
            }

            if (method.ReturnType == typeof(bool))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Force TRUE live"))
                    ApplyLiveOverride(method, true);
                if (GUILayout.Button("Force FALSE live"))
                    ApplyLiveOverride(method, false);
                GUILayout.EndHorizontal();
            }
            else
            {
                if (method.ReturnType.IsEnum)
                    GUILayout.Label("Enum values: " + string.Join(", ", Enum.GetNames(method.ReturnType)));

                GUILayout.BeginHorizontal();
                GUILayout.Label("Value:", GUILayout.Width(44f));
                _overrideText = GUILayout.TextField(_overrideText ?? string.Empty);
                if (GUILayout.Button("Apply live", GUILayout.Width(92f)))
                {
                    object value;
                    string error;
                    if (TryParseOverride(method.ReturnType, out value, out error))
                        ApplyLiveOverride(method, value);
                    else
                        _status = error;
                }
                GUILayout.EndHorizontal();
            }

            if (active && GUILayout.Button("Remove this live override"))
                RemoveLiveOverride(method);

            GUILayout.Label("Live overrides use a Harmony postfix: the original method still executes, but callers receive the forced value.");
        }

        private void DrawFieldDetails(FieldInfo field)
        {
            GUILayout.Label("FIELD");
            DetailLine("Class", FriendlyType(field.DeclaringType));
            DetailLine("Name", field.Name);
            DetailLine("Type", FriendlyType(field.FieldType));
            DetailLine("Visibility", Visibility(field));
            DetailLine("Static", field.IsStatic ? "yes" : "no");
            DetailLine("Readonly", field.IsInitOnly ? "yes" : "no");
            DetailLine("Constant", field.IsLiteral ? "yes" : "no");

            if (field.IsStatic)
            {
                try
                {
                    DetailLine("Current value", FormatValue(field.GetValue(null)));
                }
                catch (Exception ex)
                {
                    DetailLine("Current value", "<unreadable: " + ex.GetType().Name + ">");
                }
            }
            else
            {
                DetailLine("Current value", "<requires an instance>");
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Copy declaration"))
            {
                GUIUtility.systemCopyBuffer = Visibility(field) + " " +
                    FriendlyType(field.FieldType) + " " + field.DeclaringType.FullName + "." + field.Name;
                _status = "Field declaration copied.";
            }
        }

        private void DetailLine(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(112f));
            GUILayout.TextField(value ?? string.Empty);
            GUILayout.EndHorizontal();
        }

        private void ResolveAssembly()
        {
            _targetAssembly = null;
            _types.Clear();
            _selectedType = null;
            ClearMemberSelection();

            string wanted = (_assemblyName.Value ?? string.Empty).Trim();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string simple = assembly.GetName().Name;
                if (string.Equals(simple, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    _targetAssembly = assembly;
                    break;
                }
            }

            if (_targetAssembly == null)
            {
                _status = "Assembly '" + wanted + "' is not loaded yet.";
                return;
            }

            _types = GetLoadableTypes(_targetAssembly)
                .Where(type => type != null && type.IsClass)
                .OrderBy(DisplayTypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _status = "Loaded " + _types.Count + " classes from " + _targetAssembly.GetName().Name + ".";
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        private void SelectType(Type type)
        {
            _selectedType = type;
            _memberScroll = Vector2.zero;
            _detailScroll = Vector2.zero;
            _memberSearch = string.Empty;
            _returnSearch = string.Empty;
            ClearMemberSelection();
        }

        private void SetTab(MemberTab tab)
        {
            if (_tab == tab)
                return;

            _tab = tab;
            _memberScroll = Vector2.zero;
            _detailScroll = Vector2.zero;
            ClearMemberSelection();
        }

        private void ClearMemberSelection()
        {
            _selectedMethod = null;
            _selectedProperty = null;
            _selectedField = null;
            _overrideText = string.Empty;
        }

        private void SelectMethod(MethodInfo method)
        {
            _selectedMethod = method;
            _selectedProperty = null;
            _selectedField = null;
            _overrideText = DefaultOverrideText(method.ReturnType);
            _detailScroll = Vector2.zero;
        }

        private void SelectProperty(PropertyInfo property)
        {
            _selectedProperty = property;
            _selectedField = null;
            _selectedMethod = property.GetGetMethod(true);
            _overrideText = _selectedMethod == null ? string.Empty : DefaultOverrideText(_selectedMethod.ReturnType);
            _detailScroll = Vector2.zero;

            if (_selectedMethod == null)
                _status = "Selected property has no getter, so it cannot return a value.";
        }

        private void SelectField(FieldInfo field)
        {
            _selectedField = field;
            _selectedMethod = null;
            _selectedProperty = null;
            _overrideText = string.Empty;
            _detailScroll = Vector2.zero;
        }

        private MethodInfo[] GetMethods(Type type)
        {
            BindingFlags flags = _includeInherited ? InheritedMembers : DeclaredMembers;
            return type.GetMethods(flags)
                .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(method => method.GetParameters().Length)
                .ToArray();
        }

        private PropertyInfo[] GetProperties(Type type)
        {
            BindingFlags flags = _includeInherited ? InheritedMembers : DeclaredMembers;
            return type.GetProperties(flags)
                .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private FieldInfo[] GetFields(Type type)
        {
            BindingFlags flags = _includeInherited ? InheritedMembers : DeclaredMembers;
            return type.GetFields(flags)
                .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private bool MatchesType(Type type)
        {
            string search = (_typeSearch ?? string.Empty).Trim();
            if (search.Length == 0)
                return true;

            return ContainsIgnoreCase(DisplayTypeName(type), search) ||
                   ContainsIgnoreCase(type.FullName, search);
        }

        private bool MatchesMember(string name)
        {
            string search = (_memberSearch ?? string.Empty).Trim();
            return search.Length == 0 || ContainsIgnoreCase(name, search);
        }

        private bool MatchesReturn(Type returnType)
        {
            string search = (_returnSearch ?? string.Empty).Trim();
            if (search.Length == 0)
                return true;

            return ContainsIgnoreCase(FriendlyType(returnType), search) ||
                   ContainsIgnoreCase(returnType == null ? null : returnType.FullName, search);
        }

        private static bool ContainsIgnoreCase(string text, string search)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyLiveOverride(MethodInfo method, object value)
        {
            string reason;
            if (!CanOverride(method, out reason))
            {
                _status = "Cannot override: " + reason;
                return;
            }

            try
            {
                if (!_livePatchedMethods.Contains(method))
                {
                    MethodInfo factory = typeof(AssemblyInspector).GetMethod(
                        nameof(LivePostfixFactory),
                        BindingFlags.Static | BindingFlags.NonPublic);

                    if (factory == null)
                        throw new MissingMethodException(typeof(AssemblyInspector).FullName, nameof(LivePostfixFactory));

                    _harmony.Patch(method, postfix: new HarmonyMethod(factory));
                    _livePatchedMethods.Add(method);
                }

                lock (OverrideLock)
                    RuntimeOverrides[method] = value;

                _status = FormatMethodShort(method) + " now returns " + FormatValue(value) + " to callers.";
            }
            catch (Exception ex)
            {
                _status = "Patch failed: " + ex.GetType().Name + ": " + ex.Message;
                Logger.LogWarning(_status);
            }
        }

        private void RemoveLiveOverride(MethodInfo method)
        {
            try
            {
                _harmony.Unpatch(method, HarmonyPatchType.Postfix, HarmonyId);
                _livePatchedMethods.Remove(method);
                lock (OverrideLock)
                    RuntimeOverrides.Remove(method);
                _status = "Removed live override from " + FormatMethodShort(method) + ".";
            }
            catch (Exception ex)
            {
                _status = "Unpatch failed: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private void ClearAllOverrides()
        {
            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not unpatch all Assembly Inspector overrides: " + ex.Message);
            }

            _livePatchedMethods.Clear();
            lock (OverrideLock)
                RuntimeOverrides.Clear();

            _status = "Cleared all live return overrides.";
        }

        private static DynamicMethod LivePostfixFactory(MethodBase originalMethod)
        {
            MethodInfo target = originalMethod as MethodInfo;
            if (target == null)
                throw new ArgumentException("Assembly Inspector live overrides require a MethodInfo target.", nameof(originalMethod));

            return BuildDynamicPostfix(target);
        }

        private static DynamicMethod BuildDynamicPostfix(MethodInfo target)
        {
            Type returnType = target.ReturnType;
            DynamicMethod method = new DynamicMethod(
                "AssemblyInspector_Postfix_" + SafeMetadataToken(target),
                typeof(void),
                new[] { typeof(MethodBase), returnType.MakeByRefType() },
                typeof(AssemblyInspector).Module,
                true);

            method.DefineParameter(1, ParameterAttributes.None, "__originalMethod");
            method.DefineParameter(2, ParameterAttributes.None, "__result");

            ILGenerator il = method.GetILGenerator();
            MethodInfo getValue = typeof(AssemblyInspector).GetMethod(
                "GetOverrideValue",
                BindingFlags.Static | BindingFlags.NonPublic);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getValue);

            if (returnType.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, returnType);
                il.Emit(OpCodes.Stobj, returnType);
            }
            else
            {
                il.Emit(OpCodes.Castclass, returnType);
                il.Emit(OpCodes.Stind_Ref);
            }

            il.Emit(OpCodes.Ret);
            return method;
        }

        private static object GetOverrideValue(MethodBase method)
        {
            lock (OverrideLock)
            {
                object value;
                if (RuntimeOverrides.TryGetValue(method, out value))
                    return value;
            }

            throw new InvalidOperationException("No Assembly Inspector override is registered for " + method + ".");
        }

        private static bool IsOverrideActive(MethodInfo method)
        {
            lock (OverrideLock)
                return RuntimeOverrides.ContainsKey(method);
        }

        private static object GetCurrentOverride(MethodInfo method)
        {
            lock (OverrideLock)
            {
                object value;
                return RuntimeOverrides.TryGetValue(method, out value) ? value : null;
            }
        }

        private static bool CanOverride(MethodInfo method, out string reason)
        {
            if (method == null)
            {
                reason = "no method selected";
                return false;
            }

            Type returnType = method.ReturnType;

            if (returnType == typeof(void))
            {
                reason = "return type is void";
                return false;
            }

            if (method.IsAbstract)
            {
                reason = "method is abstract";
                return false;
            }

            if (method.ContainsGenericParameters || returnType.ContainsGenericParameters)
            {
                reason = "open generic methods/types are not supported";
                return false;
            }

            if (returnType.IsByRef || returnType.IsPointer)
            {
                reason = "by-ref and pointer returns are not supported";
                return false;
            }

            if (!CanGenerateScalarPatch(returnType))
            {
                reason = "live forcing is limited to bool, strings, chars, enums, decimal, and primitive numeric return types";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool TryParseOverride(Type type, out object value, out string error)
        {
            string raw = (_overrideText ?? string.Empty).Trim();

            if (type == typeof(bool))
            {
                bool parsedBool;
                if (bool.TryParse(raw, out parsedBool))
                {
                    value = parsedBool;
                    error = string.Empty;
                    return true;
                }

                value = null;
                error = "Enter true or false.";
                return false;
            }

            if (type == typeof(string))
            {
                value = _overrideText ?? string.Empty;
                error = string.Empty;
                return true;
            }

            if (type == typeof(char))
            {
                if ((_overrideText ?? string.Empty).Length == 1)
                {
                    value = _overrideText[0];
                    error = string.Empty;
                    return true;
                }

                value = null;
                error = "Enter exactly one character.";
                return false;
            }

            if (type.IsEnum)
            {
                try
                {
                    value = Enum.Parse(type, raw, true);
                    error = string.Empty;
                    return true;
                }
                catch
                {
                    value = null;
                    error = "Enter an enum name such as: " + string.Join(", ", Enum.GetNames(type));
                    return false;
                }
            }

            try
            {
                if (type == typeof(decimal))
                    value = decimal.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                else
                    value = Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);

                error = string.Empty;
                return true;
            }
            catch
            {
                value = null;
                error = "Could not parse '" + raw + "' as " + FriendlyType(type) + ".";
                return false;
            }
        }

        private static bool CanGenerateScalarPatch(Type type)
        {
            return type != null &&
                   (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal));
        }

        private static string DefaultOverrideText(Type type)
        {
            if (type == typeof(bool))
                return "true";
            if (type == typeof(string))
                return string.Empty;
            if (type == typeof(char))
                return "A";
            if (type != null && type.IsEnum)
            {
                string[] names = Enum.GetNames(type);
                return names.Length == 0 ? "0" : names[0];
            }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                return "1";
            if (type != null && type.IsPrimitive)
                return "1";
            return string.Empty;
        }

        private static string BuildGeneratedMod(MethodInfo method, object value, bool hardOverride)
        {
            Type resultType = method.ReturnType;
            string resultTypeName = SourceTypeName(resultType);
            string literal = SourceLiteral(resultType, value);
            string typeName = method.DeclaringType == null ? string.Empty : method.DeclaringType.FullName;
            string methodName = method.Name;
            string safeName = SanitizeIdentifier((method.DeclaringType == null ? "Target" : method.DeclaringType.Name) + "_" + method.Name);
            string guid = "claire.valheim.generated." + safeName.ToLowerInvariant();

            ParameterInfo[] parameters = method.GetParameters();
            string[] parameterNames = parameters
                .Select(parameter => ReflectionTypeIdentity(parameter.ParameterType))
                .ToArray();

            StringBuilder source = new StringBuilder();
            source.AppendLine("using BepInEx;");
            source.AppendLine("using HarmonyLib;");
            source.AppendLine("using System;");
            source.AppendLine("using System.Linq;");
            source.AppendLine("using System.Reflection;");
            source.AppendLine();
            source.AppendLine("[BepInPlugin(PluginGuid, PluginName, \"1.0.0\")]");
            source.AppendLine("public sealed class " + safeName + " : BaseUnityPlugin");
            source.AppendLine("{");
            source.AppendLine("    public const string PluginGuid = \"" + EscapeCSharp(guid) + "\";");
            source.AppendLine("    public const string PluginName = \"" + EscapeCSharp(safeName) + "\";");
            source.AppendLine();
            source.AppendLine("    private void Awake()");
            source.AppendLine("    {");
            source.AppendLine("        var harmony = new Harmony(PluginGuid);");

            if (hardOverride)
                source.AppendLine("        harmony.Patch(TargetMethod(), prefix: new HarmonyMethod(typeof(" + safeName + "), nameof(Prefix)));");
            else
                source.AppendLine("        harmony.Patch(TargetMethod(), postfix: new HarmonyMethod(typeof(" + safeName + "), nameof(Postfix)));");

            source.AppendLine("    }");
            source.AppendLine();

            if (hardOverride)
            {
                source.AppendLine("    private static bool Prefix(ref " + resultTypeName + " __result)");
                source.AppendLine("    {");
                source.AppendLine("        __result = " + literal + ";");
                source.AppendLine("        return false; // Skip the original method entirely.");
                source.AppendLine("    }");
            }
            else
            {
                source.AppendLine("    private static void Postfix(ref " + resultTypeName + " __result)");
                source.AppendLine("    {");
                source.AppendLine("        __result = " + literal + "; // Original method already ran.");
                source.AppendLine("    }");
            }

            source.AppendLine();
            source.AppendLine("    private static MethodBase TargetMethod()");
            source.AppendLine("    {");
            source.AppendLine("        var type = FindType(\"" + EscapeCSharp(typeName) + "\");");
            source.AppendLine("        var wanted = new[]");
            source.AppendLine("        {");
            foreach (string parameterName in parameterNames)
                source.AppendLine("            \"" + EscapeCSharp(parameterName) + "\",");
            source.AppendLine("        };");
            source.AppendLine();
            source.AppendLine("        var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |");
            source.AppendLine("            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)");
            source.AppendLine("            .FirstOrDefault(candidate =>");
            source.AppendLine("            {");
            source.AppendLine("                if (candidate.Name != \"" + EscapeCSharp(methodName) + "\") return false;");
            source.AppendLine("                if (candidate.GetGenericArguments().Length != " + method.GetGenericArguments().Length + ") return false;");
            source.AppendLine("                var p = candidate.GetParameters();");
            source.AppendLine("                if (p.Length != wanted.Length) return false;");
            source.AppendLine("                for (var i = 0; i < p.Length; i++)");
            source.AppendLine("                    if (TypeIdentity(p[i].ParameterType) != wanted[i]) return false;");
            source.AppendLine("                return true;");
            source.AppendLine("            });");
            source.AppendLine();
            source.AppendLine("        if (method == null) throw new MissingMethodException(type.FullName, \"" + EscapeCSharp(methodName) + "\");");
            source.AppendLine("        return method;");
            source.AppendLine("    }");
            source.AppendLine();
            source.AppendLine("    private static Type FindType(string fullName)");
            source.AppendLine("    {");
            source.AppendLine("        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())");
            source.AppendLine("        {");
            source.AppendLine("            var type = assembly.GetType(fullName, false, false);");
            source.AppendLine("            if (type != null) return type;");
            source.AppendLine("        }");
            source.AppendLine("        throw new TypeLoadException(fullName);");
            source.AppendLine("    }");
            source.AppendLine();
            source.AppendLine("    private static string TypeIdentity(Type type)");
            source.AppendLine("    {");
            source.AppendLine("        if (type.IsByRef) return TypeIdentity(type.GetElementType()) + \"&\";");
            source.AppendLine("        if (type.IsPointer) return TypeIdentity(type.GetElementType()) + \"*\";");
            source.AppendLine("        if (type.IsArray) return TypeIdentity(type.GetElementType()) + \"[]\";");
            source.AppendLine("        return type.FullName ?? type.Name;");
            source.AppendLine("    }");
            source.AppendLine("}");

            return source.ToString();
        }

        private static string BuildReflectionTargetSnippet(MethodInfo method)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("// " + FormatMethodSignature(method));
            text.AppendLine("Type type = AppDomain.CurrentDomain.GetAssemblies()");
            text.AppendLine("    .Select(a => a.GetType(\"" + EscapeCSharp(method.DeclaringType.FullName) + "\", false, false))");
            text.AppendLine("    .First(t => t != null);");
            text.AppendLine();
            text.AppendLine("// Exact overload identities:");

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
                text.AppendLine("// (no parameters)");
            else
                foreach (ParameterInfo parameter in parameters)
                    text.AppendLine("// " + parameter.Name + ": " + ReflectionTypeIdentity(parameter.ParameterType));

            return text.ToString().TrimEnd();
        }

        private static string ReflectionTypeIdentity(Type type)
        {
            if (type.IsByRef)
                return ReflectionTypeIdentity(type.GetElementType()) + "&";
            if (type.IsPointer)
                return ReflectionTypeIdentity(type.GetElementType()) + "*";
            if (type.IsArray)
                return ReflectionTypeIdentity(type.GetElementType()) + "[]";
            return type.FullName ?? type.Name;
        }

        private static string SourceTypeName(Type type)
        {
            if (type == typeof(bool)) return "bool";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(sbyte)) return "sbyte";
            if (type == typeof(short)) return "short";
            if (type == typeof(ushort)) return "ushort";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(long)) return "long";
            if (type == typeof(ulong)) return "ulong";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(char)) return "char";
            if (type == typeof(string)) return "string";

            if (type.IsEnum)
                return (type.FullName ?? type.Name).Replace('+', '.');

            return FriendlyType(type);
        }

        private static string SourceLiteral(Type type, object value)
        {
            if (type == typeof(bool))
                return (bool)value ? "true" : "false";
            if (type == typeof(string))
                return "\"" + EscapeCSharp(Convert.ToString(value) ?? string.Empty) + "\"";
            if (type == typeof(char))
                return "'" + EscapeChar((char)value) + "'";
            if (type == typeof(float))
                return ((float)value).ToString("R", CultureInfo.InvariantCulture) + "f";
            if (type == typeof(double))
                return ((double)value).ToString("R", CultureInfo.InvariantCulture) + "d";
            if (type == typeof(decimal))
                return ((decimal)value).ToString(CultureInfo.InvariantCulture) + "m";
            if (type == typeof(long))
                return ((long)value).ToString(CultureInfo.InvariantCulture) + "L";
            if (type == typeof(ulong))
                return ((ulong)value).ToString(CultureInfo.InvariantCulture) + "UL";
            if (type == typeof(uint))
                return ((uint)value).ToString(CultureInfo.InvariantCulture) + "U";
            if (type.IsEnum)
                return SourceTypeName(type) + "." + Enum.GetName(type, value);

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string FormatMethodShort(MethodInfo method)
        {
            string parameters = string.Join(", ", method.GetParameters().Select(parameter => FriendlyType(parameter.ParameterType)).ToArray());
            return method.Name + "(" + parameters + ") -> " + FriendlyType(method.ReturnType);
        }

        private static string FormatMethodSignature(MethodInfo method)
        {
            StringBuilder text = new StringBuilder();
            text.Append(Visibility(method));
            text.Append(' ');
            if (method.IsStatic) text.Append("static ");
            text.Append(FriendlyType(method.ReturnType));
            text.Append(' ');
            text.Append(method.DeclaringType == null ? "<unknown>" : FriendlyType(method.DeclaringType));
            text.Append('.');
            text.Append(method.Name);

            if (method.IsGenericMethod)
            {
                text.Append('<');
                text.Append(string.Join(", ", method.GetGenericArguments().Select(arg => arg.Name).ToArray()));
                text.Append('>');
            }

            text.Append('(');
            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i != 0) text.Append(", ");
                Type parameterType = parameters[i].ParameterType;
                if (parameterType.IsByRef) text.Append("ref ");
                text.Append(FriendlyType(parameterType));
                text.Append(' ');
                text.Append(parameters[i].Name);
            }
            text.Append(')');

            return text.ToString();
        }

        private static string FriendlyType(Type type)
        {
            if (type == null) return "-";
            if (type.IsByRef) return FriendlyType(type.GetElementType()) + "&";
            if (type.IsPointer) return FriendlyType(type.GetElementType()) + "*";
            if (type.IsArray) return FriendlyType(type.GetElementType()) + "[]";

            if (type == typeof(void)) return "void";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(sbyte)) return "sbyte";
            if (type == typeof(short)) return "short";
            if (type == typeof(ushort)) return "ushort";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(long)) return "long";
            if (type == typeof(ulong)) return "ulong";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(char)) return "char";
            if (type == typeof(string)) return "string";
            if (type == typeof(object)) return "object";

            if (type.IsGenericType)
            {
                string name = type.Name;
                int tick = name.IndexOf('`');
                if (tick >= 0) name = name.Substring(0, tick);
                return name + "<" + string.Join(", ", type.GetGenericArguments().Select(FriendlyType).ToArray()) + ">";
            }

            return type.Name;
        }

        private static string DisplayTypeName(Type type)
        {
            if (type == null)
                return "-";
            return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
        }

        private static string Visibility(MethodBase method)
        {
            if (method.IsPublic) return "public";
            if (method.IsFamily) return "protected";
            if (method.IsFamilyOrAssembly) return "protected internal";
            if (method.IsAssembly) return "internal";
            return "private";
        }

        private static string Visibility(FieldInfo field)
        {
            if (field.IsPublic) return "public";
            if (field.IsFamily) return "protected";
            if (field.IsFamilyOrAssembly) return "protected internal";
            if (field.IsAssembly) return "internal";
            return "private";
        }

        private static string FormatValue(object value)
        {
            if (value == null)
                return "null";
            if (value is string)
                return "\"" + value + "\"";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string SafeMetadataToken(MemberInfo member)
        {
            try
            {
                return "0x" + member.MetadataToken.ToString("X8");
            }
            catch
            {
                return "<dynamic>";
            }
        }

        private static string EscapeCSharp(string text)
        {
            if (text == null) return string.Empty;
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string EscapeChar(char value)
        {
            switch (value)
            {
                case '\\': return "\\\\";
                case '\'': return "\\'";
                case '\n': return "\\n";
                case '\r': return "\\r";
                case '\t': return "\\t";
                default: return value.ToString();
            }
        }

        private static string SanitizeIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "GeneratedOverride";

            StringBuilder result = new StringBuilder();
            foreach (char c in text)
                result.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

            if (result.Length == 0 || !char.IsLetter(result[0]) && result[0] != '_')
                result.Insert(0, '_');

            return result.ToString();
        }
    }
}
