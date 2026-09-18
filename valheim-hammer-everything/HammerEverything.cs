using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HammerEverythingMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class HammerEverything : BaseUnityPlugin
    {
        public const string PluginGuid = "claire.valheim.hammereverything";
        public const string PluginName = "Hammer Everything";
        public const string PluginVersion = "1.0.0";

        private static readonly BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] PropHints =
        {
            "barrel", "barrell", "crate", "box", "chest",
            "chair", "bench", "stool", "table", "bed", "throne",
            "banner", "curtain", "drape", "tapestry", "rug", "carpet",
            "statue", "lantern", "brazier", "torch", "candle",
            "cage", "shelf", "rack", "hook", "chain", "pot", "bucket",
            "basket", "sack", "pile", "stack", "armorstand", "armourstand",
            "sign", "tent", "stake", "skull", "bone", "cloth", "decor",
            "furniture", "props_"
        };

        private static readonly string[] StructureHints =
        {
            "wall", "floor", "roof", "beam", "pole", "stair", "ladder",
            "door", "gate", "arch", "column", "pillar", "window",
            "bridge", "fence", "railing", "platform"
        };

        private static readonly string[] UnsafeComponentTypeNames =
        {
            "Projectile",
            "Humanoid",
            "Character",
            "BaseAI",
            "AnimalAI",
            "MonsterAI",
            "CreatureSpawner",
            "SpawnArea",
            "TriggerSpawner",
            "Fish",
            "RandomFlyingBird",
            "MusicLocation",
            "Aoe",
            "ItemDrop",
            "DungeonGenerator",
            "TerrainModifier",
            "TerrainComp",
            "EventZone",
            "LocationProxy",
            "LootSpawner",
            "Mister",
            "Ragdoll",
            "MineRock",
            "MineRock5",
            "TombStone",
            "LiquidVolume",
            "Gibber",
            "ShipConstructor",
            "TeleportAbility",
            "Trader",
            "CamShaker",
            "TreeBase",
            "TreeLog",
            "Pickable",
            "Plant",
            "Ship",
            "Vagon",
            "PrivateArea"
        };

        private static readonly HashSet<string> HardBlockedNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Player",
                "Valkyrie",
                "CargoCrate",
                "Ravens",
                "odin",
                "Flies",
                "PlaceMarker",
                "TERRAIN_TEST",
                "guard_stone_test",
                "demister_ball",
                "FishingRodFloat",
                "Pickable_Item",
                "Pickable_RandomFood"
            };

        private static readonly Dictionary<string, string> FriendlyNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "barrell", "Barrel" },
                { "dvergrprops_barrel", "Dvergr Barrel" },
                { "dvergrprops_crate", "Dvergr Crate" },
                { "dvergrprops_crate_long", "Dvergr Component Crate" },
                { "dvergrprops_banner", "Dvergr Banner" },
                { "dvergrprops_bed", "Dvergr Bed" },
                { "dvergrprops_chair", "Dvergr Chair" },
                { "dvergrprops_curtain", "Dvergr Curtain" },
                { "dvergrprops_hooknchain", "Dvergr Hook & Chain" },
                { "dvergrprops_lantern", "Dvergr Lantern" }
            };

        private readonly Stopwatch _pollTimer = Stopwatch.StartNew();
        private readonly Stopwatch _removalTimer = Stopwatch.StartNew();

        private readonly HashSet<string> _managedPrefabNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pieceAddedByUs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _managedPrefabObjects =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _automaticPropScan;
        private ConfigEntry<bool> _includeStructures;
        private ConfigEntry<bool> _alwaysAvailable;
        private ConfigEntry<bool> _allowDungeonPlacement;
        private ConfigEntry<bool> _allowOverlap;
        private ConfigEntry<string> _extraPrefabNames;
        private ConfigEntry<string> _blockedPrefabNames;
        private ConfigEntry<bool> _verboseLogging;

        private Type _zNetSceneType;
        private Type _objectDbType;
        private Type _itemDropType;
        private Type _pieceType;
        private Type _zNetViewType;
        private Type _playerType;
        private Type _resourcesType;

        private object _lastScene;
        private object _lastHammerPieceTable;
        private bool _playerRefreshed;

        private void Awake()
        {
            _enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enable Hammer Everything.");

            _automaticPropScan = Config.Bind(
                "General",
                "AutomaticPropScan",
                true,
                "Automatically add safe vanilla prop prefabs that are not normally in the Hammer.");

            _includeStructures = Config.Bind(
                "General",
                "IncludeHiddenStructures",
                true,
                "Also include safe hidden wall, floor, roof, door, gate, beam, stair, and similar vanilla prefabs.");

            _alwaysAvailable = Config.Bind(
                "General",
                "AlwaysAvailable",
                true,
                "Hidden pieces added by this mod have no material or crafting-station requirement.");

            _allowDungeonPlacement = Config.Bind(
                "Placement",
                "AllowInDungeons",
                true,
                "Allow the added hidden pieces to be placed in dungeons/interiors.");

            _allowOverlap = Config.Bind(
                "Placement",
                "AllowOverlap",
                true,
                "Relax normal overlap/ground clipping restrictions for hidden props.");

            _extraPrefabNames = Config.Bind(
                "Advanced",
                "ExtraPrefabNames",
                "",
                "Comma-separated exact vanilla prefab names to add even if their names do not look like props. Unsafe entity/effect prefabs are still rejected.");

            _blockedPrefabNames = Config.Bind(
                "Advanced",
                "BlockedPrefabNames",
                "CargoCrate",
                "Comma-separated prefab names to exclude. CargoCrate is blocked by default because vanilla deletes an empty CargoCrate.");

            _verboseLogging = Config.Bind(
                "Advanced",
                "VerboseLogging",
                false,
                "Log each prefab added to the Hammer.");

            _enabled.SettingChanged += OnConfigChanged;
            _automaticPropScan.SettingChanged += OnConfigChanged;
            _includeStructures.SettingChanged += OnConfigChanged;
            _alwaysAvailable.SettingChanged += OnConfigChanged;
            _allowDungeonPlacement.SettingChanged += OnConfigChanged;
            _allowOverlap.SettingChanged += OnConfigChanged;
            _extraPrefabNames.SettingChanged += OnConfigChanged;
            _blockedPrefabNames.SettingChanged += OnConfigChanged;

            ResolveTypes();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Waiting for Valheim's prefab database.");
        }

        private void Update()
        {
            if (_pollTimer.ElapsedMilliseconds >= 1000)
            {
                _pollTimer.Restart();
                EnsureRegistered();
                TryRefreshLocalPlayer();
            }

            if (_removalTimer.ElapsedMilliseconds >= 2000)
            {
                _removalTimer.Restart();
                UpdateRemovalStateForPlacedProps();
            }
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            _lastScene = null;
            _lastHammerPieceTable = null;
            _playerRefreshed = false;
        }

        private void EnsureRegistered()
        {
            if (_enabled == null || !_enabled.Value)
                return;

            ResolveTypes();

            if (_zNetSceneType == null || _objectDbType == null ||
                _itemDropType == null || _pieceType == null)
                return;

            object scene = GetStaticSingleton(_zNetSceneType, "instance");
            if (scene == null)
                return;

            object hammerPieceTable = TryGetHammerPieceTable();
            if (hammerPieceTable == null)
                return;

            if (ReferenceEquals(scene, _lastScene) &&
                ReferenceEquals(hammerPieceTable, _lastHammerPieceTable))
                return;

            _lastScene = scene;
            _lastHammerPieceTable = hammerPieceTable;
            _playerRefreshed = false;

            RegisterPieces(scene, hammerPieceTable);
        }

        private void RegisterPieces(object scene, object hammerPieceTable)
        {
            IList hammerPieces = GetFieldValue(hammerPieceTable, "m_pieces") as IList;
            if (hammerPieces == null)
            {
                Logger.LogWarning("Could not find Hammer PieceTable.m_pieces.");
                return;
            }

            HashSet<string> existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object entry in hammerPieces)
            {
                string name = GetUnityName(entry);
                if (!string.IsNullOrEmpty(name))
                    existingNames.Add(NormalizeInstanceName(name));
            }

            object templatePrefab = TryGetScenePrefab(scene, "piece_chest_wood");
            object templatePiece = templatePrefab == null ? null : GetComponent(templatePrefab, _pieceType);

            if (templatePiece == null)
            {
                foreach (object entry in hammerPieces)
                {
                    object candidatePiece = GetComponent(entry, _pieceType);
                    if (candidatePiece != null)
                    {
                        templatePiece = candidatePiece;
                        break;
                    }
                }
            }

            int added = 0;
            HashSet<string> blocked = ParseNameSet(_blockedPrefabNames.Value);
            HashSet<string> explicitNames = ParseNameSet(_extraPrefabNames.Value);

            // Always prioritize the prop families Claire asked for.
            explicitNames.Add("barrell");
            explicitNames.Add("dvergrprops_barrel");
            explicitNames.Add("dvergrprops_crate");
            explicitNames.Add("dvergrprops_crate_long");

            foreach (string explicitName in explicitNames)
            {
                object prefab = TryGetScenePrefab(scene, explicitName);
                if (prefab == null)
                {
                    if (_verboseLogging.Value)
                        Logger.LogWarning($"Prefab not found: {explicitName}");
                    continue;
                }

                if (TryAddPrefab(prefab, hammerPieces, existingNames, templatePiece, blocked, forceNameMatch: true))
                    added++;
            }

            if (_automaticPropScan.Value)
            {
                object rawPrefabs = GetFieldValue(scene, "m_prefabs");
                if (rawPrefabs is IEnumerable prefabs)
                {
                    foreach (object prefab in prefabs)
                    {
                        if (TryAddPrefab(prefab, hammerPieces, existingNames, templatePiece, blocked, forceNameMatch: false))
                            added++;
                    }
                }
            }

            Logger.LogInfo(
                $"Hammer Everything registered {added} hidden vanilla prefab(s). " +
                $"Hammer now has {hammerPieces.Count} piece entries.");
        }

        private bool TryAddPrefab(
            object prefab,
            IList hammerPieces,
            HashSet<string> existingNames,
            object templatePiece,
            HashSet<string> blocked,
            bool forceNameMatch)
        {
            if (prefab == null)
                return false;

            string name = NormalizeInstanceName(GetUnityName(prefab));
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (existingNames.Contains(name))
                return false;

            if (blocked.Contains(name) || HardBlockedNames.Contains(name))
                return false;

            if (ShouldIgnoreByName(name))
                return false;

            if (!forceNameMatch && !LooksLikeBuildProp(name))
                return false;

            if (!IsSafeNetworkPrefab(prefab))
                return false;

            object piece = GetComponent(prefab, _pieceType);
            bool addedComponent = false;

            if (piece == null)
            {
                piece = AddComponent(prefab, _pieceType);
                if (piece == null)
                    return false;

                addedComponent = true;
                _pieceAddedByUs.Add(name);
            }

            ConfigurePiece(piece, templatePiece, name, addedComponent);

            hammerPieces.Add(prefab);
            existingNames.Add(name);
            _managedPrefabNames.Add(name);
            _managedPrefabObjects[name] = prefab;

            if (_verboseLogging.Value)
                Logger.LogInfo($"Added Hammer prefab: {name}");

            return true;
        }

        private bool LooksLikeBuildProp(string name)
        {
            string lower = name.ToLowerInvariant();

            if (lower.StartsWith("dvergrprops_") ||
                lower.StartsWith("goblinprops_") ||
                lower.StartsWith("castlekit_"))
                return true;

            foreach (string hint in PropHints)
            {
                if (lower.Contains(hint))
                    return true;
            }

            if (_includeStructures.Value)
            {
                foreach (string hint in StructureHints)
                {
                    if (lower.Contains(hint))
                        return true;
                }
            }

            return false;
        }

        private static bool ShouldIgnoreByName(string name)
        {
            string lower = name.ToLowerInvariant();

            if (lower.StartsWith("_") ||
                lower.StartsWith("old_") ||
                lower.StartsWith("vfx_") ||
                lower.StartsWith("sfx_") ||
                lower.StartsWith("fx_") ||
                lower.EndsWith("_old") ||
                lower.EndsWith("_test") ||
                lower.Contains("random") ||
                lower.Contains("projectile") ||
                lower.Contains("aoe") ||
                lower.Contains("spawner"))
                return true;

            if (lower.StartsWith("treasurechest_") ||
                lower.StartsWith("loot_chest_"))
                return true;

            return false;
        }

        private bool IsSafeNetworkPrefab(object prefab)
        {
            if (_zNetViewType != null && GetComponent(prefab, _zNetViewType) == null)
                return false;

            foreach (string typeName in UnsafeComponentTypeNames)
            {
                Type unsafeType = FindLoadedType(typeName);
                if (unsafeType != null && GetComponent(prefab, unsafeType) != null)
                    return false;
            }

            return true;
        }

        private void ConfigurePiece(object piece, object templatePiece, string prefabName, bool newlyAddedPiece)
        {
            SetPropertyIfExists(piece, "enabled", true);
            SetFieldIfExists(piece, "m_enabled", true);

            if (newlyAddedPiece)
            {
                SetFieldIfExists(piece, "m_name", GetFriendlyName(prefabName));
                SetFieldIfExists(piece, "m_description", $"Vanilla prefab: {prefabName}");

                SetFieldIfExists(piece, "m_groundOnly", false);
                SetFieldIfExists(piece, "m_groundPiece", false);
                SetFieldIfExists(piece, "m_cultivatedGroundOnly", false);
                SetFieldIfExists(piece, "m_waterPiece", false);
                SetFieldIfExists(piece, "m_noInWater", false);
                SetFieldIfExists(piece, "m_notOnWood", false);
                SetFieldIfExists(piece, "m_notOnTiltingSurface", false);
                SetFieldIfExists(piece, "m_inCeilingOnly", false);
                SetFieldIfExists(piece, "m_notOnFloor", false);
                SetFieldIfExists(piece, "m_onlyInTeleportArea", false);
                SetFieldIfExists(piece, "m_repairPiece", false);
                SetFieldIfExists(piece, "m_randomTarget", false);
                SetFieldIfExists(piece, "m_targetNonPlayerBuilt", false);

                // Keep naturally spawned copies safe from accidental Hammer deconstruction.
                // Placed copies are made removable after they receive a non-zero creator ID.
                SetFieldIfExists(piece, "m_canBeRemoved", false);

                SetEnumFieldToZero(piece, "m_onlyInBiome");
            }

            SetFieldIfExists(piece, "m_allowedInDungeons", _allowDungeonPlacement.Value);
            SetFieldIfExists(piece, "m_allowRotatedOverlap", _allowOverlap.Value);

            if (_allowOverlap.Value)
            {
                SetFieldIfExists(piece, "m_clipEverything", true);
                SetFieldIfExists(piece, "m_clipGround", true);
            }

            if (templatePiece != null)
            {
                CopyFieldIfTargetEmpty(templatePiece, piece, "m_icon");
                CopyFieldIfTargetZero(templatePiece, piece, "m_category");
                CopyFieldIfTargetZero(templatePiece, piece, "m_usage");
            }

            if (_alwaysAvailable.Value)
            {
                ClearArrayField(piece, "m_resources");
                SetFieldIfExists(piece, "m_craftingStation", null);
                SetFieldIfExists(piece, "m_requiredGlobalKey", "");
                ClearStringCollectionField(piece, "m_requiredGlobalKeys");
            }
        }

        private object TryGetHammerPieceTable()
        {
            object objectDb = GetStaticSingleton(_objectDbType, "instance");
            if (objectDb == null)
                return null;

            MethodInfo getItemPrefab = _objectDbType.GetMethod(
                "GetItemPrefab",
                AnyInstance,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            object hammerPrefab = getItemPrefab?.Invoke(objectDb, new object[] { "Hammer" });
            if (hammerPrefab == null)
                return null;

            object itemDrop = GetComponent(hammerPrefab, _itemDropType);
            object itemData = GetFieldValue(itemDrop, "m_itemData");
            object shared = GetFieldValue(itemData, "m_shared");
            return GetFieldValue(shared, "m_buildPieces");
        }

        private object TryGetScenePrefab(object scene, string prefabName)
        {
            if (scene == null || string.IsNullOrWhiteSpace(prefabName))
                return null;

            MethodInfo getPrefab = scene.GetType().GetMethod(
                "GetPrefab",
                AnyInstance,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            try
            {
                return getPrefab?.Invoke(scene, new object[] { prefabName });
            }
            catch
            {
                return null;
            }
        }

        private void TryRefreshLocalPlayer()
        {
            if (_playerRefreshed || _lastHammerPieceTable == null)
                return;

            ResolveTypes();
            if (_playerType == null)
                return;

            object player = GetStaticSingleton(_playerType, "m_localPlayer");
            if (player == null)
                return;

            TryInvokeNoArgs(player, "UpdateKnownRecipesList");
            TryInvokeNoArgs(player, "UpdateAvailablePiecesList");
            _playerRefreshed = true;
        }

        private void UpdateRemovalStateForPlacedProps()
        {
            if (_pieceAddedByUs.Count == 0 || _pieceType == null)
                return;

            ResolveTypes();
            if (_resourcesType == null)
                return;

            MethodInfo findAll = _resourcesType.GetMethod(
                "FindObjectsOfTypeAll",
                AnyStatic,
                binder: null,
                types: new[] { typeof(Type) },
                modifiers: null);

            if (findAll == null)
                return;

            try
            {
                if (!(findAll.Invoke(null, new object[] { _pieceType }) is IEnumerable pieces))
                    return;

                foreach (object piece in pieces)
                {
                    if (piece == null)
                        continue;

                    object gameObject = GetPropertyValue(piece, "gameObject");
                    string name = NormalizeInstanceName(GetUnityName(gameObject));

                    if (!_pieceAddedByUs.Contains(name))
                        continue;

                    if (_managedPrefabObjects.TryGetValue(name, out object prefab) &&
                        ReferenceEquals(prefab, gameObject))
                    {
                        SetFieldIfExists(piece, "m_canBeRemoved", false);
                        continue;
                    }

                    MethodInfo getCreator = piece.GetType().GetMethod(
                        "GetCreator",
                        AnyInstance,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null);

                    if (getCreator == null)
                        continue;

                    object creatorValue = getCreator.Invoke(piece, null);
                    long creator = creatorValue == null ? 0L : Convert.ToInt64(creatorValue, CultureInfo.InvariantCulture);
                    SetFieldIfExists(piece, "m_canBeRemoved", creator != 0L);
                }
            }
            catch (Exception ex)
            {
                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogDebug($"Placed-prop removal scan skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void ResolveTypes()
        {
            if (_zNetSceneType == null)
                _zNetSceneType = FindLoadedType("ZNetScene");
            if (_objectDbType == null)
                _objectDbType = FindLoadedType("ObjectDB");
            if (_itemDropType == null)
                _itemDropType = FindLoadedType("ItemDrop");
            if (_pieceType == null)
                _pieceType = FindLoadedType("Piece");
            if (_zNetViewType == null)
                _zNetViewType = FindLoadedType("ZNetView");
            if (_playerType == null)
                _playerType = FindLoadedType("Player");
            if (_resourcesType == null)
                _resourcesType = FindLoadedType("UnityEngine.Resources");
        }

        private static object GetStaticSingleton(Type type, string memberName)
        {
            if (type == null)
                return null;

            FieldInfo field = type.GetField(memberName, AnyStatic);
            if (field != null)
                return field.GetValue(null);

            PropertyInfo property = type.GetProperty(memberName, AnyStatic);
            return property?.GetValue(null, null);
        }

        private static object GetComponent(object gameObject, Type componentType)
        {
            if (gameObject == null || componentType == null)
                return null;

            MethodInfo method = gameObject.GetType().GetMethod(
                "GetComponent",
                AnyInstance,
                binder: null,
                types: new[] { typeof(Type) },
                modifiers: null);

            return method?.Invoke(gameObject, new object[] { componentType });
        }

        private static object AddComponent(object gameObject, Type componentType)
        {
            if (gameObject == null || componentType == null)
                return null;

            MethodInfo method = gameObject.GetType().GetMethod(
                "AddComponent",
                AnyInstance,
                binder: null,
                types: new[] { typeof(Type) },
                modifiers: null);

            return method?.Invoke(gameObject, new object[] { componentType });
        }

        private static object GetFieldValue(object instance, string fieldName)
        {
            if (instance == null)
                return null;

            FieldInfo field = instance.GetType().GetField(fieldName, AnyInstance);
            return field?.GetValue(instance);
        }

        private static object GetPropertyValue(object instance, string propertyName)
        {
            if (instance == null)
                return null;

            PropertyInfo property = instance.GetType().GetProperty(propertyName, AnyInstance);
            return property?.GetValue(instance, null);
        }

        private static void SetPropertyIfExists(object instance, string propertyName, object value)
        {
            if (instance == null)
                return;

            PropertyInfo property = instance.GetType().GetProperty(propertyName, AnyInstance);
            if (property != null && property.CanWrite)
                property.SetValue(instance, value, null);
        }

        private static void SetFieldIfExists(object instance, string fieldName, object value)
        {
            if (instance == null)
                return;

            FieldInfo field = instance.GetType().GetField(fieldName, AnyInstance);
            if (field == null)
                return;

            if (value == null)
            {
                if (!field.FieldType.IsValueType || Nullable.GetUnderlyingType(field.FieldType) != null)
                    field.SetValue(instance, null);
                return;
            }

            if (field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(instance, value);
                return;
            }

            try
            {
                object converted = Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture);
                field.SetValue(instance, converted);
            }
            catch
            {
                // Field changed type between Valheim versions. Ignore it instead of breaking startup.
            }
        }

        private static void SetEnumFieldToZero(object instance, string fieldName)
        {
            if (instance == null)
                return;

            FieldInfo field = instance.GetType().GetField(fieldName, AnyInstance);
            if (field != null && field.FieldType.IsEnum)
                field.SetValue(instance, Enum.ToObject(field.FieldType, 0));
        }

        private static void CopyFieldIfTargetEmpty(object source, object target, string fieldName)
        {
            if (source == null || target == null)
                return;

            FieldInfo sourceField = source.GetType().GetField(fieldName, AnyInstance);
            FieldInfo targetField = target.GetType().GetField(fieldName, AnyInstance);
            if (sourceField == null || targetField == null)
                return;

            object current = targetField.GetValue(target);
            if (current == null)
                targetField.SetValue(target, sourceField.GetValue(source));
        }

        private static void CopyFieldIfTargetZero(object source, object target, string fieldName)
        {
            if (source == null || target == null)
                return;

            FieldInfo sourceField = source.GetType().GetField(fieldName, AnyInstance);
            FieldInfo targetField = target.GetType().GetField(fieldName, AnyInstance);
            if (sourceField == null || targetField == null)
                return;

            object current = targetField.GetValue(target);
            if (IsZeroValue(current))
                targetField.SetValue(target, sourceField.GetValue(source));
        }

        private static bool IsZeroValue(object value)
        {
            if (value == null)
                return true;

            Type type = value.GetType();
            if (type.IsEnum)
                return Convert.ToInt64(value, CultureInfo.InvariantCulture) == 0L;

            if (type.IsValueType)
                return value.Equals(Activator.CreateInstance(type));

            return false;
        }

        private static void ClearArrayField(object instance, string fieldName)
        {
            if (instance == null)
                return;

            FieldInfo field = instance.GetType().GetField(fieldName, AnyInstance);
            if (field == null || !field.FieldType.IsArray)
                return;

            Type elementType = field.FieldType.GetElementType();
            if (elementType != null)
                field.SetValue(instance, Array.CreateInstance(elementType, 0));
        }

        private static void ClearStringCollectionField(object instance, string fieldName)
        {
            if (instance == null)
                return;

            FieldInfo field = instance.GetType().GetField(fieldName, AnyInstance);
            if (field == null)
                return;

            if (field.FieldType == typeof(string[]))
            {
                field.SetValue(instance, new string[0]);
                return;
            }

            if (typeof(IList).IsAssignableFrom(field.FieldType))
            {
                object value = field.GetValue(instance);
                if (value is IList list)
                    list.Clear();
            }
        }

        private static string GetUnityName(object unityObject)
        {
            if (unityObject == null)
                return null;

            PropertyInfo property = unityObject.GetType().GetProperty("name", AnyInstance);
            object value = property?.GetValue(unityObject, null);
            return value as string;
        }

        private static string NormalizeInstanceName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            const string cloneSuffix = "(Clone)";
            return name.EndsWith(cloneSuffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - cloneSuffix.Length)
                : name;
        }

        private static HashSet<string> ParseNameSet(string raw)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(raw))
                return result;

            string[] parts = raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string value = part.Trim();
                if (!string.IsNullOrEmpty(value))
                    result.Add(value);
            }

            return result;
        }

        private static string GetFriendlyName(string prefabName)
        {
            if (FriendlyNames.TryGetValue(prefabName, out string friendly))
                return friendly;

            string value = prefabName ?? "Vanilla Prop";
            value = Regex.Replace(value, "^dvergrprops_", "Dvergr ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "^goblinprops_", "Fuling ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "^castlekit_", "Castle ", RegexOptions.IgnoreCase);
            value = value.Replace('_', ' ').Replace('-', ' ');
            value = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2");
            value = Regex.Replace(value, "\\s+", " ").Trim();

            if (value.Length == 0)
                return "Vanilla Prop";

            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
        }

        private static void TryInvokeNoArgs(object instance, string methodName)
        {
            if (instance == null)
                return;

            try
            {
                MethodInfo method = instance.GetType().GetMethod(
                    methodName,
                    AnyInstance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);

                method?.Invoke(instance, null);
            }
            catch
            {
                // These refresh methods have changed across Valheim versions.
                // The normal Hammer refresh path will still rebuild the list.
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
    }
}
