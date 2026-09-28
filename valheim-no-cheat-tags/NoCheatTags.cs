using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoCheatTagsMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class NoCheatTags : BaseUnityPlugin
    {
        public const string PluginGuid = "claire.valheim.nocheattags";
        public const string PluginName = "No Cheat Tags";
        public const string PluginVersion = "1.0.0";

        private const BindingFlags Declared =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        private const BindingFlags AnyMember =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        private const int QueuedCheatSlots = 8;

        private Harmony _harmony;
        private Assembly _gameAssembly;

        private Type _itemDataType;
        private Type _inventoryType;
        private Type _itemDropType;
        private Type _characterDropType;
        private Type _zdoType;
        private Type _zdoVarsType;
        private Type _playerProfileType;

        private FieldInfo _itemCheatedField;
        private FieldInfo _itemDropItemDataField;
        private FieldInfo _characterDropCheatedField;
        private MethodInfo _inventoryGetAllItems;

        private bool _haveZdoHashes;
        private int _zdoCheatedHash;
        private int _zdoCheatedQueuedHash;

        private int _patchCount;

        private void Awake()
        {
            try
            {
                ResolveContracts();

                _harmony = new Harmony(PluginGuid);
                InstallPatches();

                Logger.LogInfo(
                    PluginName + " " + PluginVersion +
                    " loaded with " + _patchCount +
                    " Harmony patch point" + (_patchCount == 1 ? "." : "s."));

                Logger.LogInfo(
                    "Native cheat bypass is forced in memory only; this plugin does not invoke the vanilla bypass command or write its saved bypasscheatchecks key.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not initialize " + PluginName + ": " + ex);
                try
                {
                    if (_harmony != null)
                        _harmony.UnpatchSelf();
                }
                catch
                {
                }
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_harmony != null)
                    _harmony.UnpatchSelf();
            }
            catch
            {
            }
        }

        private void ResolveContracts()
        {
            _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly =>
                    string.Equals(assembly.GetName().Name, "assembly_valheim", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assembly.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));

            if (_gameAssembly == null)
                throw new InvalidOperationException("Valheim gameplay assembly is not loaded.");

            _itemDataType = FindTypeWithField("ItemData", "m_cheated", typeof(bool));
            _inventoryType = FindType("Inventory");
            _itemDropType = FindType("ItemDrop");
            _characterDropType = FindType("CharacterDrop");
            _zdoType = FindType("ZDO");
            _zdoVarsType = FindType("ZDOVars");
            _playerProfileType = FindType("PlayerProfile");

            if (_itemDataType == null)
                throw new MissingMemberException("Could not resolve Valheim ItemData.m_cheated.");

            _itemCheatedField = FindField(_itemDataType, "m_cheated");
            if (_itemCheatedField == null || _itemCheatedField.FieldType != typeof(bool))
                throw new MissingFieldException(_itemDataType.FullName, "m_cheated");

            if (_inventoryType != null)
            {
                _inventoryGetAllItems = _inventoryType.GetMethod(
                    "GetAllItems",
                    AnyMember,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
            }

            if (_itemDropType != null)
                _itemDropItemDataField = FindField(_itemDropType, "m_itemData");

            if (_characterDropType != null)
                _characterDropCheatedField = FindField(_characterDropType, "m_cheated");

            ResolveZdoHashes();
        }

        private void ResolveZdoHashes()
        {
            if (_zdoVarsType == null)
                return;

            FieldInfo cheated = _zdoVarsType.GetField("s_cheated", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo queued = _zdoVarsType.GetField("s_cheatedQueued", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (cheated == null || queued == null ||
                cheated.FieldType != typeof(int) || queued.FieldType != typeof(int))
            {
                return;
            }

            _zdoCheatedHash = (int)cheated.GetValue(null);
            _zdoCheatedQueuedHash = (int)queued.GetValue(null);
            _haveZdoHashes = true;
        }

        private void InstallPatches()
        {
            PatchNativeBypassGetter();
            PatchEveryItemCheatedCheck();
            PatchEveryCheatedBoolArgument();
            PatchItemDataLifecycle();
            PatchInventoryLifecycle();
            PatchCharacterDropFallbacks();
            PatchWorldCheatFlags();
        }

        private void PatchNativeBypassGetter()
        {
            if (_playerProfileType == null)
            {
                Logger.LogWarning("PlayerProfile was not found; native bypass getter was not patched.");
                return;
            }

            MethodInfo getter = _playerProfileType.GetMethod(
                "get_s_bypassCheatChecks",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (getter == null || getter.ReturnType != typeof(bool))
            {
                Logger.LogWarning("PlayerProfile.s_bypassCheatChecks getter was not found.");
                return;
            }

            Patch(
                getter,
                prefix: HarmonyMethod(nameof(ForceTrueAndSkip)),
                postfix: null,
                "PlayerProfile.s_bypassCheatChecks -> true");
        }

        private void PatchEveryItemCheatedCheck()
        {
            foreach (Type type in GetLoadableTypes(_gameAssembly))
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(Declared);
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (method.IsAbstract ||
                        method.ContainsGenericParameters ||
                        method.ReturnType != typeof(bool) ||
                        !string.Equals(method.Name, "ItemCheated", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(ForceFalseAndSkip)),
                        postfix: null,
                        type.FullName + "." + method.Name + " -> false");
                }
            }
        }

        private void PatchEveryCheatedBoolArgument()
        {
            foreach (Type type in GetLoadableTypes(_gameAssembly))
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(Declared);
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                        continue;

                    ParameterInfo cheated = method.GetParameters()
                        .FirstOrDefault(parameter =>
                        {
                            Type parameterType = parameter.ParameterType;
                            if (parameterType.IsByRef)
                                parameterType = parameterType.GetElementType();

                            return parameterType == typeof(bool) &&
                                   string.Equals(parameter.Name, "cheated", StringComparison.Ordinal);
                        });

                    if (cheated == null)
                        continue;

                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(ForceCheatedArgumentFalse)),
                        postfix: null,
                        type.FullName + "." + method.Name + "(... cheated ...) -> false");
                }
            }
        }

        private void PatchItemDataLifecycle()
        {
            if (_itemDataType == null)
                return;

            foreach (MethodInfo method in SafeMethods(_itemDataType))
            {
                if (method.IsAbstract || method.ContainsGenericParameters)
                    continue;

                if (string.Equals(method.Name, "Load", StringComparison.Ordinal))
                {
                    Patch(
                        method,
                        prefix: null,
                        postfix: HarmonyMethod(
                            method.IsStatic
                                ? nameof(ScrubArgumentsPostfix)
                                : nameof(ScrubInstanceAndArgumentsPostfix)),
                        "ItemData.Load scrub");
                }
                else if (string.Equals(method.Name, "Save", StringComparison.Ordinal))
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(
                            method.IsStatic
                                ? nameof(ScrubArgumentsPrefix)
                                : nameof(ScrubInstanceAndArgumentsPrefix)),
                        postfix: null,
                        "ItemData.Save scrub");
                }
                else if (string.Equals(method.Name, "GetTooltip", StringComparison.Ordinal))
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(
                            method.IsStatic
                                ? nameof(ScrubArgumentsPrefix)
                                : nameof(ScrubInstanceAndArgumentsPrefix)),
                        postfix: null,
                        "ItemData.GetTooltip scrub");
                }
            }
        }

        private void PatchInventoryLifecycle()
        {
            if (_inventoryType == null)
                return;

            foreach (MethodInfo method in SafeMethods(_inventoryType))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.IsStatic)
                    continue;

                if (string.Equals(method.Name, "Load", StringComparison.Ordinal))
                {
                    Patch(
                        method,
                        prefix: null,
                        postfix: HarmonyMethod(nameof(ScrubInventoryPostfix)),
                        "Inventory.Load scrub");
                }
                else if (string.Equals(method.Name, "Save", StringComparison.Ordinal))
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(ScrubInventoryPrefix)),
                        postfix: null,
                        "Inventory.Save scrub");
                }
                else if (string.Equals(method.Name, "AddItem", StringComparison.Ordinal))
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(ScrubArgumentsPrefix)),
                        postfix: HarmonyMethod(nameof(ScrubInventoryPostfix)),
                        "Inventory.AddItem scrub");
                }
            }
        }

        private void PatchCharacterDropFallbacks()
        {
            if (_characterDropType == null)
                return;

            foreach (MethodInfo method in SafeMethods(_characterDropType))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.IsStatic)
                    continue;

                if (string.Equals(method.Name, "DropItems", StringComparison.Ordinal))
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(ScrubInstanceAndArgumentsPrefix)),
                        postfix: HarmonyMethod(nameof(ScrubInstancePostfix)),
                        "CharacterDrop.DropItems scrub");
                }
                else if (string.Equals(method.Name, "Awake", StringComparison.Ordinal) ||
                         string.Equals(method.Name, "Start", StringComparison.Ordinal))
                {
                    Patch(
                        method,
                        prefix: null,
                        postfix: HarmonyMethod(nameof(ScrubInstancePostfix)),
                        "CharacterDrop lifecycle scrub");
                }
            }
        }

        private void PatchWorldCheatFlags()
        {
            if (!_haveZdoHashes || _zdoType == null)
            {
                Logger.LogWarning("ZDOVars cheat hashes were not resolved; ZDO cheat flags were not patched.");
                return;
            }

            foreach (MethodInfo method in SafeMethods(_zdoType))
            {
                if (method.IsAbstract || method.ContainsGenericParameters)
                    continue;

                ParameterInfo[] parameters = method.GetParameters();

                if (string.Equals(method.Name, "Set", StringComparison.Ordinal) &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(int) &&
                    parameters[1].ParameterType == typeof(bool))
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(ZdoSetBoolPrefix)),
                        postfix: null,
                        "ZDO.Set cheat flag write filter");
                }
                else if (string.Equals(method.Name, "GetBool", StringComparison.Ordinal) &&
                         method.ReturnType == typeof(bool) &&
                         parameters.Length >= 1 &&
                         parameters[0].ParameterType == typeof(int))
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(ZdoGetBoolPrefix)),
                        postfix: null,
                        "ZDO.GetBool cheat flag read filter");
                }
            }
        }

        private HarmonyMethod HarmonyMethod(string name)
        {
            MethodInfo method = typeof(NoCheatTags).GetMethod(
                name,
                BindingFlags.Static | BindingFlags.NonPublic);

            if (method == null)
                throw new MissingMethodException(typeof(NoCheatTags).FullName, name);

            return new HarmonyMethod(method);
        }

        private void Patch(MethodBase target, HarmonyMethod prefix, HarmonyMethod postfix, string label)
        {
            if (target == null)
                return;

            try
            {
                _harmony.Patch(target, prefix, postfix);
                _patchCount++;
                Logger.LogDebug("Patched: " + label + " [" + target + "]");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not patch " + label + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool ForceTrueAndSkip(ref bool __result)
        {
            __result = true;
            return false;
        }

        private static bool ForceFalseAndSkip(ref bool __result)
        {
            __result = false;
            return false;
        }

        private static void ForceCheatedArgumentFalse(ref bool cheated)
        {
            cheated = false;
        }

        private static void ScrubArgumentsPrefix(object[] __args)
        {
            Instance?.ScrubArguments(__args);
        }

        private static void ScrubArgumentsPostfix(object[] __args)
        {
            Instance?.ScrubArguments(__args);
        }

        private static void ScrubInstanceAndArgumentsPrefix(object __instance, object[] __args)
        {
            NoCheatTags plugin = Instance;
            if (plugin == null)
                return;

            plugin.ScrubObjectShallow(__instance);
            plugin.ScrubArguments(__args);
        }

        private static void ScrubInstanceAndArgumentsPostfix(object __instance, object[] __args)
        {
            NoCheatTags plugin = Instance;
            if (plugin == null)
                return;

            plugin.ScrubObjectShallow(__instance);
            plugin.ScrubArguments(__args);
        }

        private static void ScrubInstancePostfix(object __instance)
        {
            Instance?.ScrubObjectShallow(__instance);
        }

        private static void ScrubInventoryPrefix(object __instance)
        {
            Instance?.ScrubInventory(__instance);
        }

        private static void ScrubInventoryPostfix(object __instance)
        {
            Instance?.ScrubInventory(__instance);
        }

        private static void ZdoSetBoolPrefix(int __0, ref bool __1)
        {
            NoCheatTags plugin = Instance;
            if (plugin != null && plugin.IsCheatZdoHash(__0))
                __1 = false;
        }

        private static bool ZdoGetBoolPrefix(int __0, ref bool __result)
        {
            NoCheatTags plugin = Instance;
            if (plugin == null || !plugin.IsCheatZdoHash(__0))
                return true;

            __result = false;
            return false;
        }

        private void ScrubArguments(object[] args)
        {
            if (args == null)
                return;

            foreach (object arg in args)
                ScrubObjectShallow(arg);
        }

        private void ScrubObjectShallow(object value)
        {
            if (value == null)
                return;

            try
            {
                Type actual = value.GetType();

                if (_itemDataType != null && _itemDataType.IsAssignableFrom(actual))
                {
                    _itemCheatedField.SetValue(value, false);
                    return;
                }

                if (_inventoryType != null && _inventoryType.IsAssignableFrom(actual))
                {
                    ScrubInventory(value);
                    return;
                }

                if (_itemDropType != null && _itemDropType.IsAssignableFrom(actual))
                {
                    if (_itemDropItemDataField != null)
                    {
                        object itemData = _itemDropItemDataField.GetValue(value);
                        if (itemData != null)
                            _itemCheatedField.SetValue(itemData, false);
                    }
                    return;
                }

                if (_characterDropType != null &&
                    _characterDropType.IsAssignableFrom(actual) &&
                    _characterDropCheatedField != null &&
                    _characterDropCheatedField.FieldType == typeof(bool))
                {
                    _characterDropCheatedField.SetValue(value, false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Scrub skipped: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ScrubInventory(object inventory)
        {
            if (inventory == null || _inventoryGetAllItems == null)
                return;

            try
            {
                object result = _inventoryGetAllItems.Invoke(inventory, null);
                IEnumerable items = result as IEnumerable;
                if (items == null)
                    return;

                foreach (object item in items)
                {
                    if (item != null &&
                        _itemDataType != null &&
                        _itemDataType.IsAssignableFrom(item.GetType()))
                    {
                        _itemCheatedField.SetValue(item, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Inventory scrub skipped: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private bool IsCheatZdoHash(int hash)
        {
            if (!_haveZdoHashes)
                return false;

            if (hash == _zdoCheatedHash)
                return true;

            unchecked
            {
                return (uint)(hash - _zdoCheatedQueuedHash) < QueuedCheatSlots;
            }
        }

        private Type FindType(string fullOrSimpleName)
        {
            Type direct = _gameAssembly.GetType(fullOrSimpleName, false, false);
            if (direct != null)
                return direct;

            return GetLoadableTypes(_gameAssembly)
                .FirstOrDefault(type =>
                    string.Equals(type.Name, fullOrSimpleName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, fullOrSimpleName, StringComparison.Ordinal));
        }

        private Type FindTypeWithField(string preferredName, string fieldName, Type fieldType)
        {
            IEnumerable<Type> types = GetLoadableTypes(_gameAssembly);

            Type preferred = types.FirstOrDefault(type =>
                string.Equals(type.Name, preferredName, StringComparison.Ordinal) &&
                HasField(type, fieldName, fieldType));

            if (preferred != null)
                return preferred;

            return types.FirstOrDefault(type => HasField(type, fieldName, fieldType));
        }

        private static bool HasField(Type type, string fieldName, Type fieldType)
        {
            FieldInfo field = FindField(type, fieldName);
            return field != null && field.FieldType == fieldType;
        }

        private static FieldInfo FindField(Type type, string fieldName)
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

        private static MethodInfo[] SafeMethods(Type type)
        {
            if (type == null)
                return new MethodInfo[0];

            try
            {
                return type.GetMethods(Declared);
            }
            catch
            {
                return new MethodInfo[0];
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null)
                yield break;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
                yield break;

            foreach (Type type in types)
            {
                if (type != null)
                    yield return type;
            }
        }

        private static NoCheatTags Instance { get; set; }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }
    }
}
