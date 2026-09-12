using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace WardHitSoundToggleMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class WardHitSoundToggle : BaseUnityPlugin
    {
        public const string PluginGuid = "claire.valheim.wardhitsoundtoggle";
        public const string PluginName = "Ward Hit Sound Toggle";
        public const string PluginVersion = "1.0.0";

        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly Dictionary<object, bool> _originalMuteStates = new Dictionary<object, bool>(ReferenceComparer.Instance);
        private readonly Stopwatch _scanTimer = Stopwatch.StartNew();

        private ConfigEntry<bool> _wardHitSound;
        private ConfigEntry<string> _toggleKey;

        private Type _privateAreaType;
        private Type _zNetSceneType;
        private Type _audioSourceType;
        private Type _inputType;
        private Type _keyCodeType;

        private FieldInfo _allAreasField;
        private FieldInfo _flashEffectField;
        private PropertyInfo _audioMuteProperty;
        private MethodInfo _getKeyDownMethod;

        private bool _lastAppliedSoundState;
        private bool _hasAppliedState;

        private void Awake()
        {
            _wardHitSound = Config.Bind(
                "General",
                "WardHitSound",
                true,
                "If true, the vanilla ward hit/violation sound plays. If false, only that sound is muted; the ward flash and protection still work.");

            _toggleKey = Config.Bind(
                "General",
                "ToggleKey",
                "F8",
                "Unity KeyCode used to toggle the ward hit sound in game. Examples: F8, F9, Home, End.");

            _wardHitSound.SettingChanged += OnSoundSettingChanged;

            ResolveTypes();
            ApplySoundState(force: true);

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Ward hit sound is {(_wardHitSound.Value ? "ON" : "OFF")}; toggle key: {_toggleKey.Value}.");
        }

        private void OnDestroy()
        {
            RestoreOriginalMuteStates();
        }

        private void Update()
        {
            if (TryGetKeyDown(_toggleKey.Value))
            {
                _wardHitSound.Value = !_wardHitSound.Value;
                Logger.LogInfo($"Ward hit sound: {(_wardHitSound.Value ? "ON" : "OFF")}");
            }

            if (_scanTimer.ElapsedMilliseconds >= 750)
            {
                _scanTimer.Restart();
                ApplySoundState(force: false);
            }
        }

        private void OnSoundSettingChanged(object sender, EventArgs e)
        {
            ApplySoundState(force: true);
        }

        private void ApplySoundState(bool force)
        {
            ResolveTypes();

            if (_privateAreaType == null || _audioSourceType == null || _audioMuteProperty == null)
                return;

            bool enabled = _wardHitSound != null && _wardHitSound.Value;
            if (!force && _hasAppliedState && enabled == _lastAppliedSoundState)
            {
                // Still rescan loaded wards so newly streamed/placed wards inherit the selected state.
            }

            TryApplyGuardStonePrefab(enabled);
            TryApplyLoadedPrivateAreas(enabled);

            _lastAppliedSoundState = enabled;
            _hasAppliedState = true;
        }

        private void ResolveTypes()
        {
            if (_privateAreaType == null)
                _privateAreaType = FindLoadedType("PrivateArea");

            if (_zNetSceneType == null)
                _zNetSceneType = FindLoadedType("ZNetScene");

            if (_audioSourceType == null)
                _audioSourceType = FindLoadedType("UnityEngine.AudioSource");

            if (_inputType == null)
                _inputType = FindLoadedType("UnityEngine.Input");

            if (_keyCodeType == null)
                _keyCodeType = FindLoadedType("UnityEngine.KeyCode");

            if (_privateAreaType != null)
            {
                if (_allAreasField == null)
                    _allAreasField = _privateAreaType.GetField("m_allAreas", AnyStatic);

                if (_flashEffectField == null)
                    _flashEffectField = _privateAreaType.GetField("m_flashEffect", AnyInstance);
            }

            if (_audioSourceType != null && _audioMuteProperty == null)
                _audioMuteProperty = _audioSourceType.GetProperty("mute", AnyInstance);

            if (_inputType != null && _keyCodeType != null && _getKeyDownMethod == null)
            {
                _getKeyDownMethod = _inputType.GetMethod(
                    "GetKeyDown",
                    AnyStatic,
                    binder: null,
                    types: new[] { _keyCodeType },
                    modifiers: null);
            }
        }

        private void TryApplyGuardStonePrefab(bool soundEnabled)
        {
            if (_zNetSceneType == null || _privateAreaType == null)
                return;

            try
            {
                object scene = null;

                FieldInfo instanceField = _zNetSceneType.GetField("instance", AnyStatic);
                if (instanceField != null)
                    scene = instanceField.GetValue(null);

                if (scene == null)
                {
                    PropertyInfo instanceProperty = _zNetSceneType.GetProperty("instance", AnyStatic);
                    if (instanceProperty != null)
                        scene = instanceProperty.GetValue(null, null);
                }

                if (scene == null)
                    return;

                MethodInfo getPrefab = _zNetSceneType.GetMethod(
                    "GetPrefab",
                    AnyInstance,
                    binder: null,
                    types: new[] { typeof(string) },
                    modifiers: null);

                if (getPrefab == null)
                    return;

                object guardStone = getPrefab.Invoke(scene, new object[] { "guard_stone" });
                if (guardStone == null)
                    return;

                MethodInfo getComponent = guardStone.GetType().GetMethod(
                    "GetComponent",
                    AnyInstance,
                    binder: null,
                    types: new[] { typeof(Type) },
                    modifiers: null);

                object privateArea = getComponent?.Invoke(guardStone, new object[] { _privateAreaType });
                if (privateArea != null)
                    ApplyToPrivateArea(privateArea, soundEnabled);
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Could not update guard_stone prefab yet: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void TryApplyLoadedPrivateAreas(bool soundEnabled)
        {
            if (_allAreasField == null)
                return;

            try
            {
                object allAreas = _allAreasField.GetValue(null);
                if (!(allAreas is IEnumerable enumerable))
                    return;

                foreach (object area in enumerable)
                {
                    if (area != null)
                        ApplyToPrivateArea(area, soundEnabled);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Could not scan loaded wards: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void ApplyToPrivateArea(object privateArea, bool soundEnabled)
        {
            if (_flashEffectField == null || privateArea == null)
                return;

            try
            {
                object flashEffect = _flashEffectField.GetValue(privateArea);
                if (flashEffect == null)
                    return;

                FieldInfo effectPrefabsField = flashEffect.GetType().GetField("m_effectPrefabs", AnyInstance);
                if (effectPrefabsField == null)
                    return;

                if (!(effectPrefabsField.GetValue(flashEffect) is Array effectPrefabs))
                    return;

                foreach (object effectData in effectPrefabs)
                {
                    if (effectData == null)
                        continue;

                    FieldInfo prefabField = effectData.GetType().GetField("m_prefab", AnyInstance);
                    object prefab = prefabField?.GetValue(effectData);
                    if (prefab != null)
                        ApplyMuteToPrefab(prefab, !soundEnabled);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Could not update a ward flash effect: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void ApplyMuteToPrefab(object prefab, bool mute)
        {
            if (prefab == null || _audioSourceType == null || _audioMuteProperty == null)
                return;

            try
            {
                MethodInfo getComponentsInChildren = prefab.GetType().GetMethod(
                    "GetComponentsInChildren",
                    AnyInstance,
                    binder: null,
                    types: new[] { typeof(Type), typeof(bool) },
                    modifiers: null);

                if (getComponentsInChildren == null)
                    return;

                object result = getComponentsInChildren.Invoke(prefab, new object[] { _audioSourceType, true });
                if (!(result is IEnumerable audioSources))
                    return;

                foreach (object audioSource in audioSources)
                {
                    if (audioSource == null)
                        continue;

                    if (!_originalMuteStates.ContainsKey(audioSource))
                    {
                        bool original = false;
                        object value = _audioMuteProperty.GetValue(audioSource, null);
                        if (value is bool boolValue)
                            original = boolValue;
                        _originalMuteStates[audioSource] = original;
                    }

                    bool target = mute ? true : _originalMuteStates[audioSource];
                    _audioMuteProperty.SetValue(audioSource, target, null);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"Could not mute ward flash AudioSource: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void RestoreOriginalMuteStates()
        {
            if (_audioMuteProperty == null)
                return;

            foreach (KeyValuePair<object, bool> pair in _originalMuteStates)
            {
                try
                {
                    if (pair.Key != null)
                        _audioMuteProperty.SetValue(pair.Key, pair.Value, null);
                }
                catch
                {
                    // Unity objects may already have been destroyed during shutdown.
                }
            }

            _originalMuteStates.Clear();
        }

        private bool TryGetKeyDown(string keyName)
        {
            ResolveTypes();

            if (_keyCodeType == null || _getKeyDownMethod == null || string.IsNullOrWhiteSpace(keyName))
                return false;

            try
            {
                object keyCode = Enum.Parse(_keyCodeType, keyName.Trim(), ignoreCase: true);
                object result = _getKeyDownMethod.Invoke(null, new[] { keyCode });
                return result is bool pressed && pressed;
            }
            catch
            {
                return false;
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Ignore dynamic/partially loaded assemblies and keep searching.
                }
            }

            return null;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
