using BepInEx;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace NoCheatTagsMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class NoCheatTags : BaseUnityPlugin
    {
        public const string PluginGuid = "claire.valheim.nocheattags";
        public const string PluginName = "No Cheat Tags";
        public const string PluginVersion = "1.3.2";

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
        private Type _playerStatType;
        private Type _gameType;

        private FieldInfo _itemCheatedField;
        private FieldInfo _itemDropItemDataField;
        private FieldInfo _characterDropCheatedField;
        private FieldInfo _inventoryItemsField;

        private static FieldInfo s_itemCheatedField;
        private static FieldInfo s_characterDropCheatedField;

        private FieldInfo _profileUsedCheatsField;
        private FieldInfo _profilePlayerStatsField;
        private FieldInfo _playerStatsStatsField;
        private object _cheatsStatKey;

        private FieldInfo _gameInstanceField;
        private PropertyInfo _gameInstanceProperty;
        private MethodInfo _gameGetPlayerProfileMethod;

        private bool _haveZdoHashes;
        private int _zdoCheatedHash;
        private int _zdoCheatedQueuedHash;

        private int _patchCount;

        private void Awake()
        {
            Instance = this;

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
                Logger.LogInfo(
                    "PlayerProfile.m_usedCheats is suppressed/cleared and PlayerStatType.Cheats is held at zero; known command history is left alone.");
                Logger.LogInfo(
                    "Item cheat cleanup is event-driven: vanilla m_cheated writes are forced false and inventories are checked once when loaded; no periodic polling is used.");
                Logger.LogInfo(
                    "ItemData resolver used exact nested-type lookup first (ItemDrop+ItemData).");
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
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            // Valheim 1.0 keeps the actual gameplay classes (ItemDrop, Inventory,
            // PlayerProfile, etc.) in assembly_valheim.dll. Assembly-CSharp.dll
            // still exists, but is only a small unrelated/sample assembly. The
            // previous OR-based FirstOrDefault could therefore bind to
            // Assembly-CSharp first on Linux/Mono and make every gameplay lookup
            // fail even though assembly_valheim was loaded.
            _gameAssembly = loadedAssemblies.FirstOrDefault(assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    "assembly_valheim",
                    StringComparison.OrdinalIgnoreCase));

            if (_gameAssembly == null)
            {
                _gameAssembly = loadedAssemblies.FirstOrDefault(assembly =>
                    string.Equals(
                        assembly.GetName().Name,
                        "Assembly-CSharp",
                        StringComparison.OrdinalIgnoreCase));
            }

            if (_gameAssembly == null)
                throw new InvalidOperationException("Valheim gameplay assembly is not loaded.");

            Logger.LogInfo("Selected gameplay assembly: " + _gameAssembly.GetName().Name);

            _itemDropType = FindType("ItemDrop");
            _itemDataType = ResolveItemDataType();
            _inventoryType = FindType("Inventory");
            _characterDropType = FindType("CharacterDrop");
            _zdoType = FindType("ZDO");
            _zdoVarsType = FindType("ZDOVars");
            _playerProfileType = FindType("PlayerProfile");
            _playerStatType = FindType("PlayerStatType");
            _gameType = FindType("Game");

            if (_itemDataType == null)
                throw new MissingMemberException("Could not resolve Valheim ItemData.m_cheated.");

            _itemCheatedField = FindField(_itemDataType, "m_cheated");
            if (_itemCheatedField == null || _itemCheatedField.FieldType != typeof(bool))
                throw new MissingFieldException(_itemDataType.FullName, "m_cheated");

            if (_inventoryType != null)
                _inventoryItemsField = FindField(_inventoryType, "m_inventory");

            if (_itemDropType != null)
                _itemDropItemDataField = FindField(_itemDropType, "m_itemData");

            if (_characterDropType != null)
                _characterDropCheatedField = FindField(_characterDropType, "m_cheated");

            s_itemCheatedField = _itemCheatedField;
            s_characterDropCheatedField = _characterDropCheatedField;

            ResolveProfileHistoryContracts();
            ResolveGameProfileAccess();
            ResolveZdoHashes();

            if (_inventoryItemsField == null)
                Logger.LogWarning("Inventory.m_inventory was not found; event-boundary inventory cleanup is unavailable.");

            Logger.LogInfo(
                "Resolved current Valheim cheat fields from assembly " +
                _gameAssembly.GetName().Name + ": " +
                _itemDataType.FullName + ".m_cheated" +
                (_characterDropCheatedField != null ? " and CharacterDrop.m_cheated." : "."));
        }


        private Type ResolveItemDataType()
        {
            // In current Valheim this is the nested type ItemDrop.ItemData
            // (runtime name ItemDrop+ItemData). Prefer exact nested/direct lookup
            // before any assembly-wide type enumeration. On some Linux Mono
            // setups GetTypes() can return a partial list even though the nested
            // gameplay type itself is fully usable.
            Type nested = null;

            if (_itemDropType != null)
            {
                try
                {
                    nested = _itemDropType.GetNestedType(
                        "ItemData",
                        BindingFlags.Public | BindingFlags.NonPublic);
                }
                catch
                {
                }

                if (nested != null && HasField(nested, "m_cheated", typeof(bool)))
                    return nested;
            }

            try
            {
                Type direct = _gameAssembly.GetType("ItemDrop+ItemData", false, false);
                if (direct != null && HasField(direct, "m_cheated", typeof(bool)))
                    return direct;
            }
            catch
            {
            }

            return FindTypeWithField("ItemData", "m_cheated", typeof(bool));
        }


        private void ResolveProfileHistoryContracts()
        {
            if (_playerProfileType == null)
                return;

            _profileUsedCheatsField = FindField(_playerProfileType, "m_usedCheats");
            _profilePlayerStatsField = FindField(_playerProfileType, "m_playerStats");

            if (_profileUsedCheatsField == null || _profileUsedCheatsField.FieldType != typeof(bool))
            {
                Logger.LogWarning("PlayerProfile.m_usedCheats was not found; that history flag cannot be scrubbed.");
                _profileUsedCheatsField = null;
            }

            if (_playerStatType == null || !_playerStatType.IsEnum)
            {
                Logger.LogWarning("PlayerStatType enum was not found; the Cheats counter cannot be blocked.");
                return;
            }

            try
            {
                _cheatsStatKey = Enum.Parse(_playerStatType, "Cheats", ignoreCase: false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("PlayerStatType.Cheats was not found: " + ex.Message);
                _cheatsStatKey = null;
                return;
            }

            if (_profilePlayerStatsField == null)
            {
                Logger.LogWarning("PlayerProfile.m_playerStats was not found; the Cheats counter cannot be scrubbed.");
                return;
            }

            Type statsType = _profilePlayerStatsField.FieldType.IsArray
                ? _profilePlayerStatsField.FieldType.GetElementType()
                : null;

            if (statsType == null)
                statsType = FindType("PlayerStats");

            if (statsType != null)
                _playerStatsStatsField = FindField(statsType, "m_stats");

            if (_playerStatsStatsField == null)
                Logger.LogWarning("PlayerStats.m_stats was not found; the Cheats counter cannot be scrubbed.");
        }

        private void ResolveGameProfileAccess()
        {
            if (_gameType == null)
                return;

            _gameInstanceField = _gameType.GetField(
                "instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            _gameInstanceProperty = _gameType.GetProperty(
                "instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            _gameGetPlayerProfileMethod = _gameType.GetMethod(
                "GetPlayerProfile",
                AnyMember,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
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
            PatchPlayerProfileHistory();
            PatchConsoleCheatHistoryWrites();
            PatchEveryItemCheatedCheck();
            PatchEveryCheatedBoolArgument();
            PatchCheatFieldWriters();
            PatchSerializationBoundaries();
            PatchInventoryIngress();
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


        private void PatchPlayerProfileHistory()
        {
            if (_playerProfileType == null)
                return;

            foreach (MethodInfo method in SafeMethods(_playerProfileType))
            {
                if (method.IsAbstract || method.ContainsGenericParameters)
                    continue;

                if (string.Equals(method.Name, "IncrementStat", StringComparison.Ordinal) &&
                    _cheatsStatKey != null &&
                    method.GetParameters().Any(parameter =>
                    {
                        Type type = parameter.ParameterType;
                        if (type.IsByRef)
                            type = type.GetElementType();
                        return type == _playerStatType;
                    }))
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(BlockCheatStatIncrementPrefix)),
                        postfix: null,
                        "PlayerProfile.IncrementStat(PlayerStatType.Cheats) block");
                    continue;
                }

                if (method.IsStatic)
                    continue;

                if (method.Name.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Patch(
                        method,
                        prefix: null,
                        postfix: HarmonyMethod(nameof(ScrubProfilePostfix)),
                        "PlayerProfile load/history scrub");
                }
                else if (method.Name.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Patch(
                        method,
                        prefix: HarmonyMethod(nameof(ScrubProfilePrefix)),
                        postfix: null,
                        "PlayerProfile save/history scrub");
                }
            }
        }

        private void PatchConsoleCheatHistoryWrites()
        {
            Type terminalType = FindType("Terminal");
            if (terminalType == null)
            {
                Logger.LogWarning("Terminal was not found; direct m_usedCheats write suppression was not installed.");
                return;
            }

            Type consoleCommandType = terminalType
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(type => string.Equals(type.Name, "ConsoleCommand", StringComparison.Ordinal));

            if (consoleCommandType == null)
            {
                Logger.LogWarning("Terminal.ConsoleCommand was not found; direct m_usedCheats write suppression was not installed.");
                return;
            }

            foreach (MethodInfo method in SafeMethods(consoleCommandType))
            {
                if (!string.Equals(method.Name, "RunAction", StringComparison.Ordinal) ||
                    method.IsAbstract ||
                    method.ContainsGenericParameters)
                {
                    continue;
                }

                PatchWithTranspiler(
                    method,
                    HarmonyMethod(nameof(PreventUsedCheatsWriteTranspiler)),
                    "Terminal.ConsoleCommand.RunAction m_usedCheats write removal");

                Patch(
                    method,
                    prefix: HarmonyMethod(nameof(ScrubActiveProfilePrefix)),
                    postfix: HarmonyMethod(nameof(ScrubActiveProfilePostfix)),
                    "Terminal.ConsoleCommand.RunAction profile history scrub");
            }
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

        private void PatchCheatFieldWriters()
        {
            // Verified against the current Valheim 1.0.16 decompile. These are the
            // vanilla methods that directly store ItemData.m_cheated or
            // CharacterDrop.m_cheated. The transpiler changes only those exact
            // field stores, replacing the value with false.
            PatchCheatFieldStores(_itemDataType, "Load");
            PatchCheatFieldStores(_inventoryType, "AddItem");
            PatchCheatFieldStores(_itemDropType, "OnCreateNew");
            PatchCheatFieldStores(_characterDropType, "DropItems");
            PatchCheatFieldStores(FindType("Character"), "OnDeath");
            PatchCheatFieldStores(FindType("Piece"), "DropResources");
            PatchCheatFieldStores(FindType("Container"), "AddDefaultItems");
            PatchCheatFieldStores(FindType("ZDOMan"), "ConvertInventories");
        }

        private void PatchCheatFieldStores(Type type, string methodName)
        {
            if (type == null)
                return;

            foreach (MethodInfo method in SafeMethods(type))
            {
                if (method.IsAbstract ||
                    method.ContainsGenericParameters ||
                    !string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                PatchWithTranspiler(
                    method,
                    HarmonyMethod(nameof(ForceCheatFieldWritesFalseTranspiler)),
                    type.FullName + "." + method.Name + " cheat-field stores -> false");
            }
        }

        private void PatchSerializationBoundaries()
        {
            if (_itemDataType != null)
            {
                foreach (MethodInfo method in SafeMethods(_itemDataType))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                        continue;

                    if (string.Equals(method.Name, "Save", StringComparison.Ordinal))
                    {
                        Patch(
                            method,
                            prefix: HarmonyMethod(
                                method.IsStatic
                                    ? nameof(ScrubArgumentsPrefix)
                                    : nameof(ScrubInstanceAndArgumentsPrefix)),
                            postfix: null,
                            "ItemData.Save clean-before-serialize");
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
                            "ItemData.GetTooltip clean-on-inspect");
                    }
                }
            }

            if (_inventoryType != null)
            {
                foreach (MethodInfo method in SafeMethods(_inventoryType))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.IsStatic)
                        continue;

                    if (string.Equals(method.Name, "Load", StringComparison.Ordinal))
                    {
                        Patch(
                            method,
                            prefix: null,
                            postfix: HarmonyMethod(nameof(ScrubInventoryLoadPostfix)),
                            "Inventory.Load final one-shot cleanup");
                    }
                    else if (string.Equals(method.Name, "Save", StringComparison.Ordinal) ||
                             string.Equals(method.Name, "OldSave", StringComparison.Ordinal))
                    {
                        Patch(
                            method,
                            prefix: HarmonyMethod(nameof(ScrubInventoryPrefix)),
                            postfix: null,
                            "Inventory save clean-before-serialize");
                    }
                }
            }

            if (_itemDropType != null)
            {
                foreach (MethodInfo method in SafeMethods(_itemDropType))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                        continue;

                    if (string.Equals(method.Name, "SaveToZDO", StringComparison.Ordinal) ||
                        string.Equals(method.Name, "DropItem", StringComparison.Ordinal))
                    {
                        Patch(
                            method,
                            prefix: HarmonyMethod(nameof(ScrubArgumentsPrefix)),
                            postfix: null,
                            "ItemDrop." + method.Name + " clean item input");
                    }
                }
            }
        }

        private void PatchInventoryIngress()
        {
            if (_inventoryType == null)
                return;

            foreach (MethodInfo method in SafeMethods(_inventoryType))
            {
                if (method.IsAbstract ||
                    method.ContainsGenericParameters ||
                    !string.Equals(method.Name, "AddItem", StringComparison.Ordinal))
                {
                    continue;
                }

                Patch(
                    method,
                    prefix: HarmonyMethod(nameof(ScrubArgumentsPrefix)),
                    postfix: null,
                    "Inventory.AddItem clean incoming ItemData");
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

        private void PatchWithTranspiler(MethodBase target, HarmonyMethod transpiler, string label)
        {
            if (target == null)
                return;

            try
            {
                _harmony.Patch(target, transpiler: transpiler);
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


        private static bool BlockCheatStatIncrementPrefix(object[] __args)
        {
            NoCheatTags plugin = Instance;
            if (plugin == null || plugin._cheatsStatKey == null || __args == null)
                return true;

            foreach (object arg in __args)
            {
                if (arg != null &&
                    plugin._playerStatType != null &&
                    arg.GetType() == plugin._playerStatType &&
                    Equals(arg, plugin._cheatsStatKey))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<CodeInstruction> PreventUsedCheatsWriteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            NoCheatTags plugin = Instance;
            FieldInfo target = plugin == null ? null : plugin._profileUsedCheatsField;

            foreach (CodeInstruction instruction in instructions)
            {
                FieldInfo writtenField = instruction.operand as FieldInfo;

                if (target != null &&
                    instruction.opcode == OpCodes.Stfld &&
                    SameField(writtenField, target))
                {
                    CodeInstruction popValue = new CodeInstruction(OpCodes.Pop);
                    if (instruction.labels != null)
                        popValue.labels.AddRange(instruction.labels);

                    CodeInstruction popInstance = new CodeInstruction(OpCodes.Pop);
                    if (instruction.blocks != null)
                        popInstance.blocks.AddRange(instruction.blocks);

                    yield return popValue;
                    yield return popInstance;
                    continue;
                }

                yield return instruction;
            }
        }

        private static IEnumerable<CodeInstruction> ForceCheatFieldWritesFalseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                FieldInfo writtenField = instruction.operand as FieldInfo;
                bool targetStore =
                    instruction.opcode == OpCodes.Stfld &&
                    (SameField(writtenField, s_itemCheatedField) ||
                     SameField(writtenField, s_characterDropCheatedField));

                if (!targetStore)
                {
                    yield return instruction;
                    continue;
                }

                // stfld consumes [instance, value]. Throw away the calculated value,
                // push false, then perform the original store. Move any branch labels
                // / exception-block markers to the first replacement instruction.
                CodeInstruction popValue = new CodeInstruction(OpCodes.Pop);
                if (instruction.labels != null)
                {
                    popValue.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                }
                if (instruction.blocks != null)
                {
                    popValue.blocks.AddRange(instruction.blocks);
                    instruction.blocks.Clear();
                }

                yield return popValue;
                yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                yield return instruction;
            }
        }

        private static bool SameField(FieldInfo left, FieldInfo right)
        {
            if (left == null || right == null)
                return false;

            if (ReferenceEquals(left, right) || left.Equals(right))
                return true;

            try
            {
                return left.Module == right.Module && left.MetadataToken == right.MetadataToken;
            }
            catch
            {
                return false;
            }
        }

        private static void ScrubProfilePrefix(object __instance)
        {
            Instance?.ScrubProfile(__instance);
        }

        private static void ScrubProfilePostfix(object __instance)
        {
            Instance?.ScrubProfile(__instance);
        }

        private static void ScrubActiveProfilePrefix()
        {
            Instance?.ScrubActiveProfile();
        }

        private static void ScrubActiveProfilePostfix()
        {
            Instance?.ScrubActiveProfile();
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

        private static void ScrubInventoryLoadPostfix(object __instance)
        {
            NoCheatTags plugin = Instance;
            if (plugin == null)
                return;

            int cleaned = plugin.ScrubInventory(__instance);
            if (cleaned > 0)
            {
                plugin.Logger.LogInfo(
                    "Inventory.Load removed " + cleaned +
                    " pre-existing cheated item tag" + (cleaned == 1 ? "." : "s."));
            }
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



        private void ScrubActiveProfile()
        {
            if (_gameType == null || _gameGetPlayerProfileMethod == null)
                return;

            try
            {
                object game = null;

                if (_gameInstanceField != null)
                    game = _gameInstanceField.GetValue(null);

                if (game == null && _gameInstanceProperty != null)
                    game = _gameInstanceProperty.GetValue(null, null);

                if (game == null)
                    return;

                object profile = _gameGetPlayerProfileMethod.Invoke(game, null);
                ScrubProfile(profile);
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Active profile history scrub skipped: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ScrubProfile(object profile)
        {
            if (profile == null ||
                _playerProfileType == null ||
                !_playerProfileType.IsAssignableFrom(profile.GetType()))
            {
                return;
            }

            try
            {
                if (_profileUsedCheatsField != null)
                    _profileUsedCheatsField.SetValue(profile, false);

                if (_profilePlayerStatsField == null ||
                    _playerStatsStatsField == null ||
                    _cheatsStatKey == null)
                {
                    return;
                }

                object statsContainer = _profilePlayerStatsField.GetValue(profile);
                IEnumerable statsEntries = statsContainer as IEnumerable;
                if (statsEntries == null)
                    return;

                foreach (object stats in statsEntries)
                {
                    if (stats == null)
                        continue;

                    IDictionary dictionary = _playerStatsStatsField.GetValue(stats) as IDictionary;
                    if (dictionary != null)
                        dictionary[_cheatsStatKey] = 0f;
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug("PlayerProfile history scrub skipped: " + ex.GetType().Name + ": " + ex.Message);
            }
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

                if (_playerProfileType != null && _playerProfileType.IsAssignableFrom(actual))
                {
                    ScrubProfile(value);
                    return;
                }

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

        private int ScrubInventory(object inventory)
        {
            if (inventory == null ||
                _inventoryItemsField == null ||
                _itemDataType == null ||
                _itemCheatedField == null)
            {
                return 0;
            }

            int cleaned = 0;

            try
            {
                IEnumerable items = _inventoryItemsField.GetValue(inventory) as IEnumerable;
                if (items == null)
                    return 0;

                foreach (object item in items)
                {
                    if (item == null || !_itemDataType.IsAssignableFrom(item.GetType()))
                        continue;

                    object current = _itemCheatedField.GetValue(item);
                    if (current is bool cheated && cheated)
                    {
                        _itemCheatedField.SetValue(item, false);
                        cleaned++;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Inventory cleanup skipped: " + ex.GetType().Name + ": " + ex.Message);
            }

            return cleaned;
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
