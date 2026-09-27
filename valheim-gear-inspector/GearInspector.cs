using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace GearInspectorMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class GearInspector : BaseUnityPlugin
    {
        public const string PluginGuid = "claire.valheim.gearinspector";
        public const string PluginName = "Gear Inspector";
        public const string PluginVersion = "1.0.0";

        private const int WindowId = 294117;
        private static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] Categories = { "All", "Armor", "Weapons", "Shields", "Tools", "Utility", "Ammo", "Other" };
        private static readonly string[] EquippedItemFields =
        {
            "m_rightItem", "m_leftItem", "m_chestItem", "m_legItem", "m_helmetItem",
            "m_shoulderItem", "m_utilityItem", "m_ammoItem", "m_hiddenLeftItem", "m_hiddenRightItem"
        };

        private readonly List<GearEntry> _entries = new List<GearEntry>();
        private readonly List<object> _previewItems = new List<object>();
        private readonly List<object> _originalItems = new List<object>();

        private ConfigEntry<KeyCode> _toggleKey;
        private ConfigEntry<bool> _previewOnSelect;

        private Rect _windowRect = new Rect(32f, 32f, 1180f, 720f);
        private Vector2 _itemScroll;
        private Vector2 _detailScroll;
        private string _search = string.Empty;
        private string _category = "All";
        private GearEntry _selected;
        private int _variant;
        private bool _visible;
        private string _status = "Open the inspector in a world and press Rescan if another mod registers items later.";

        private bool _snapshotTaken;
        private object _previewPlayer;
        private bool _cursorSaved;
        private bool _savedCursorVisible;
        private CursorLockMode _savedCursorLock;

        private Type _objectDbType;
        private Type _itemDropType;
        private Type _playerType;
        private Type _localizationType;

        private void Awake()
        {
            _toggleKey = Config.Bind(
                "General",
                "ToggleKey",
                KeyCode.F8,
                "Key used to open/close Gear Inspector.");

            _previewOnSelect = Config.Bind(
                "General",
                "PreviewOnSelect",
                true,
                "If true, clicking an item immediately equips a temporary preview clone.");

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Toggle: {_toggleKey.Value}.");
        }

        private void OnDestroy()
        {
            RestorePreview();
            RestoreCursor();
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey.Value))
                SetVisible(!_visible);

            if (_visible)
                ForceCursor();
        }

        private void OnGUI()
        {
            if (!_visible)
                return;

            ForceCursor();

            float maxWidth = Mathf.Max(720f, Screen.width - 24f);
            float maxHeight = Mathf.Max(520f, Screen.height - 24f);
            _windowRect.width = Mathf.Min(_windowRect.width, maxWidth);
            _windowRect.height = Mathf.Min(_windowRect.height, maxHeight);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));

            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, $"{PluginName} {PluginVersion}");
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;

            _visible = visible;

            if (_visible)
            {
                SaveCursor();
                ForceCursor();
                ScanItems();
            }
            else
            {
                RestorePreview();
                RestoreCursor();
            }
        }

        private void SaveCursor()
        {
            if (_cursorSaved)
                return;

            _savedCursorVisible = Cursor.visible;
            _savedCursorLock = Cursor.lockState;
            _cursorSaved = true;
        }

        private void ForceCursor()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void RestoreCursor()
        {
            if (!_cursorSaved)
                return;

            Cursor.visible = _savedCursorVisible;
            Cursor.lockState = _savedCursorLock;
            _cursorSaved = false;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan", GUILayout.Width(88f)))
                ScanItems();

            if (GUILayout.Button("Dump JSON", GUILayout.Width(96f)))
                DumpJson();

            if (GUILayout.Button("Restore loadout", GUILayout.Width(130f)))
                RestorePreview();

            GUILayout.FlexibleSpace();
            GUILayout.Label(_status ?? string.Empty);
            GUILayout.Space(8f);
            if (GUILayout.Button("Close", GUILayout.Width(72f)))
                SetVisible(false);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();

            DrawFilters();
            GUILayout.Space(8f);
            DrawItemList();
            GUILayout.Space(8f);
            DrawDetails();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawFilters()
        {
            GUILayout.BeginVertical(GUILayout.Width(185f));

            GUILayout.Label("Search");
            string nextSearch = GUILayout.TextField(_search ?? string.Empty);
            if (!string.Equals(nextSearch, _search, StringComparison.Ordinal))
            {
                _search = nextSearch;
                _itemScroll = Vector2.zero;
            }

            GUILayout.Space(8f);
            GUILayout.Label("Category");
            foreach (string category in Categories)
            {
                string label = string.Equals(_category, category, StringComparison.Ordinal)
                    ? $"> {category} ({CountCategory(category)})"
                    : $"{category} ({CountCategory(category)})";

                if (GUILayout.Button(label))
                {
                    _category = category;
                    _itemScroll = Vector2.zero;
                }
            }

            GUILayout.Space(10f);
            bool previewOnSelect = GUILayout.Toggle(_previewOnSelect.Value, "Preview on select");
            if (previewOnSelect != _previewOnSelect.Value)
                _previewOnSelect.Value = previewOnSelect;

            GUILayout.Space(10f);
            GUILayout.Label("The list comes from the live ObjectDB. Mod-added items appear too if they register normally before the scan.");

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Total equipable: {_entries.Count}");

            GUILayout.EndVertical();
        }

        private void DrawItemList()
        {
            GUILayout.BeginVertical(GUILayout.Width(390f));
            GUILayout.Label("Equipable items");

            int visibleCount = 0;
            _itemScroll = GUILayout.BeginScrollView(_itemScroll, GUI.skin.box);

            foreach (GearEntry entry in _entries)
            {
                if (!MatchesFilter(entry))
                    continue;

                visibleCount++;
                string recipeMark = entry.HasRecipe ? "" : " [no recipe]";
                string variantMark = entry.Variants > 1 ? $" [{entry.Variants} variants]" : "";
                string selectedMark = ReferenceEquals(_selected, entry) ? "> " : "";
                string label = $"{selectedMark}{entry.DisplayName}\n    {entry.PrefabName}{variantMark}{recipeMark}";

                if (GUILayout.Button(label, GUILayout.Height(44f)))
                {
                    _selected = entry;
                    _variant = 0;
                    _detailScroll = Vector2.zero;
                    _status = $"Selected {entry.PrefabName}.";

                    if (_previewOnSelect.Value)
                        PreviewSelected();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Label($"Showing {visibleCount}");

            GUILayout.EndVertical();
        }

        private void DrawDetails()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("Selected item");

            if (_selected == null)
            {
                GUILayout.Label("Select an item to inspect its prefab ID and technical fields.");
                GUILayout.EndVertical();
                return;
            }

            _detailScroll = GUILayout.BeginScrollView(_detailScroll, GUI.skin.box);

            GUILayout.Label(_selected.DisplayName);
            GUILayout.Space(4f);
            FieldLine("Prefab / spawn ID", _selected.PrefabName);
            FieldLine("Stable hash", _selected.StableHash.ToString());
            FieldLine("Localization token", _selected.NameToken);
            FieldLine("Item type", _selected.ItemType);
            FieldLine("Category", _selected.Category);
            FieldLine("Recipe-backed", _selected.HasRecipe ? "yes" : "no (not proof that it is unobtainable)");
            FieldLine("Variants", _selected.Variants.ToString());
            FieldLine("Set", EmptyAsDash(_selected.SetName));
            FieldLine("Set size", _selected.SetSize.ToString());
            FieldLine("Attach override", EmptyAsDash(_selected.AttachOverride));
            FieldLine("Equip status effect", EmptyAsDash(_selected.EquipStatusEffect));
            FieldLine("Movement modifier", EmptyAsDash(_selected.MovementModifier));
            FieldLine("Armor", EmptyAsDash(_selected.Armor));
            FieldLine("Armor / level", EmptyAsDash(_selected.ArmorPerLevel));
            FieldLine("Weight", EmptyAsDash(_selected.Weight));

            GUILayout.Space(10f);
            GUILayout.Label("Spawn command");
            GUILayout.TextField($"spawn {_selected.PrefabName} 1");

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy prefab ID"))
            {
                GUIUtility.systemCopyBuffer = _selected.PrefabName;
                _status = "Prefab ID copied.";
            }

            if (GUILayout.Button("Copy spawn command"))
            {
                GUIUtility.systemCopyBuffer = $"spawn {_selected.PrefabName} 1";
                _status = "Spawn command copied.";
            }

            if (GUILayout.Button("Copy technical block"))
            {
                GUIUtility.systemCopyBuffer = BuildTechnicalBlock(_selected);
                _status = "Technical block copied.";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(12f);
            GUILayout.Label("Fitting-room preview");

            int maxVariant = Mathf.Max(0, _selected.Variants - 1);
            _variant = Mathf.Clamp(_variant, 0, maxVariant);

            GUILayout.BeginHorizontal();
            GUI.enabled = _selected.Variants > 1;
            if (GUILayout.Button("< Variant", GUILayout.Width(92f)))
            {
                _variant = (_variant - 1 + _selected.Variants) % _selected.Variants;
                PreviewSelected();
            }

            GUILayout.Label($"Variant {_variant} / {maxVariant}", GUILayout.Width(110f));

            if (GUILayout.Button("Variant >", GUILayout.Width(92f)))
            {
                _variant = (_variant + 1) % _selected.Variants;
                PreviewSelected();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Preview now", GUILayout.Width(100f)))
                PreviewSelected();

            if (GUILayout.Button("Restore original gear", GUILayout.Width(145f)))
                RestorePreview();

            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Preview clones are not added to your inventory. The first preview snapshots your equipped loadout; closing the inspector restores it.");

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void FieldLine(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(142f));
            GUILayout.TextField(value ?? string.Empty);
            GUILayout.EndHorizontal();
        }

        private void ScanItems()
        {
            _entries.Clear();
            _selected = null;
            _variant = 0;

            ResolveTypes();

            object objectDb = GetStaticSingleton(_objectDbType, "instance");
            if (objectDb == null)
            {
                _status = "ObjectDB is not ready. Enter a world, then press Rescan.";
                return;
            }

            object itemCollection = GetMember(objectDb, "m_items");
            if (!(itemCollection is IEnumerable items))
            {
                _status = "Could not read ObjectDB.m_items.";
                return;
            }

            HashSet<string> recipeItems = ReadRecipePrefabNames(objectDb);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (object rawPrefab in items)
            {
                GameObject prefab = rawPrefab as GameObject;
                if (prefab == null || string.IsNullOrEmpty(prefab.name) || !seen.Add(prefab.name))
                    continue;

                Component itemDrop = null;
                try
                {
                    if (_itemDropType != null)
                        itemDrop = prefab.GetComponent(_itemDropType);
                }
                catch
                {
                    // A malformed prefab should not break the whole browser.
                }

                if (itemDrop == null)
                    continue;

                object itemData = GetMember(itemDrop, "m_itemData");
                if (itemData == null || !IsEquipable(itemData))
                    continue;

                object shared = GetMember(itemData, "m_shared");
                if (shared == null)
                    continue;

                string prefabName = prefab.name;
                string token = Convert.ToString(GetMember(shared, "m_name")) ?? string.Empty;
                string itemType = ValueToString(GetMember(shared, "m_itemType"));
                int variants = Math.Max(1, ReadInt(shared, "m_variants", 1));

                GearEntry entry = new GearEntry
                {
                    PrefabName = prefabName,
                    StableHash = StableHash(prefabName),
                    NameToken = token,
                    DisplayName = Localize(token, prefabName),
                    ItemType = itemType,
                    Category = CategoryForType(itemType),
                    Variants = variants,
                    SetName = ValueToString(GetMember(shared, "m_setName")),
                    SetSize = ReadInt(shared, "m_setSize", 0),
                    AttachOverride = ValueToString(GetMember(shared, "m_attachOverride")),
                    EquipStatusEffect = ObjectName(GetMember(shared, "m_equipStatusEffect")),
                    MovementModifier = ValueToString(GetMember(shared, "m_movementModifier")),
                    Armor = ValueToString(GetMember(shared, "m_armor")),
                    ArmorPerLevel = ValueToString(GetMember(shared, "m_armorPerLevel")),
                    Weight = ValueToString(GetMember(shared, "m_weight")),
                    HasRecipe = recipeItems.Contains(prefabName),
                    ItemData = itemData
                };

                _entries.Add(entry);
            }

            _entries.Sort((a, b) =>
            {
                int byName = string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.Compare(a.PrefabName, b.PrefabName, StringComparison.OrdinalIgnoreCase);
            });

            _status = $"Scanned {_entries.Count} equipable ObjectDB items.";
        }

        private HashSet<string> ReadRecipePrefabNames(object objectDb)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            object rawRecipes = GetMember(objectDb, "m_recipes");
            if (!(rawRecipes is IEnumerable recipes))
                return names;

            foreach (object recipe in recipes)
            {
                if (recipe == null)
                    continue;

                object item = GetMember(recipe, "m_item");
                if (item == null)
                    continue;

                if (item is Component component && component.gameObject != null)
                {
                    names.Add(component.gameObject.name);
                    continue;
                }

                GameObject gameObject = GetMember(item, "gameObject") as GameObject;
                if (gameObject != null)
                    names.Add(gameObject.name);
            }

            return names;
        }

        private bool IsEquipable(object itemData)
        {
            MethodInfo method = FindMethod(itemData.GetType(), "IsEquipable", 0);
            if (method != null)
            {
                try
                {
                    object result = method.Invoke(itemData, null);
                    if (result is bool boolResult)
                        return boolResult;
                }
                catch
                {
                    // Fall back to the item type below.
                }
            }

            object shared = GetMember(itemData, "m_shared");
            string type = ValueToString(GetMember(shared, "m_itemType"));
            string category = CategoryForType(type);
            return !string.Equals(category, "Other", StringComparison.Ordinal);
        }

        private void PreviewSelected()
        {
            if (_selected == null)
                return;

            ResolveTypes();
            object player = GetStaticSingleton(_playerType, "m_localPlayer");
            if (player == null)
                player = GetStaticSingleton(_playerType, "localPlayer");

            if (player == null)
            {
                _status = "No local player is available yet.";
                return;
            }

            try
            {
                if (!_snapshotTaken || !ReferenceEquals(_previewPlayer, player))
                {
                    RestorePreview();
                    SnapshotLoadout(player);
                }

                object clone = CloneItemData(_selected.ItemData);
                if (clone == null)
                {
                    _status = $"Could not clone {_selected.PrefabName}.";
                    return;
                }

                SetMember(clone, "m_variant", _variant);

                if (!InvokeItemMethod(player, "EquipItem", clone))
                {
                    _status = $"Valheim refused to equip {_selected.PrefabName}.";
                    return;
                }

                _previewItems.Add(clone);
                _status = $"Previewing {_selected.PrefabName} variant {_variant}.";
            }
            catch (Exception ex)
            {
                _status = $"Preview failed: {ex.GetType().Name}: {ex.Message}";
                Logger.LogWarning(_status);
            }
        }

        private void SnapshotLoadout(object player)
        {
            _originalItems.Clear();

            foreach (string fieldName in EquippedItemFields)
            {
                object item = GetMember(player, fieldName);
                if (item != null && !ContainsReference(_originalItems, item))
                    _originalItems.Add(item);
            }

            _previewPlayer = player;
            _snapshotTaken = true;
            _status = $"Saved {_originalItems.Count} equipped item reference(s).";
        }

        private void RestorePreview()
        {
            if (!_snapshotTaken)
            {
                _previewItems.Clear();
                return;
            }

            object player = _previewPlayer;

            if (player != null)
            {
                for (int i = _previewItems.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        InvokeItemMethod(player, "UnequipItem", _previewItems[i]);
                    }
                    catch
                    {
                        // Continue restoring the rest.
                    }
                }

                foreach (object original in _originalItems)
                {
                    try
                    {
                        InvokeItemMethod(player, "EquipItem", original);
                    }
                    catch
                    {
                        // A stale/destroyed item should not prevent the remaining loadout from restoring.
                    }
                }
            }

            _previewItems.Clear();
            _originalItems.Clear();
            _previewPlayer = null;
            _snapshotTaken = false;
            _status = "Original loadout restored.";
        }

        private object CloneItemData(object itemData)
        {
            if (itemData == null)
                return null;

            MethodInfo clone = FindMethod(itemData.GetType(), "Clone", 0);
            if (clone != null)
            {
                try
                {
                    return clone.Invoke(itemData, null);
                }
                catch
                {
                    // Fall through to MemberwiseClone.
                }
            }

            MethodInfo memberwise = typeof(object).GetMethod("MemberwiseClone", AnyInstance);
            return memberwise?.Invoke(itemData, null);
        }

        private bool InvokeItemMethod(object target, string methodName, object item)
        {
            if (target == null || item == null)
                return false;

            MethodInfo method = FindItemMethod(target.GetType(), methodName, item.GetType());
            if (method == null)
                return false;

            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];
            args[0] = item;

            for (int i = 1; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];

                if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                }
                else if (parameter.ParameterType == typeof(bool))
                {
                    args[i] = false;
                }
                else if (parameter.ParameterType.IsValueType)
                {
                    args[i] = Activator.CreateInstance(parameter.ParameterType);
                }
                else
                {
                    args[i] = null;
                }
            }

            object result = method.Invoke(target, args);
            return !(result is bool boolResult) || boolResult;
        }

        private MethodInfo FindItemMethod(Type type, string name, Type itemType)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                foreach (MethodInfo method in current.GetMethods(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 0)
                        continue;

                    if (parameters[0].ParameterType.IsAssignableFrom(itemType))
                        return method;
                }
            }

            return null;
        }

        private void DumpJson()
        {
            if (_entries.Count == 0)
                ScanItems();

            try
            {
                string directory = Path.Combine(Paths.ConfigPath, "GearInspector");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "equipable-items.json");

                StringBuilder json = new StringBuilder();
                json.AppendLine("[");
                for (int i = 0; i < _entries.Count; i++)
                {
                    GearEntry e = _entries[i];
                    json.AppendLine("  {");
                    JsonProperty(json, "displayName", e.DisplayName, true);
                    JsonProperty(json, "prefab", e.PrefabName, true);
                    JsonProperty(json, "stableHash", e.StableHash.ToString(), true, false);
                    JsonProperty(json, "itemType", e.ItemType, true);
                    JsonProperty(json, "category", e.Category, true);
                    JsonProperty(json, "localizationToken", e.NameToken, true);
                    JsonProperty(json, "variants", e.Variants.ToString(), true, false);
                    JsonProperty(json, "hasRecipe", e.HasRecipe ? "true" : "false", true, false);
                    JsonProperty(json, "setName", e.SetName, true);
                    JsonProperty(json, "setSize", e.SetSize.ToString(), true, false);
                    JsonProperty(json, "attachOverride", e.AttachOverride, true);
                    JsonProperty(json, "equipStatusEffect", e.EquipStatusEffect, false);
                    json.Append("  }");
                    if (i + 1 < _entries.Count)
                        json.Append(',');
                    json.AppendLine();
                }
                json.AppendLine("]");

                File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
                _status = $"Wrote {path}";
                Logger.LogInfo(_status);
            }
            catch (Exception ex)
            {
                _status = $"JSON dump failed: {ex.Message}";
                Logger.LogWarning(_status);
            }
        }

        private static void JsonProperty(StringBuilder json, string name, string value, bool comma, bool quoted = true)
        {
            json.Append("    \"").Append(JsonEscape(name)).Append("\": ");
            if (quoted)
                json.Append('\"').Append(JsonEscape(value ?? string.Empty)).Append('\"');
            else
                json.Append(string.IsNullOrEmpty(value) ? "0" : value);
            if (comma)
                json.Append(',');
            json.AppendLine();
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private string BuildTechnicalBlock(GearEntry entry)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine($"Display: {entry.DisplayName}");
            text.AppendLine($"Prefab: {entry.PrefabName}");
            text.AppendLine($"Spawn: spawn {entry.PrefabName} 1");
            text.AppendLine($"Stable hash: {entry.StableHash}");
            text.AppendLine($"Token: {entry.NameToken}");
            text.AppendLine($"Type: {entry.ItemType}");
            text.AppendLine($"Category: {entry.Category}");
            text.AppendLine($"Variants: {entry.Variants}");
            text.AppendLine($"Recipe-backed: {entry.HasRecipe}");
            text.AppendLine($"Set: {entry.SetName}");
            text.AppendLine($"Set size: {entry.SetSize}");
            text.AppendLine($"Attach override: {entry.AttachOverride}");
            text.AppendLine($"Equip effect: {entry.EquipStatusEffect}");
            text.AppendLine($"Movement modifier: {entry.MovementModifier}");
            text.AppendLine($"Armor: {entry.Armor}");
            text.AppendLine($"Armor/level: {entry.ArmorPerLevel}");
            text.AppendLine($"Weight: {entry.Weight}");
            return text.ToString().TrimEnd();
        }

        private bool MatchesFilter(GearEntry entry)
        {
            if (entry == null)
                return false;

            if (!string.Equals(_category, "All", StringComparison.Ordinal) &&
                !string.Equals(entry.Category, _category, StringComparison.Ordinal))
                return false;

            string search = (_search ?? string.Empty).Trim();
            if (search.Length == 0)
                return true;

            return ContainsIgnoreCase(entry.DisplayName, search) ||
                   ContainsIgnoreCase(entry.PrefabName, search) ||
                   ContainsIgnoreCase(entry.ItemType, search) ||
                   ContainsIgnoreCase(entry.NameToken, search);
        }

        private int CountCategory(string category)
        {
            if (string.Equals(category, "All", StringComparison.Ordinal))
                return _entries.Count;

            int count = 0;
            foreach (GearEntry entry in _entries)
            {
                if (string.Equals(entry.Category, category, StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) &&
                   haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string Localize(string token, string fallback)
        {
            if (string.IsNullOrEmpty(token))
                return fallback;

            ResolveTypes();
            object localization = GetStaticSingleton(_localizationType, "instance");
            if (localization == null)
                return token;

            MethodInfo method = FindStringMethod(_localizationType, "Localize");
            if (method == null)
                return token;

            try
            {
                object localized = method.Invoke(localization, new object[] { token });
                string result = localized as string;
                return string.IsNullOrEmpty(result) ? token : result;
            }
            catch
            {
                return token;
            }
        }

        private void ResolveTypes()
        {
            if (_objectDbType == null)
                _objectDbType = FindLoadedType("ObjectDB");
            if (_itemDropType == null)
                _itemDropType = FindLoadedType("ItemDrop");
            if (_playerType == null)
                _playerType = FindLoadedType("Player");
            if (_localizationType == null)
                _localizationType = FindLoadedType("Localization");
        }

        private static object GetStaticSingleton(Type type, string memberName)
        {
            if (type == null)
                return null;

            FieldInfo field = FindField(type, memberName, true);
            if (field != null)
                return field.GetValue(null);

            PropertyInfo property = FindProperty(type, memberName, true);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(null, null);

            return null;
        }

        private static object GetMember(object target, string memberName)
        {
            if (target == null)
                return null;

            Type type = target.GetType();

            FieldInfo field = FindField(type, memberName, false);
            if (field != null)
                return field.GetValue(target);

            PropertyInfo property = FindProperty(type, memberName, false);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target, null);

            return null;
        }

        private static bool SetMember(object target, string memberName, object value)
        {
            if (target == null)
                return false;

            Type type = target.GetType();

            FieldInfo field = FindField(type, memberName, false);
            if (field != null)
            {
                field.SetValue(target, ConvertForMember(value, field.FieldType));
                return true;
            }

            PropertyInfo property = FindProperty(type, memberName, false);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(target, ConvertForMember(value, property.PropertyType), null);
                return true;
            }

            return false;
        }

        private static object ConvertForMember(object value, Type destination)
        {
            if (value == null || destination.IsInstanceOfType(value))
                return value;

            try
            {
                if (destination.IsEnum)
                    return Enum.ToObject(destination, value);
                return Convert.ChangeType(value, destination);
            }
            catch
            {
                return value;
            }
        }

        private static FieldInfo FindField(Type type, string name, bool isStatic)
        {
            BindingFlags flags = (isStatic ? AnyStatic : AnyInstance) | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type type, string name, bool isStatic)
        {
            BindingFlags flags = (isStatic ? AnyStatic : AnyInstance) | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, flags);
                if (property != null)
                    return property;
            }
            return null;
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                foreach (MethodInfo method in current.GetMethods(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    if (string.Equals(method.Name, name, StringComparison.Ordinal) &&
                        method.GetParameters().Length == parameterCount)
                        return method;
                }
            }
            return null;
        }

        private static MethodInfo FindStringMethod(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                foreach (MethodInfo method in current.GetMethods(AnyInstance | BindingFlags.DeclaredOnly))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (string.Equals(method.Name, name, StringComparison.Ordinal) &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(string))
                        return method;
                }
            }
            return null;
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, false, false);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Ignore dynamic/partially loaded assemblies.
                }
            }
            return null;
        }

        private static int ReadInt(object target, string memberName, int fallback)
        {
            object value = GetMember(target, memberName);
            if (value == null)
                return fallback;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static string ObjectName(object value)
        {
            if (value == null)
                return string.Empty;

            if (value is UnityEngine.Object unityObject)
                return unityObject != null ? unityObject.name : string.Empty;

            return ValueToString(value);
        }

        private static string ValueToString(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static string EmptyAsDash(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        private static string CategoryForType(string itemType)
        {
            switch (itemType ?? string.Empty)
            {
                case "Helmet":
                case "Chest":
                case "Legs":
                case "Hands":
                case "Shoulder":
                    return "Armor";

                case "OneHandedWeapon":
                case "TwoHandedWeapon":
                case "TwoHandedWeaponLeft":
                case "Bow":
                case "Torch":
                case "Attach_Atgeir":
                    return "Weapons";

                case "Shield":
                    return "Shields";

                case "Tool":
                    return "Tools";

                case "Utility":
                    return "Utility";

                case "Ammo":
                case "AmmoNonEquipable":
                    return "Ammo";

                default:
                    return "Other";
            }
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;

                for (int i = 0; i < text.Length && text[i] != '\0'; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ text[i];
                    if (i == text.Length - 1 || text[i + 1] == '\0')
                        break;
                    hash2 = ((hash2 << 5) + hash2) ^ text[i + 1];
                }

                return hash1 + hash2 * 1566083941;
            }
        }

        private static bool ContainsReference(List<object> list, object value)
        {
            foreach (object item in list)
            {
                if (ReferenceEquals(item, value))
                    return true;
            }
            return false;
        }

        private sealed class GearEntry
        {
            internal string DisplayName;
            internal string PrefabName;
            internal int StableHash;
            internal string NameToken;
            internal string ItemType;
            internal string Category;
            internal int Variants;
            internal string SetName;
            internal int SetSize;
            internal string AttachOverride;
            internal string EquipStatusEffect;
            internal string MovementModifier;
            internal string Armor;
            internal string ArmorPerLevel;
            internal string Weight;
            internal bool HasRecipe;
            internal object ItemData;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
