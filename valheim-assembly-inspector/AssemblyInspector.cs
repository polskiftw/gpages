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
        public const string PluginVersion = "1.5.0";
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
        private static readonly Dictionary<MethodBase, Dictionary<int, object>> RuntimeArgumentOverrides =
            new Dictionary<MethodBase, Dictionary<int, object>>();
        private static readonly Dictionary<MethodBase, List<FieldMutationPatch>> RuntimeFieldMutations =
            new Dictionary<MethodBase, List<FieldMutationPatch>>();

        private readonly HashSet<MethodInfo> _livePatchedMethods =
            new HashSet<MethodInfo>();

        private ConfigEntry<KeyCode> _toggleKey;
        private ConfigEntry<string> _assemblyName;
        private ConfigEntry<int> _fontSize;

        private Harmony _harmony;
        private Harmony _uiHarmony;
        private Assembly _targetAssembly;
        private List<Type> _types = new List<Type>();
        private readonly List<GlobalMemberEntry> _globalMembers = new List<GlobalMemberEntry>();
        private readonly List<GlobalMemberEntry> _globalSearchResults = new List<GlobalMemberEntry>();
        private const int MaxGlobalSearchResults = 200;
        private const float GlobalSearchDebounceSeconds = 0.12f;
        private string _globalSearchAppliedQuery = string.Empty;
        private int _globalSearchMatchCount;
        private bool _globalSearchDirty;
        private float _globalSearchChangedAt;

        private Type _selectedType;
        private MethodInfo _selectedMethod;
        private PropertyInfo _selectedProperty;
        private FieldInfo _selectedField;

        private string _typeSearch = string.Empty;
        private string _memberSearch = string.Empty;
        private string _returnSearch = string.Empty;
        private string _globalSearch = string.Empty;
        private string _overrideText = string.Empty;
        private readonly Dictionary<string, string> _argumentOverrideTexts =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private string _fieldMutationTargetText = string.Empty;
        private string _fieldMutationValueText = string.Empty;
        private string _fieldMutationLastFieldKey = string.Empty;
        private bool _fieldMutationPostfix;

        private string _fieldEditorHookSearch = string.Empty;
        private MethodInfo _fieldEditorHookMethod;
        private string _fieldEditorValueText = string.Empty;
        private bool _fieldEditorPostfix;
        private Vector2 _fieldEditorHookScroll;

        private string _status = "Open in game after Valheim has loaded its managed assemblies.";

        private bool _visible;
        private bool _includeInherited;
        private bool _showSpecialMethods;
        private MemberTab _tab = MemberTab.Methods;

        private Rect _windowRect = new Rect(24f, 24f, 1650f, 960f);
        private Vector2 _typeScroll;
        private Vector2 _memberScroll;
        private Vector2 _globalSearchScroll;
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

        private enum FieldMutationSourceKind
        {
            Instance,
            Argument,
            Static
        }

        private sealed class ArgumentOverrideSnapshot
        {
            public MethodInfo Method;
            public int ArgumentIndex;
            public object Value;
        }

        private sealed class FieldMutationPatch
        {
            public MethodInfo Method;
            public FieldMutationSourceKind SourceKind;
            public int ArgumentIndex;
            public FieldInfo Field;
            public object Value;
            public bool Postfix;
            public string TargetPath;
        }

        private sealed class GlobalMemberEntry
        {
            public Type DeclaringType;
            public MethodInfo Method;
            public PropertyInfo Property;
            public FieldInfo Field;
            public string Kind;
            public string Label;
            public string ClassText;
            public string NameText;
            public string TypeText;
            public string ParameterText;
            public string SearchText;
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

            int liveOverrideCount = GetLiveOverrideCount();
            if (GUILayout.Button("Copy live bundle (" + liveOverrideCount + ")", GUILayout.Width(190f)))
            {
                if (liveOverrideCount == 0)
                {
                    _status = "No live patches are active yet.";
                }
                else
                {
                    GUIUtility.systemCopyBuffer = BuildGeneratedLiveBundle();
                    _status = "Copied one standalone mod containing " + liveOverrideCount + " live patch" +
                        (liveOverrideCount == 1 ? "." : "es.");
                }
            }

            if (GUILayout.Button("Clear all live patches", GUILayout.Width(165f)))
                ClearAllOverrides();

            GUILayout.FlexibleSpace();
            GUILayout.Label(_status ?? string.Empty);
            GUILayout.Space(8f);
            if (GUILayout.Button("Close", GUILayout.Width(68f)))
                SetVisible(false);
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Global search:", GUILayout.Width(92f));
            string nextGlobalSearch = GUILayout.TextField(_globalSearch ?? string.Empty);
            if (!string.Equals(nextGlobalSearch, _globalSearch, StringComparison.Ordinal))
            {
                _globalSearch = nextGlobalSearch;
                MarkGlobalSearchDirty();
            }

            if (!string.IsNullOrWhiteSpace(_globalSearch) && GUILayout.Button("Clear", GUILayout.Width(64f)))
            {
                _globalSearch = string.Empty;
                ClearGlobalSearchCache();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Search all classes at once. Terms match class/member/signature/type/parameters. Filters: method:, field:, property:, class:, name:, type:, param:");
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

            if (!string.IsNullOrWhiteSpace(_globalSearch))
            {
                GUILayout.Label("Global search is active.");
                GUILayout.Label("The 1,000+ class button list is paused while searching so IMGUI does not rebuild it every frame.");
                if (_selectedType != null)
                    GUILayout.Label("Selected: " + DisplayTypeName(_selectedType));
                GUILayout.FlexibleSpace();
                GUILayout.Label(_types.Count + " classes indexed.");
                GUILayout.EndVertical();
                return;
            }

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

            string globalQuery = (_globalSearch ?? string.Empty).Trim();
            if (globalQuery.Length != 0)
            {
                DrawGlobalSearchResults(globalQuery);
                GUILayout.EndVertical();
                return;
            }

            if (_selectedType == null)
            {
                GUILayout.Label("Select a class, or use Global search above.");
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

        private void DrawGlobalSearchResults(string query)
        {
            GUILayout.Label("Global members");
            GUILayout.Label("Indexed " + _globalMembers.Count + " declared methods/properties/fields.");

            if (_globalSearchDirty ||
                !string.Equals(_globalSearchAppliedQuery, query, StringComparison.Ordinal))
            {
                float elapsed = Time.realtimeSinceStartup - _globalSearchChangedAt;
                if (elapsed < GlobalSearchDebounceSeconds)
                {
                    GUILayout.Label("Waiting for typing to pause before searching...");
                    GUILayout.Label("Search is debounced so a partial query does not rescan the assembly every frame.");
                    return;
                }

                RebuildGlobalSearchResults(query);
            }

            _globalSearchScroll = GUILayout.BeginScrollView(_globalSearchScroll);
            foreach (GlobalMemberEntry entry in _globalSearchResults)
            {
                string label = IsGlobalEntrySelected(entry) ? "> " + entry.Label : entry.Label;
                if (GUILayout.Button(label, GUILayout.Height(42f)))
                    SelectGlobalMember(entry);
            }
            GUILayout.EndScrollView();

            if (_globalSearchMatchCount > MaxGlobalSearchResults)
            {
                GUILayout.Label("Showing first " + MaxGlobalSearchResults + " / " +
                    _globalSearchMatchCount + " matches. Add another term or filter to narrow it.");
            }
            else
            {
                GUILayout.Label("Showing " + _globalSearchMatchCount + " match" +
                    (_globalSearchMatchCount == 1 ? "." : "es."));
            }
        }

        private void MarkGlobalSearchDirty()
        {
            _globalSearchDirty = true;
            _globalSearchChangedAt = Time.realtimeSinceStartup;
            _globalSearchAppliedQuery = string.Empty;
            _globalSearchMatchCount = 0;
            _globalSearchResults.Clear();
            _globalSearchScroll = Vector2.zero;
        }

        private void ClearGlobalSearchCache()
        {
            _globalSearchDirty = false;
            _globalSearchAppliedQuery = string.Empty;
            _globalSearchMatchCount = 0;
            _globalSearchResults.Clear();
            _globalSearchScroll = Vector2.zero;
        }

        private void RebuildGlobalSearchResults(string query)
        {
            string[] tokens = (query ?? string.Empty)
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            _globalSearchResults.Clear();
            int matches = 0;

            foreach (GlobalMemberEntry entry in _globalMembers)
            {
                if (!MatchesGlobalSearch(entry, tokens))
                    continue;

                matches++;
                if (_globalSearchResults.Count < MaxGlobalSearchResults)
                    _globalSearchResults.Add(entry);
            }

            _globalSearchMatchCount = matches;
            _globalSearchAppliedQuery = query;
            _globalSearchDirty = false;
        }

        private bool IsGlobalEntrySelected(GlobalMemberEntry entry)
        {
            if (entry.Method != null)
                return ReferenceEquals(entry.Method, _selectedMethod) && _selectedProperty == null;
            if (entry.Property != null)
                return ReferenceEquals(entry.Property, _selectedProperty);
            return entry.Field != null && ReferenceEquals(entry.Field, _selectedField);
        }

        private void SelectGlobalMember(GlobalMemberEntry entry)
        {
            SelectType(entry.DeclaringType);

            if (entry.Method != null)
            {
                _tab = MemberTab.Methods;
                SelectMethod(entry.Method);
            }
            else if (entry.Property != null)
            {
                _tab = MemberTab.Properties;
                SelectProperty(entry.Property);
            }
            else if (entry.Field != null)
            {
                _tab = MemberTab.Fields;
                SelectField(entry.Field);
            }
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
            }
            else
            {
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
                GUILayout.Label("Generated return-override source");
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

            DrawArgumentOverrideEditor(method);
            DrawFieldMutationEditor(method);
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


        private void DrawArgumentOverrideEditor(MethodInfo method)
        {
            GUILayout.Space(14f);
            GUILayout.Label("Argument overrides");

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                GUILayout.Label("This method has no arguments to override.");
                return;
            }

            bool showedAny = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                string reason;
                if (!CanOverrideArgument(method, i, out reason))
                    continue;

                showedAny = true;
                ParameterInfo parameter = parameters[i];
                Type valueType = ParameterValueType(parameter);
                string key = MethodIdentity(method) + "|arg:" + i;

                string textValue;
                if (!_argumentOverrideTexts.TryGetValue(key, out textValue))
                    textValue = DefaultOverrideText(valueType);

                bool active = IsArgumentOverrideActive(method, i);
                GUILayout.Space(5f);
                GUILayout.Label("[" + i + "] " + FriendlyType(parameter.ParameterType) + " " + parameter.Name +
                    (active ? "   LIVE: " + FormatValue(GetCurrentArgumentOverride(method, i)) : string.Empty));

                GUILayout.BeginHorizontal();
                textValue = GUILayout.TextField(textValue ?? string.Empty);
                _argumentOverrideTexts[key] = textValue;

                if (GUILayout.Button("Apply live", GUILayout.Width(92f)))
                {
                    object value;
                    string error;
                    if (TryParseScalarValue(valueType, textValue, out value, out error))
                        ApplyLiveArgumentOverride(method, i, value);
                    else
                        _status = error;
                }

                if (GUILayout.Button("Copy argument mod", GUILayout.Width(142f)))
                {
                    object value;
                    string error;
                    if (TryParseScalarValue(valueType, textValue, out value, out error))
                    {
                        GUIUtility.systemCopyBuffer = BuildGeneratedArgumentMod(method, i, value);
                        _status = "Argument override mod copied.";
                    }
                    else
                    {
                        _status = error;
                    }
                }

                if (active && GUILayout.Button("Remove live", GUILayout.Width(100f)))
                    RemoveLiveArgumentOverride(method, i);

                GUILayout.EndHorizontal();
            }

            if (!showedAny)
                GUILayout.Label("No scalar arguments on this method can be safely overridden automatically.");
            else
                GUILayout.Label("Argument overrides use a Harmony prefix and replace the selected argument before Valheim receives it.");
        }

        private void DrawFieldMutationEditor(MethodInfo method)
        {
            GUILayout.Space(14f);
            GUILayout.Label("Field mutation patch");
            GUILayout.Label("Target one field on this method's instance or one object argument, for example: this.m_cheated or item.m_cheated");

            if (string.IsNullOrEmpty(_fieldMutationTargetText))
                _fieldMutationTargetText = DefaultFieldMutationTarget(method);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Target:", GUILayout.Width(58f));
            _fieldMutationTargetText = GUILayout.TextField(_fieldMutationTargetText ?? string.Empty);
            GUILayout.EndHorizontal();

            FieldMutationPatch target;
            string error;
            if (!TryResolveFieldMutationTarget(method, _fieldMutationTargetText, out target, out error))
            {
                GUILayout.Label("Not ready: " + error);
                return;
            }

            string fieldKey = MethodIdentity(method) + "|" + FieldMutationIdentity(target);
            if (!string.Equals(_fieldMutationLastFieldKey, fieldKey, StringComparison.Ordinal))
            {
                _fieldMutationLastFieldKey = fieldKey;
                _fieldMutationValueText = DefaultFieldMutationText(target.Field.FieldType);
            }

            DetailLine("Resolved field", (target.Field.DeclaringType == null ? string.Empty : target.Field.DeclaringType.FullName) +
                "." + target.Field.Name);
            DetailLine("Field type", FriendlyType(target.Field.FieldType));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Value:", GUILayout.Width(58f));
            _fieldMutationValueText = GUILayout.TextField(_fieldMutationValueText ?? string.Empty);
            GUILayout.EndHorizontal();

            _fieldMutationPostfix = GUILayout.Toggle(
                _fieldMutationPostfix,
                "Run after the original method (postfix). Off = mutate before it runs (prefix).");
            target.Postfix = _fieldMutationPostfix;

            FieldMutationPatch active = GetCurrentFieldMutation(method, target);
            if (active != null)
                GUILayout.Label("LIVE FIELD MUTATION ACTIVE: " + FormatValue(active.Value));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply live"))
            {
                object value;
                if (TryParseScalarValue(target.Field.FieldType, _fieldMutationValueText, out value, out error))
                {
                    target.Value = value;
                    ApplyLiveFieldMutation(target);
                }
                else
                {
                    _status = error;
                }
            }

            if (GUILayout.Button("Copy field-mutation mod"))
            {
                object value;
                if (TryParseScalarValue(target.Field.FieldType, _fieldMutationValueText, out value, out error))
                {
                    GUIUtility.systemCopyBuffer = BuildGeneratedFieldMutationMod(
                        method,
                        _fieldMutationTargetText,
                        value,
                        _fieldMutationPostfix);
                    _status = "Field mutation mod copied.";
                }
                else
                {
                    _status = error;
                }
            }

            if (active != null && GUILayout.Button("Remove live"))
                RemoveLiveFieldMutation(method, target);

            GUILayout.EndHorizontal();

            GUILayout.Label(_fieldMutationPostfix
                ? "Postfix field mutation runs after Valheim finishes the method; useful for clearing a field that the method just loaded or created."
                : "Prefix field mutation runs before Valheim enters the method; useful for sanitizing an object before it is saved or consumed.");
        }

        private static string DefaultFieldMutationTarget(MethodInfo method)
        {
            if (method == null)
                return string.Empty;

            if (!method.IsStatic)
                return "this.";

            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = ParameterValueType(parameters[i]);
                if (type != null && !type.IsValueType && !type.IsPointer && type != typeof(string))
                    return (parameters[i].Name ?? ("arg" + i)) + ".";
            }

            return string.Empty;
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

            GUILayout.Space(14f);
            GUILayout.Label("Create mutation patch");

            string reason;
            if (!CanMutateField(field, out reason))
            {
                GUILayout.Label("Automatic mutation unavailable: " + reason);
                return;
            }

            if (field.IsStatic)
            {
                GUILayout.Label("This is a static field. Pick any patchable method on " +
                    FriendlyType(field.DeclaringType) + " to decide when the field is forced.");
            }
            else
            {
                GUILayout.Label("This is an instance field. Pick an instance method on " +
                    FriendlyType(field.DeclaringType) +
                    "; whenever that method runs, the field on that same object can be forced.");
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Value:", GUILayout.Width(58f));
            _fieldEditorValueText = GUILayout.TextField(_fieldEditorValueText ?? string.Empty);
            GUILayout.EndHorizontal();

            _fieldEditorPostfix = GUILayout.Toggle(
                _fieldEditorPostfix,
                "Run after the hook method (postfix). Off = mutate before it runs (prefix).");

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Hook:", GUILayout.Width(58f));
            _fieldEditorHookSearch = GUILayout.TextField(_fieldEditorHookSearch ?? string.Empty);
            GUILayout.EndHorizontal();

            MethodInfo[] hookMethods = GetFieldHookMethods(field);
            string hookSearch = (_fieldEditorHookSearch ?? string.Empty).Trim();
            int matchingHooks = 0;
            int shownHooks = 0;

            _fieldEditorHookScroll = GUILayout.BeginScrollView(
                _fieldEditorHookScroll,
                GUILayout.Height(190f));

            foreach (MethodInfo method in hookMethods)
            {
                string methodText = FormatMethodShort(method);
                if (hookSearch.Length != 0 &&
                    !ContainsIgnoreCase(method.Name, hookSearch) &&
                    !ContainsIgnoreCase(methodText, hookSearch))
                {
                    continue;
                }

                matchingHooks++;
                if (shownHooks >= 60)
                    continue;

                string label = ReferenceEquals(method, _fieldEditorHookMethod)
                    ? "> " + methodText
                    : methodText;

                if (GUILayout.Button(label, GUILayout.Height(34f)))
                {
                    _fieldEditorHookMethod = method;
                    _status = "Field hook selected: " + FormatMethodShort(method) + ".";
                }

                shownHooks++;
            }

            GUILayout.EndScrollView();

            if (matchingHooks > 60)
                GUILayout.Label("Showing first 60 / " + matchingHooks + " hook methods. Type above to narrow it.");
            else
                GUILayout.Label("Showing " + matchingHooks + " hook method" + (matchingHooks == 1 ? "." : "s."));

            if (_fieldEditorHookMethod == null)
            {
                GUILayout.Label("Select a hook method above, then Apply live or copy the standalone patch.");
                return;
            }

            DetailLine("Selected hook", FormatMethodShort(_fieldEditorHookMethod));
            string targetPath = field.IsStatic ? "static." + field.Name : "this." + field.Name;
            DetailLine("Mutation target", targetPath);

            FieldMutationPatch patch;
            if (!TryCreateFieldMutationPatch(
                    field,
                    _fieldEditorHookMethod,
                    _fieldEditorPostfix,
                    out patch,
                    out reason))
            {
                GUILayout.Label("Cannot use this hook: " + reason);
                return;
            }

            FieldMutationPatch active = GetCurrentFieldMutation(_fieldEditorHookMethod, patch);
            if (active != null)
                GUILayout.Label("LIVE FIELD MUTATION ACTIVE: " + FormatValue(active.Value));

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Apply live"))
            {
                object value;
                string error;
                if (TryParseScalarValue(field.FieldType, _fieldEditorValueText, out value, out error))
                {
                    patch.Value = value;
                    ApplyLiveFieldMutation(patch);
                }
                else
                {
                    _status = error;
                }
            }

            if (GUILayout.Button("Copy field-mutation mod"))
            {
                object value;
                string error;
                if (TryParseScalarValue(field.FieldType, _fieldEditorValueText, out value, out error))
                {
                    patch.Value = value;
                    GUIUtility.systemCopyBuffer = BuildGeneratedFieldMutationMod(patch);
                    _status = "Field mutation mod copied.";
                }
                else
                {
                    _status = error;
                }
            }

            if (GUILayout.Button("Open hook method"))
            {
                MethodInfo hook = _fieldEditorHookMethod;
                string valueText = _fieldEditorValueText;
                bool postfix = _fieldEditorPostfix;
                string mutationTarget = targetPath;

                SelectType(hook.DeclaringType);
                _tab = MemberTab.Methods;
                SelectMethod(hook);
                _fieldMutationTargetText = mutationTarget;
                _fieldMutationValueText = valueText;
                _fieldMutationPostfix = postfix;
                _status = "Opened hook method with " + mutationTarget + " prefilled.";
            }

            if (active != null && GUILayout.Button("Remove live"))
                RemoveLiveFieldMutation(_fieldEditorHookMethod, patch);

            GUILayout.EndHorizontal();
        }

        private static bool CanMutateField(FieldInfo field, out string reason)
        {
            if (field == null)
            {
                reason = "no field selected";
                return false;
            }

            if (field.IsLiteral || field.IsInitOnly)
            {
                reason = "constant/readonly fields are not writable";
                return false;
            }

            if (!CanGenerateScalarPatch(field.FieldType))
            {
                reason = "automatic field mutation is limited to scalar field types";
                return false;
            }

            if (field.DeclaringType == null)
            {
                reason = "field has no declaring type";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static MethodInfo[] GetFieldHookMethods(FieldInfo field)
        {
            if (field == null || field.DeclaringType == null)
                return new MethodInfo[0];

            return field.DeclaringType
                .GetMethods(DeclaredMembers)
                .Where(method =>
                    !method.IsAbstract &&
                    !method.ContainsGenericParameters &&
                    !method.IsSpecialName &&
                    (field.IsStatic || !method.IsStatic))
                .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(method => method.GetParameters().Length)
                .ToArray();
        }

        private static bool TryCreateFieldMutationPatch(
            FieldInfo field,
            MethodInfo method,
            bool postfix,
            out FieldMutationPatch patch,
            out string error)
        {
            patch = null;

            string reason;
            if (!CanMutateField(field, out reason))
            {
                error = reason;
                return false;
            }

            if (method == null)
            {
                error = "select a hook method";
                return false;
            }

            if (method.IsAbstract || method.ContainsGenericParameters)
            {
                error = "abstract/open-generic methods cannot be patched";
                return false;
            }

            if (!field.IsStatic && method.IsStatic)
            {
                error = "an instance field needs an instance hook method";
                return false;
            }

            if (method.DeclaringType == null ||
                field.DeclaringType == null ||
                !field.DeclaringType.IsAssignableFrom(method.DeclaringType) &&
                !method.DeclaringType.IsAssignableFrom(field.DeclaringType))
            {
                error = "hook method does not belong to the field's object type";
                return false;
            }

            patch = new FieldMutationPatch
            {
                Method = method,
                SourceKind = field.IsStatic
                    ? FieldMutationSourceKind.Static
                    : FieldMutationSourceKind.Instance,
                ArgumentIndex = -1,
                Field = field,
                Value = null,
                Postfix = postfix,
                TargetPath = field.IsStatic
                    ? "static." + field.Name
                    : "this." + field.Name
            };

            error = string.Empty;
            return true;
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
            _globalMembers.Clear();
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

            BuildGlobalMemberIndex();
            if (!string.IsNullOrWhiteSpace(_globalSearch))
            {
                _globalSearchDirty = true;
                _globalSearchChangedAt = 0f;
                _globalSearchAppliedQuery = string.Empty;
                _globalSearchResults.Clear();
                _globalSearchMatchCount = 0;
            }

            _status = "Loaded " + _types.Count + " classes and " + _globalMembers.Count +
                " searchable members from " + _targetAssembly.GetName().Name + ".";
        }

        private void BuildGlobalMemberIndex()
        {
            _globalMembers.Clear();

            foreach (Type type in _types)
            {
                string classText = DisplayTypeName(type) + " " + (type.FullName ?? string.Empty);

                try
                {
                    foreach (MethodInfo method in type.GetMethods(DeclaredMembers))
                    {
                        // Properties are indexed separately, so omit accessor methods from the
                        // global method index to keep common searches from returning duplicates.
                        if (method.IsSpecialName)
                            continue;

                        string parameterText = string.Join(" ", method.GetParameters()
                            .Select(parameter =>
                                (parameter.Name ?? string.Empty) + " " +
                                FriendlyType(parameter.ParameterType) + " " +
                                (parameter.ParameterType.FullName ?? string.Empty))
                            .ToArray());
                        string typeText = FriendlyType(method.ReturnType) + " " +
                            (method.ReturnType.FullName ?? string.Empty);

                        _globalMembers.Add(new GlobalMemberEntry
                        {
                            DeclaringType = type,
                            Method = method,
                            Kind = "method",
                            Label = "[METHOD] " + DisplayTypeName(type) + "." + FormatMethodShort(method),
                            ClassText = classText,
                            NameText = method.Name,
                            TypeText = typeText,
                            ParameterText = parameterText,
                            SearchText = "method " + classText + " " + method.Name + " " +
                                FormatMethodSignature(method) + " " + typeText + " " + parameterText
                        });
                    }

                    foreach (PropertyInfo property in type.GetProperties(DeclaredMembers))
                    {
                        string parameterText = string.Join(" ", property.GetIndexParameters()
                            .Select(parameter =>
                                (parameter.Name ?? string.Empty) + " " +
                                FriendlyType(parameter.ParameterType) + " " +
                                (parameter.ParameterType.FullName ?? string.Empty))
                            .ToArray());
                        string typeText = FriendlyType(property.PropertyType) + " " +
                            (property.PropertyType.FullName ?? string.Empty);

                        _globalMembers.Add(new GlobalMemberEntry
                        {
                            DeclaringType = type,
                            Property = property,
                            Kind = "property",
                            Label = "[PROPERTY] " + DisplayTypeName(type) + "." + property.Name +
                                " -> " + FriendlyType(property.PropertyType),
                            ClassText = classText,
                            NameText = property.Name,
                            TypeText = typeText,
                            ParameterText = parameterText,
                            SearchText = "property " + classText + " " + property.Name + " " +
                                typeText + " " + parameterText
                        });
                    }

                    foreach (FieldInfo field in type.GetFields(DeclaredMembers))
                    {
                        string typeText = FriendlyType(field.FieldType) + " " +
                            (field.FieldType.FullName ?? string.Empty);

                        _globalMembers.Add(new GlobalMemberEntry
                        {
                            DeclaringType = type,
                            Field = field,
                            Kind = "field",
                            Label = "[FIELD] " + DisplayTypeName(type) + "." + field.Name +
                                " : " + FriendlyType(field.FieldType),
                            ClassText = classText,
                            NameText = field.Name,
                            TypeText = typeText,
                            ParameterText = string.Empty,
                            SearchText = "field " + classText + " " + field.Name + " " + typeText
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDebug("Could not index all members of " + type.FullName + ": " + ex.Message);
                }
            }

            _globalMembers.Sort((left, right) =>
            {
                int byName = StringComparer.OrdinalIgnoreCase.Compare(left.NameText, right.NameText);
                if (byName != 0)
                    return byName;

                int byClass = StringComparer.OrdinalIgnoreCase.Compare(left.ClassText, right.ClassText);
                if (byClass != 0)
                    return byClass;

                return StringComparer.OrdinalIgnoreCase.Compare(left.Kind, right.Kind);
            });
        }

        private static bool MatchesGlobalSearch(GlobalMemberEntry entry, string[] tokens)
        {
            foreach (string rawToken in tokens)
            {
                int colon = rawToken.IndexOf(':');
                if (colon > 0)
                {
                    string prefix = rawToken.Substring(0, colon).ToLowerInvariant();
                    string value = rawToken.Substring(colon + 1);

                    switch (prefix)
                    {
                        case "method":
                            if (entry.Method == null || value.Length != 0 && !ContainsIgnoreCase(entry.SearchText, value))
                                return false;
                            continue;
                        case "property":
                            if (entry.Property == null || value.Length != 0 && !ContainsIgnoreCase(entry.SearchText, value))
                                return false;
                            continue;
                        case "field":
                            if (entry.Field == null || value.Length != 0 && !ContainsIgnoreCase(entry.SearchText, value))
                                return false;
                            continue;
                        case "class":
                            if (!ContainsIgnoreCase(entry.ClassText, value))
                                return false;
                            continue;
                        case "name":
                            if (!ContainsIgnoreCase(entry.NameText, value))
                                return false;
                            continue;
                        case "type":
                        case "return":
                            if (!ContainsIgnoreCase(entry.TypeText, value))
                                return false;
                            continue;
                        case "param":
                        case "parameter":
                            if (!ContainsIgnoreCase(entry.ParameterText, value))
                                return false;
                            continue;
                        case "kind":
                            if (!ContainsIgnoreCase(entry.Kind, value))
                                return false;
                            continue;
                    }
                }

                if (!ContainsIgnoreCase(entry.SearchText, rawToken))
                    return false;
            }

            return true;
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
            _fieldMutationTargetText = string.Empty;
            _fieldMutationValueText = string.Empty;
            _fieldMutationLastFieldKey = string.Empty;
            _fieldMutationPostfix = false;
            ResetFieldEditorState();
        }

        private void ResetFieldEditorState()
        {
            _fieldEditorHookSearch = string.Empty;
            _fieldEditorHookMethod = null;
            _fieldEditorValueText = string.Empty;
            _fieldEditorPostfix = false;
            _fieldEditorHookScroll = Vector2.zero;
        }

        private void SelectMethod(MethodInfo method)
        {
            _selectedMethod = method;
            _selectedProperty = null;
            _selectedField = null;
            _overrideText = DefaultOverrideText(method.ReturnType);
            _fieldMutationTargetText = DefaultFieldMutationTarget(method);
            _fieldMutationValueText = string.Empty;
            _fieldMutationLastFieldKey = string.Empty;
            _fieldMutationPostfix = false;
            ResetFieldEditorState();
            _detailScroll = Vector2.zero;
        }

        private void SelectProperty(PropertyInfo property)
        {
            _selectedProperty = property;
            _selectedField = null;
            _selectedMethod = property.GetGetMethod(true);
            _overrideText = _selectedMethod == null ? string.Empty : DefaultOverrideText(_selectedMethod.ReturnType);
            _fieldMutationTargetText = DefaultFieldMutationTarget(_selectedMethod);
            _fieldMutationValueText = string.Empty;
            _fieldMutationLastFieldKey = string.Empty;
            _fieldMutationPostfix = false;
            ResetFieldEditorState();
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
            _fieldMutationTargetText = string.Empty;
            _fieldMutationValueText = string.Empty;
            _fieldMutationLastFieldKey = string.Empty;
            _fieldMutationPostfix = false;
            ResetFieldEditorState();
            _fieldEditorValueText = DefaultFieldMutationText(field == null ? null : field.FieldType);
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

            lock (OverrideLock)
                RuntimeOverrides[method] = value;

            try
            {
                RefreshLivePatches(method);
                _status = FormatMethodShort(method) + " now returns " + FormatValue(value) + " to callers.";
            }
            catch (Exception ex)
            {
                lock (OverrideLock)
                    RuntimeOverrides.Remove(method);
                try { RefreshLivePatches(method); } catch { }
                _status = "Patch failed: " + ex.GetType().Name + ": " + ex.Message;
                Logger.LogWarning(_status);
            }
        }

        private void RemoveLiveOverride(MethodInfo method)
        {
            try
            {
                lock (OverrideLock)
                    RuntimeOverrides.Remove(method);
                RefreshLivePatches(method);
                _status = "Removed live return override from " + FormatMethodShort(method) + ".";
            }
            catch (Exception ex)
            {
                _status = "Unpatch failed: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private void ApplyLiveArgumentOverride(MethodInfo method, int argumentIndex, object value)
        {
            string reason;
            if (!CanOverrideArgument(method, argumentIndex, out reason))
            {
                _status = "Cannot override argument: " + reason;
                return;
            }

            lock (OverrideLock)
            {
                Dictionary<int, object> entries;
                if (!RuntimeArgumentOverrides.TryGetValue(method, out entries))
                {
                    entries = new Dictionary<int, object>();
                    RuntimeArgumentOverrides[method] = entries;
                }
                entries[argumentIndex] = value;
            }

            try
            {
                RefreshLivePatches(method);
                ParameterInfo parameter = method.GetParameters()[argumentIndex];
                _status = FormatMethodShort(method) + " argument '" + parameter.Name + "' now receives " + FormatValue(value) + ".";
            }
            catch (Exception ex)
            {
                lock (OverrideLock)
                {
                    Dictionary<int, object> entries;
                    if (RuntimeArgumentOverrides.TryGetValue(method, out entries))
                    {
                        entries.Remove(argumentIndex);
                        if (entries.Count == 0)
                            RuntimeArgumentOverrides.Remove(method);
                    }
                }
                try { RefreshLivePatches(method); } catch { }
                _status = "Argument patch failed: " + ex.GetType().Name + ": " + ex.Message;
                Logger.LogWarning(_status);
            }
        }

        private void RemoveLiveArgumentOverride(MethodInfo method, int argumentIndex)
        {
            lock (OverrideLock)
            {
                Dictionary<int, object> entries;
                if (RuntimeArgumentOverrides.TryGetValue(method, out entries))
                {
                    entries.Remove(argumentIndex);
                    if (entries.Count == 0)
                        RuntimeArgumentOverrides.Remove(method);
                }
            }

            try
            {
                RefreshLivePatches(method);
                _status = "Removed live argument override from " + FormatMethodShort(method) + ".";
            }
            catch (Exception ex)
            {
                _status = "Unpatch failed: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private void ApplyLiveFieldMutation(FieldMutationPatch patch)
        {
            if (patch == null || patch.Method == null || patch.Field == null)
            {
                _status = "Cannot apply field mutation: target is incomplete.";
                return;
            }

            FieldMutationPatch stored = new FieldMutationPatch
            {
                Method = patch.Method,
                SourceKind = patch.SourceKind,
                ArgumentIndex = patch.ArgumentIndex,
                Field = patch.Field,
                Value = patch.Value,
                Postfix = patch.Postfix,
                TargetPath = patch.TargetPath
            };

            lock (OverrideLock)
            {
                List<FieldMutationPatch> entries;
                if (!RuntimeFieldMutations.TryGetValue(patch.Method, out entries))
                {
                    entries = new List<FieldMutationPatch>();
                    RuntimeFieldMutations[patch.Method] = entries;
                }

                entries.RemoveAll(existing => FieldMutationMatches(existing, stored));
                entries.Add(stored);
            }

            try
            {
                RefreshLivePatches(patch.Method);
                _status = (patch.Postfix ? "Postfix" : "Prefix") + " field mutation active: " +
                    patch.TargetPath + " = " + FormatValue(patch.Value) + ".";
            }
            catch (Exception ex)
            {
                lock (OverrideLock)
                {
                    List<FieldMutationPatch> entries;
                    if (RuntimeFieldMutations.TryGetValue(patch.Method, out entries))
                    {
                        entries.RemoveAll(existing => FieldMutationMatches(existing, stored));
                        if (entries.Count == 0)
                            RuntimeFieldMutations.Remove(patch.Method);
                    }
                }
                try { RefreshLivePatches(patch.Method); } catch { }
                _status = "Field mutation patch failed: " + ex.GetType().Name + ": " + ex.Message;
                Logger.LogWarning(_status);
            }
        }

        private void RemoveLiveFieldMutation(MethodInfo method, FieldMutationPatch patch)
        {
            lock (OverrideLock)
            {
                List<FieldMutationPatch> entries;
                if (RuntimeFieldMutations.TryGetValue(method, out entries))
                {
                    entries.RemoveAll(existing => FieldMutationMatches(existing, patch));
                    if (entries.Count == 0)
                        RuntimeFieldMutations.Remove(method);
                }
            }

            try
            {
                RefreshLivePatches(method);
                _status = "Removed live field mutation from " + FormatMethodShort(method) + ".";
            }
            catch (Exception ex)
            {
                _status = "Unpatch failed: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private void RefreshLivePatches(MethodInfo method)
        {
            _harmony.Unpatch(method, HarmonyPatchType.Prefix, HarmonyId);
            _harmony.Unpatch(method, HarmonyPatchType.Postfix, HarmonyId);

            bool patched = false;

            if (IsOverrideActive(method))
            {
                MethodInfo factory = typeof(AssemblyInspector).GetMethod(
                    nameof(LivePostfixFactory),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (factory == null)
                    throw new MissingMethodException(typeof(AssemblyInspector).FullName, nameof(LivePostfixFactory));

                _harmony.Patch(method, postfix: new HarmonyMethod(factory));
                patched = true;
            }

            if (HasArgumentOverride(method) || HasFieldMutation(method, false, false))
            {
                MethodInfo prefix = typeof(AssemblyInspector).GetMethod(
                    nameof(LiveArgumentAndFieldPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                patched = true;
            }

            if (HasFieldMutation(method, true, false))
            {
                MethodInfo postfix = typeof(AssemblyInspector).GetMethod(
                    nameof(LiveArgumentAndFieldPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                patched = true;
            }

            if (!method.IsStatic && HasFieldMutation(method, false, true))
            {
                MethodInfo prefix = typeof(AssemblyInspector).GetMethod(
                    nameof(LiveInstanceFieldPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                patched = true;
            }

            if (!method.IsStatic && HasFieldMutation(method, true, true))
            {
                MethodInfo postfix = typeof(AssemblyInspector).GetMethod(
                    nameof(LiveInstanceFieldPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                patched = true;
            }

            if (patched)
                _livePatchedMethods.Add(method);
            else
                _livePatchedMethods.Remove(method);
        }

        private void ClearAllOverrides()
        {
            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not unpatch all Assembly Inspector live patches: " + ex.Message);
            }

            _livePatchedMethods.Clear();
            lock (OverrideLock)
            {
                RuntimeOverrides.Clear();
                RuntimeArgumentOverrides.Clear();
                RuntimeFieldMutations.Clear();
            }

            _status = "Cleared all live patches.";
        }

        private static void LiveArgumentAndFieldPrefix(MethodBase __originalMethod, object[] __args)
        {
            Dictionary<int, object> argumentOverrides = null;
            List<FieldMutationPatch> fieldMutations = null;

            lock (OverrideLock)
            {
                Dictionary<int, object> storedArguments;
                if (RuntimeArgumentOverrides.TryGetValue(__originalMethod, out storedArguments))
                    argumentOverrides = new Dictionary<int, object>(storedArguments);

                List<FieldMutationPatch> storedFields;
                if (RuntimeFieldMutations.TryGetValue(__originalMethod, out storedFields))
                    fieldMutations = storedFields
                        .Where(patch => !patch.Postfix && patch.SourceKind != FieldMutationSourceKind.Instance)
                        .ToList();
            }

            if (argumentOverrides != null && __args != null)
            {
                foreach (KeyValuePair<int, object> entry in argumentOverrides)
                {
                    if (entry.Key >= 0 && entry.Key < __args.Length)
                        __args[entry.Key] = entry.Value;
                }
            }

            ApplyNonInstanceFieldMutations(fieldMutations, __args);
        }

        private static void LiveArgumentAndFieldPostfix(MethodBase __originalMethod, object[] __args)
        {
            List<FieldMutationPatch> fieldMutations = null;
            lock (OverrideLock)
            {
                List<FieldMutationPatch> storedFields;
                if (RuntimeFieldMutations.TryGetValue(__originalMethod, out storedFields))
                    fieldMutations = storedFields
                        .Where(patch => patch.Postfix && patch.SourceKind != FieldMutationSourceKind.Instance)
                        .ToList();
            }

            ApplyNonInstanceFieldMutations(fieldMutations, __args);
        }

        private static void LiveInstanceFieldPrefix(MethodBase __originalMethod, object __instance)
        {
            ApplyInstanceFieldMutations(__originalMethod, __instance, false);
        }

        private static void LiveInstanceFieldPostfix(MethodBase __originalMethod, object __instance)
        {
            ApplyInstanceFieldMutations(__originalMethod, __instance, true);
        }

        private static void ApplyInstanceFieldMutations(MethodBase method, object instance, bool postfix)
        {
            List<FieldMutationPatch> mutations = null;
            lock (OverrideLock)
            {
                List<FieldMutationPatch> stored;
                if (RuntimeFieldMutations.TryGetValue(method, out stored))
                    mutations = stored
                        .Where(patch => patch.Postfix == postfix && patch.SourceKind == FieldMutationSourceKind.Instance)
                        .ToList();
            }

            if (mutations == null)
                return;

            foreach (FieldMutationPatch patch in mutations)
                TrySetRuntimeField(patch, instance);
        }

        private static void ApplyNonInstanceFieldMutations(List<FieldMutationPatch> mutations, object[] args)
        {
            if (mutations == null)
                return;

            foreach (FieldMutationPatch patch in mutations)
            {
                object target = null;
                if (patch.SourceKind == FieldMutationSourceKind.Argument &&
                    args != null &&
                    patch.ArgumentIndex >= 0 &&
                    patch.ArgumentIndex < args.Length)
                {
                    target = args[patch.ArgumentIndex];
                }

                TrySetRuntimeField(patch, target);
            }
        }

        private static void TrySetRuntimeField(FieldMutationPatch patch, object target)
        {
            try
            {
                if (patch.Field.IsStatic)
                    patch.Field.SetValue(null, patch.Value);
                else if (target != null)
                    patch.Field.SetValue(target, patch.Value);
            }
            catch (Exception ex)
            {
                if (_instance != null)
                    _instance.Logger.LogWarning("Live field mutation failed for " + patch.TargetPath + ": " + ex.Message);
            }
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

        private static bool IsArgumentOverrideActive(MethodInfo method, int argumentIndex)
        {
            lock (OverrideLock)
            {
                Dictionary<int, object> entries;
                return RuntimeArgumentOverrides.TryGetValue(method, out entries) &&
                       entries.ContainsKey(argumentIndex);
            }
        }

        private static object GetCurrentArgumentOverride(MethodInfo method, int argumentIndex)
        {
            lock (OverrideLock)
            {
                Dictionary<int, object> entries;
                object value;
                return RuntimeArgumentOverrides.TryGetValue(method, out entries) &&
                       entries.TryGetValue(argumentIndex, out value)
                    ? value
                    : null;
            }
        }

        private static FieldMutationPatch GetCurrentFieldMutation(MethodInfo method, FieldMutationPatch target)
        {
            lock (OverrideLock)
            {
                List<FieldMutationPatch> entries;
                if (!RuntimeFieldMutations.TryGetValue(method, out entries))
                    return null;

                return entries.FirstOrDefault(existing => FieldMutationMatches(existing, target));
            }
        }

        private static bool HasArgumentOverride(MethodInfo method)
        {
            lock (OverrideLock)
            {
                Dictionary<int, object> entries;
                return RuntimeArgumentOverrides.TryGetValue(method, out entries) && entries.Count != 0;
            }
        }

        private static bool HasFieldMutation(MethodInfo method, bool postfix, bool instanceSource)
        {
            lock (OverrideLock)
            {
                List<FieldMutationPatch> entries;
                if (!RuntimeFieldMutations.TryGetValue(method, out entries))
                    return false;

                return entries.Any(patch =>
                    patch.Postfix == postfix &&
                    (instanceSource
                        ? patch.SourceKind == FieldMutationSourceKind.Instance
                        : patch.SourceKind != FieldMutationSourceKind.Instance));
            }
        }

        private static bool FieldMutationMatches(FieldMutationPatch left, FieldMutationPatch right)
        {
            return left != null &&
                   right != null &&
                   left.Postfix == right.Postfix &&
                   left.SourceKind == right.SourceKind &&
                   left.ArgumentIndex == right.ArgumentIndex &&
                   Equals(left.Field, right.Field);
        }

        private static int GetLiveOverrideCount()
        {
            lock (OverrideLock)
            {
                int count = RuntimeOverrides.Count;
                count += RuntimeArgumentOverrides.Values.Sum(entries => entries.Count);
                count += RuntimeFieldMutations.Values.Sum(entries => entries.Count);
                return count;
            }
        }

        private static List<KeyValuePair<MethodInfo, object>> GetLiveOverrideSnapshot()
        {
            lock (OverrideLock)
            {
                return RuntimeOverrides
                    .Where(pair => pair.Key is MethodInfo)
                    .Select(pair => new KeyValuePair<MethodInfo, object>((MethodInfo)pair.Key, pair.Value))
                    .OrderBy(pair => MethodIdentity(pair.Key), StringComparer.Ordinal)
                    .ToList();
            }
        }

        private static List<ArgumentOverrideSnapshot> GetLiveArgumentOverrideSnapshot()
        {
            lock (OverrideLock)
            {
                List<ArgumentOverrideSnapshot> result = new List<ArgumentOverrideSnapshot>();
                foreach (KeyValuePair<MethodBase, Dictionary<int, object>> methodEntry in RuntimeArgumentOverrides)
                {
                    MethodInfo method = methodEntry.Key as MethodInfo;
                    if (method == null)
                        continue;

                    foreach (KeyValuePair<int, object> entry in methodEntry.Value)
                    {
                        result.Add(new ArgumentOverrideSnapshot
                        {
                            Method = method,
                            ArgumentIndex = entry.Key,
                            Value = entry.Value
                        });
                    }
                }

                return result
                    .OrderBy(entry => MethodIdentity(entry.Method), StringComparer.Ordinal)
                    .ThenBy(entry => entry.ArgumentIndex)
                    .ToList();
            }
        }

        private static List<FieldMutationPatch> GetLiveFieldMutationSnapshot()
        {
            lock (OverrideLock)
            {
                return RuntimeFieldMutations.Values
                    .SelectMany(entries => entries)
                    .OrderBy(entry => MethodIdentity(entry.Method), StringComparer.Ordinal)
                    .ThenBy(entry => entry.Postfix)
                    .ThenBy(entry => entry.TargetPath, StringComparer.Ordinal)
                    .ToList();
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

        private static bool CanOverrideArgument(MethodInfo method, int argumentIndex, out string reason)
        {
            if (method == null)
            {
                reason = "no method selected";
                return false;
            }

            if (method.IsAbstract)
            {
                reason = "method is abstract";
                return false;
            }

            if (method.ContainsGenericParameters)
            {
                reason = "open generic methods are not supported";
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (argumentIndex < 0 || argumentIndex >= parameters.Length)
            {
                reason = "argument index is out of range";
                return false;
            }

            Type type = ParameterValueType(parameters[argumentIndex]);
            if (type == null || type.IsPointer || type.ContainsGenericParameters)
            {
                reason = "pointer/open-generic arguments are not supported";
                return false;
            }

            if (!CanGenerateScalarPatch(type))
            {
                reason = "automatic argument overrides are limited to scalar values";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static Type ParameterValueType(ParameterInfo parameter)
        {
            if (parameter == null)
                return null;

            Type type = parameter.ParameterType;
            return type.IsByRef ? type.GetElementType() : type;
        }

        private static bool TryResolveFieldMutationTarget(
            MethodInfo method,
            string targetPath,
            out FieldMutationPatch patch,
            out string error)
        {
            patch = null;
            string raw = (targetPath ?? string.Empty).Trim();

            if (method == null)
            {
                error = "no method selected";
                return false;
            }

            if (method.IsAbstract || method.ContainsGenericParameters)
            {
                error = "abstract/open-generic methods cannot be patched";
                return false;
            }

            int dot = raw.IndexOf('.');
            if (dot <= 0 || dot == raw.Length - 1 || raw.IndexOf('.', dot + 1) >= 0)
            {
                error = "enter one-level path such as this.m_cheated, item.m_cheated, or static.someFlag";
                return false;
            }

            string owner = raw.Substring(0, dot).Trim();
            string fieldName = raw.Substring(dot + 1).Trim();
            Type sourceType;
            FieldMutationSourceKind sourceKind;
            int argumentIndex = -1;
            bool explicitStatic = false;

            if (string.Equals(owner, "this", StringComparison.Ordinal))
            {
                if (method.IsStatic)
                {
                    error = "this is unavailable on a static method";
                    return false;
                }

                sourceType = method.DeclaringType;
                sourceKind = FieldMutationSourceKind.Instance;
            }
            else if (string.Equals(owner, "static", StringComparison.OrdinalIgnoreCase))
            {
                sourceType = method.DeclaringType;
                sourceKind = FieldMutationSourceKind.Static;
                explicitStatic = true;
            }
            else
            {
                ParameterInfo[] parameters = method.GetParameters();
                argumentIndex = -1;

                for (int i = 0; i < parameters.Length; i++)
                {
                    if (string.Equals(parameters[i].Name, owner, StringComparison.Ordinal))
                    {
                        argumentIndex = i;
                        break;
                    }
                }

                if (argumentIndex < 0 && owner.StartsWith("arg", StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (int.TryParse(owner.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) &&
                        parsed >= 0 &&
                        parsed < parameters.Length)
                    {
                        argumentIndex = parsed;
                    }
                }

                if (argumentIndex < 0)
                {
                    error = "left side must be this, static, a parameter name, or argN";
                    return false;
                }

                sourceType = ParameterValueType(parameters[argumentIndex]);
                sourceKind = FieldMutationSourceKind.Argument;
            }

            if (sourceType == null || sourceType.IsPointer)
            {
                error = "field source type is unsupported";
                return false;
            }

            FieldInfo field = FindFieldInHierarchy(sourceType, fieldName);
            if (field == null)
            {
                error = "field '" + fieldName + "' was not found on " + FriendlyType(sourceType);
                return false;
            }

            if (explicitStatic && !field.IsStatic)
            {
                error = "static." + fieldName + " resolved to an instance field";
                return false;
            }

            if (field.IsLiteral || field.IsInitOnly)
            {
                error = "constant/readonly fields are not writable";
                return false;
            }

            if (!CanGenerateScalarPatch(field.FieldType))
            {
                error = "automatic field mutation is limited to scalar field types";
                return false;
            }

            if (!field.IsStatic && sourceType.IsValueType)
            {
                error = "instance fields on value-type arguments are not supported";
                return false;
            }

            if (field.IsStatic)
            {
                sourceKind = FieldMutationSourceKind.Static;
                argumentIndex = -1;
            }

            patch = new FieldMutationPatch
            {
                Method = method,
                SourceKind = sourceKind,
                ArgumentIndex = argumentIndex,
                Field = field,
                Value = null,
                Postfix = false,
                TargetPath = raw
            };

            error = string.Empty;
            return true;
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static string FieldMutationIdentity(FieldMutationPatch patch)
        {
            string declaringType = patch.Field == null || patch.Field.DeclaringType == null
                ? string.Empty
                : patch.Field.DeclaringType.FullName;

            return (patch.Postfix ? "post" : "pre") + "|" +
                   patch.SourceKind + "|" +
                   patch.ArgumentIndex + "|" +
                   declaringType + "|" +
                   (patch.Field == null ? string.Empty : patch.Field.Name);
        }

        private bool TryParseOverride(Type type, out object value, out string error)
        {
            return TryParseScalarValue(type, _overrideText, out value, out error);
        }

        private static bool TryParseScalarValue(Type type, string input, out object value, out string error)
        {
            string rawInput = input ?? string.Empty;
            string raw = rawInput.Trim();

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
                value = rawInput;
                error = string.Empty;
                return true;
            }

            if (type == typeof(char))
            {
                if (rawInput.Length == 1)
                {
                    value = rawInput[0];
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

        private static string DefaultFieldMutationText(Type type)
        {
            if (type == typeof(bool))
                return "false";
            return DefaultOverrideText(type);
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

        private static string BuildGeneratedLiveBundle()
        {
            List<KeyValuePair<MethodInfo, object>> returnOverrides = GetLiveOverrideSnapshot();
            List<ArgumentOverrideSnapshot> argumentOverrides = GetLiveArgumentOverrideSnapshot();
            List<FieldMutationPatch> fieldMutations = GetLiveFieldMutationSnapshot();

            if (returnOverrides.Count + argumentOverrides.Count + fieldMutations.Count == 0)
                throw new InvalidOperationException("No live patches are active.");

            return BuildGeneratedPatchSource(
                returnOverrides,
                argumentOverrides,
                fieldMutations,
                true);
        }

        private static string BuildGeneratedArgumentMod(MethodInfo method, int argumentIndex, object value)
        {
            string reason;
            if (!CanOverrideArgument(method, argumentIndex, out reason))
                throw new InvalidOperationException(reason);

            return BuildGeneratedPatchSource(
                new List<KeyValuePair<MethodInfo, object>>(),
                new List<ArgumentOverrideSnapshot>
                {
                    new ArgumentOverrideSnapshot
                    {
                        Method = method,
                        ArgumentIndex = argumentIndex,
                        Value = value
                    }
                },
                new List<FieldMutationPatch>(),
                false);
        }

        private static string BuildGeneratedFieldMutationMod(
            MethodInfo method,
            string targetPath,
            object value,
            bool postfix)
        {
            FieldMutationPatch patch;
            string error;
            if (!TryResolveFieldMutationTarget(method, targetPath, out patch, out error))
                throw new InvalidOperationException(error);

            patch.Value = value;
            patch.Postfix = postfix;
            return BuildGeneratedFieldMutationMod(patch);
        }

        private static string BuildGeneratedFieldMutationMod(FieldMutationPatch patch)
        {
            if (patch == null || patch.Method == null || patch.Field == null)
                throw new ArgumentException("Field mutation patch is incomplete.", nameof(patch));

            return BuildGeneratedPatchSource(
                new List<KeyValuePair<MethodInfo, object>>(),
                new List<ArgumentOverrideSnapshot>(),
                new List<FieldMutationPatch> { patch },
                false);
        }

        private static string BuildGeneratedPatchSource(
            List<KeyValuePair<MethodInfo, object>> returnOverrides,
            List<ArgumentOverrideSnapshot> argumentOverrides,
            List<FieldMutationPatch> fieldMutations,
            bool liveBundle)
        {
            returnOverrides = returnOverrides
                .OrderBy(entry => MethodIdentity(entry.Key), StringComparer.Ordinal)
                .ToList();
            argumentOverrides = argumentOverrides
                .OrderBy(entry => MethodIdentity(entry.Method), StringComparer.Ordinal)
                .ThenBy(entry => entry.ArgumentIndex)
                .ToList();
            fieldMutations = fieldMutations
                .OrderBy(entry => MethodIdentity(entry.Method), StringComparer.Ordinal)
                .ThenBy(entry => entry.Postfix)
                .ThenBy(entry => entry.TargetPath, StringComparer.Ordinal)
                .ToList();

            int total = returnOverrides.Count + argumentOverrides.Count + fieldMutations.Count;
            string bundleId = ComputePatchBundleId(returnOverrides, argumentOverrides, fieldMutations);
            string className = liveBundle
                ? "AssemblyInspectorLiveBundle_" + bundleId
                : "AssemblyInspectorGeneratedPatch_" + bundleId;
            string guid = liveBundle
                ? "claire.valheim.generated.livebundle." + bundleId
                : "claire.valheim.generated.patch." + bundleId;
            string pluginName = liveBundle
                ? "Assembly Inspector Bundle " + bundleId
                : "Assembly Inspector Patch " + bundleId;

            StringBuilder source = new StringBuilder();
            source.AppendLine("// Generated by Assembly Inspector " + PluginVersion + ".");
            source.AppendLine("// Contains " + total + " patch" + (total == 1 ? "." : "es."));
            source.AppendLine("// Deterministic content ID: " + bundleId);
            source.AppendLine("using BepInEx;");
            source.AppendLine("using HarmonyLib;");
            source.AppendLine("using System;");
            source.AppendLine("using System.Linq;");
            source.AppendLine("using System.Reflection;");
            source.AppendLine();
            source.AppendLine("[BepInPlugin(PluginGuid, PluginName, \"1.0.0\")]");
            source.AppendLine("public sealed class " + className + " : BaseUnityPlugin");
            source.AppendLine("{");
            source.AppendLine("    public const string PluginGuid = \"" + guid + "\";");
            source.AppendLine("    public const string PluginName = \"" + pluginName + "\";");
            source.AppendLine();
            source.AppendLine("    private void Awake()");
            source.AppendLine("    {");
            source.AppendLine("        var harmony = new Harmony(PluginGuid);");

            for (int i = 0; i < returnOverrides.Count; i++)
            {
                MethodInfo method = returnOverrides[i].Key;
                source.Append("        harmony.Patch(");
                source.Append(GeneratedTargetExpression(method));
                source.Append(", postfix: new HarmonyMethod(typeof(");
                source.Append(className);
                source.Append("), nameof(Postfix_");
                source.Append(i);
                source.AppendLine(")));");
            }

            for (int i = 0; i < argumentOverrides.Count; i++)
            {
                MethodInfo method = argumentOverrides[i].Method;
                source.Append("        harmony.Patch(");
                source.Append(GeneratedTargetExpression(method));
                source.Append(", prefix: new HarmonyMethod(typeof(");
                source.Append(className);
                source.Append("), nameof(PrefixArg_");
                source.Append(i);
                source.AppendLine(")));");
            }

            for (int i = 0; i < fieldMutations.Count; i++)
            {
                FieldMutationPatch patch = fieldMutations[i];
                string patchName = (patch.Postfix ? "PostfixField_" : "PrefixField_") + i;

                source.Append("        harmony.Patch(");
                source.Append(GeneratedTargetExpression(patch.Method));
                source.Append(patch.Postfix ? ", postfix: " : ", prefix: ");
                source.Append("new HarmonyMethod(typeof(");
                source.Append(className);
                source.Append("), nameof(");
                source.Append(patchName);
                source.AppendLine(")));");
            }

            source.AppendLine("    }");
            source.AppendLine();

            for (int i = 0; i < returnOverrides.Count; i++)
            {
                MethodInfo method = returnOverrides[i].Key;
                object value = returnOverrides[i].Value;
                Type resultType = method.ReturnType;

                source.AppendLine("    // Return: " + EscapeCSharp(FormatMethodShort(method)) + " = " + EscapeCSharp(FormatValue(value)));
                source.Append("    private static void Postfix_");
                source.Append(i);
                source.Append("(ref ");
                source.Append(SourceTypeName(resultType));
                source.AppendLine(" __result)");
                source.AppendLine("    {");
                source.Append("        __result = ");
                source.Append(SourceLiteral(resultType, value));
                source.AppendLine(";");
                source.AppendLine("    }");
                source.AppendLine();
            }

            for (int i = 0; i < argumentOverrides.Count; i++)
            {
                ArgumentOverrideSnapshot entry = argumentOverrides[i];
                ParameterInfo parameter = entry.Method.GetParameters()[entry.ArgumentIndex];
                Type valueType = ParameterValueType(parameter);

                source.AppendLine("    // Argument: " + EscapeCSharp(FormatMethodShort(entry.Method)) +
                    " [" + entry.ArgumentIndex + "] " + EscapeCSharp(parameter.Name ?? ("arg" + entry.ArgumentIndex)) +
                    " = " + EscapeCSharp(FormatValue(entry.Value)));
                source.Append("    private static void PrefixArg_");
                source.Append(i);
                source.AppendLine("(object[] __args)");
                source.AppendLine("    {");
                source.Append("        __args[");
                source.Append(entry.ArgumentIndex);
                source.Append("] = ");
                source.Append(SourceObjectExpression(valueType, entry.Value));
                source.AppendLine(";");
                source.AppendLine("    }");
                source.AppendLine();
            }

            for (int i = 0; i < fieldMutations.Count; i++)
            {
                FieldMutationPatch patch = fieldMutations[i];
                string patchName = (patch.Postfix ? "PostfixField_" : "PrefixField_") + i;
                string sourceExpression;

                if (patch.SourceKind == FieldMutationSourceKind.Instance)
                {
                    source.Append("    private static void ");
                    source.Append(patchName);
                    source.AppendLine("(object __instance)");
                    sourceExpression = "__instance";
                }
                else if (patch.SourceKind == FieldMutationSourceKind.Argument)
                {
                    source.Append("    private static void ");
                    source.Append(patchName);
                    source.AppendLine("(object[] __args)");
                    sourceExpression = "__args[" + patch.ArgumentIndex + "]";
                }
                else
                {
                    source.Append("    private static void ");
                    source.Append(patchName);
                    source.AppendLine("()");
                    sourceExpression = "null";
                }

                source.AppendLine("    {");
                source.Append("        SetField(");
                source.Append(sourceExpression);
                source.Append(", \"");
                source.Append(EscapeCSharp(patch.Field.DeclaringType == null ? string.Empty : patch.Field.DeclaringType.FullName));
                source.Append("\", \"");
                source.Append(EscapeCSharp(patch.Field.Name));
                source.Append("\", ");
                source.Append(SourceObjectExpression(patch.Field.FieldType, patch.Value));
                source.AppendLine(");");
                source.AppendLine("    }");
                source.AppendLine();
            }

            source.AppendLine("    private static MethodBase FindMethod(string typeName, string methodName, int genericArity, string[] wanted)");
            source.AppendLine("    {");
            source.AppendLine("        var type = FindType(typeName);");
            source.AppendLine("        var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |");
            source.AppendLine("            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)");
            source.AppendLine("            .FirstOrDefault(candidate =>");
            source.AppendLine("            {");
            source.AppendLine("                if (candidate.Name != methodName) return false;");
            source.AppendLine("                if (candidate.GetGenericArguments().Length != genericArity) return false;");
            source.AppendLine("                var p = candidate.GetParameters();");
            source.AppendLine("                if (p.Length != wanted.Length) return false;");
            source.AppendLine("                for (var i = 0; i < p.Length; i++)");
            source.AppendLine("                    if (TypeIdentity(p[i].ParameterType) != wanted[i]) return false;");
            source.AppendLine("                return true;");
            source.AppendLine("            });");
            source.AppendLine();
            source.AppendLine("        if (method == null) throw new MissingMethodException(type.FullName, methodName);");
            source.AppendLine("        return method;");
            source.AppendLine("    }");
            source.AppendLine();

            if (fieldMutations.Count != 0)
            {
                source.AppendLine("    private static void SetField(object instance, string declaringTypeName, string fieldName, object value)");
                source.AppendLine("    {");
                source.AppendLine("        var type = FindType(declaringTypeName);");
                source.AppendLine("        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Static |");
                source.AppendLine("            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);");
                source.AppendLine("        if (field == null) throw new MissingFieldException(type.FullName, fieldName);");
                source.AppendLine("        if (!field.IsStatic && instance == null) return;");
                source.AppendLine("        field.SetValue(field.IsStatic ? null : instance, value);");
                source.AppendLine("    }");
                source.AppendLine();
            }

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

        private static string GeneratedTargetExpression(MethodInfo method)
        {
            string[] wanted = method.GetParameters()
                .Select(parameter => ReflectionTypeIdentity(parameter.ParameterType))
                .ToArray();

            StringBuilder text = new StringBuilder();
            text.Append("FindMethod(\"");
            text.Append(EscapeCSharp(method.DeclaringType == null ? string.Empty : method.DeclaringType.FullName));
            text.Append("\", \"");
            text.Append(EscapeCSharp(method.Name));
            text.Append("\", ");
            text.Append(method.GetGenericArguments().Length);
            text.Append(", new string[] { ");

            for (int i = 0; i < wanted.Length; i++)
            {
                if (i != 0) text.Append(", ");
                text.Append("\"");
                text.Append(EscapeCSharp(wanted[i]));
                text.Append("\"");
            }

            text.Append(" })");
            return text.ToString();
        }

        private static string ComputePatchBundleId(
            List<KeyValuePair<MethodInfo, object>> returnOverrides,
            List<ArgumentOverrideSnapshot> argumentOverrides,
            List<FieldMutationPatch> fieldMutations)
        {
            List<string> canonical = new List<string>();

            foreach (KeyValuePair<MethodInfo, object> entry in returnOverrides)
            {
                canonical.Add(
                    "return|" + MethodIdentity(entry.Key) + "|" +
                    ReflectionTypeIdentity(entry.Key.ReturnType) + "=" +
                    SourceLiteral(entry.Key.ReturnType, entry.Value));
            }

            foreach (ArgumentOverrideSnapshot entry in argumentOverrides)
            {
                ParameterInfo parameter = entry.Method.GetParameters()[entry.ArgumentIndex];
                Type valueType = ParameterValueType(parameter);
                canonical.Add(
                    "argument|" + MethodIdentity(entry.Method) + "|" +
                    entry.ArgumentIndex + "|" +
                    ReflectionTypeIdentity(valueType) + "=" +
                    SourceObjectExpression(valueType, entry.Value));
            }

            foreach (FieldMutationPatch patch in fieldMutations)
            {
                canonical.Add(
                    "field|" + MethodIdentity(patch.Method) + "|" +
                    (patch.Postfix ? "postfix" : "prefix") + "|" +
                    patch.SourceKind + "|" +
                    patch.ArgumentIndex + "|" +
                    (patch.Field.DeclaringType == null ? string.Empty : patch.Field.DeclaringType.FullName) + "|" +
                    patch.Field.Name + "|" +
                    ReflectionTypeIdentity(patch.Field.FieldType) + "=" +
                    SourceObjectExpression(patch.Field.FieldType, patch.Value));
            }

            canonical.Sort(StringComparer.Ordinal);

            byte[] data = Encoding.UTF8.GetBytes(string.Join("\n", canonical.ToArray()));
            byte[] hash;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                hash = sha.ComputeHash(data);

            StringBuilder suffix = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                suffix.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));

            return suffix.ToString();
        }

        private static string MethodIdentity(MethodInfo method)
        {
            StringBuilder text = new StringBuilder();
            text.Append(method.DeclaringType == null ? string.Empty : method.DeclaringType.FullName);
            text.Append('|');
            text.Append(method.Name);
            text.Append('|');
            text.Append(method.GetGenericArguments().Length);
            text.Append('|');

            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i != 0) text.Append(';');
                text.Append(ReflectionTypeIdentity(parameters[i].ParameterType));
            }

            return text.ToString();
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


        private static string SourceObjectExpression(Type type, object value)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (type.IsEnum)
            {
                string name = Enum.GetName(type, value);
                if (!string.IsNullOrEmpty(name))
                {
                    return "Enum.Parse(FindType(\"" +
                        EscapeCSharp(type.FullName ?? type.Name) +
                        "\"), \"" +
                        EscapeCSharp(name) +
                        "\")";
                }

                Type underlying = Enum.GetUnderlyingType(type);
                object raw = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
                return "Enum.ToObject(FindType(\"" +
                    EscapeCSharp(type.FullName ?? type.Name) +
                    "\"), " +
                    SourceObjectExpression(underlying, raw) +
                    ")";
            }

            if (type == typeof(byte) || type == typeof(sbyte) ||
                type == typeof(short) || type == typeof(ushort))
            {
                return "(" + SourceTypeName(type) + ")" +
                    Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return SourceLiteral(type, value);
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
