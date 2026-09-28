using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Jotunn.Utils
{
    public enum CompatibilityLevel
    {
        NoNeedForSync = 0,
        OnlySyncWhenInstalled = 1,
        EveryoneMustHaveMod = 2,
        ClientMustHaveMod = 3,
        ServerMustHaveMod = 4,
        VersionCheckOnly = 5,
        NotEnforced = 6
    }

    public enum VersionStrictness
    {
        None = 0,
        Major = 1,
        Minor = 2,
        Patch = 3
    }

    public sealed class ConfigurationSynchronizationEventArgs : EventArgs
    {
        public bool InitialSynchronization { get; set; }
        public HashSet<string> UpdatedPluginGUIDs { get; set; } =
            new HashSet<string>();
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly)]
    public sealed class NetworkCompatibilityAttribute : Attribute
    {
        public CompatibilityLevel EnforceModOnClients { get; set; }
        public VersionStrictness EnforceSameVersion { get; set; }

        public NetworkCompatibilityAttribute(
            CompatibilityLevel enforceMod,
            VersionStrictness enforceVersion)
        {
            EnforceModOnClients = enforceMod;
            EnforceSameVersion = enforceVersion;
        }
    }
}

namespace Jotunn.Entities
{
    public abstract class ConsoleCommand
    {
        public abstract string Name { get; }
        public abstract string Help { get; }
        public virtual bool IsCheat => false;
        public virtual bool IsNetwork => false;
        public virtual bool OnlyServer => false;
        public virtual bool IsSecret => false;

        public virtual void Run(string[] args) { }

        public virtual void Run(string[] args, Terminal context)
        {
            Run(args);
        }

        public virtual List<string> CommandOptionList()
        {
            return null;
        }

        public override string ToString()
        {
            return (Name ?? string.Empty).ToLowerInvariant();
        }
    }
}

namespace Jotunn.Managers
{
    public sealed class SynchronizationManager
    {
        private static SynchronizationManager instance;
        public static SynchronizationManager Instance =>
            instance ??= new SynchronizationManager();

        private SynchronizationManager() { }

        public static event EventHandler<Jotunn.Utils.ConfigurationSynchronizationEventArgs>
            OnConfigurationSynchronized;

        public bool PlayerIsAdmin =>
            ZNet.instance == null || ZNet.instance.IsServer();

        internal void InvokeConfigurationSynchronized(bool initial)
        {
            OnConfigurationSynchronized?.Invoke(
                this,
                new Jotunn.Utils.ConfigurationSynchronizationEventArgs
                {
                    InitialSynchronization = initial
                });
        }
    }

    public sealed class PieceManager
    {
        private static PieceManager instance;
        public static PieceManager Instance =>
            instance ??= new PieceManager();

        private sealed class CustomUsageTag
        {
            internal string Name { get; }
            internal Piece.PieceCategory Category { get; }

            internal CustomUsageTag(
                string name,
                Piece.PieceCategory category)
            {
                Name = name;
                Category = category;
            }
        }

        private readonly Dictionary<string, Jotunn.Entities.CustomPiece> pieces =
            new Dictionary<string, Jotunn.Entities.CustomPiece>();
        private readonly Dictionary<string, Jotunn.Entities.CustomPieceTable> customTables =
            new Dictionary<string, Jotunn.Entities.CustomPieceTable>();
        private readonly Dictionary<string, PieceTable> pieceTables =
            new Dictionary<string, PieceTable>();
        private readonly Dictionary<string, Piece.PieceCategory> categories =
            new Dictionary<string, Piece.PieceCategory>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            ByUsagePieceList,
            Dictionary<int, CustomUsageTag>> customAvailableTags =
                new Dictionary<
                    ByUsagePieceList,
                    Dictionary<int, CustomUsageTag>>();
        private readonly Dictionary<Piece.PieceCategory, string> vanillaLabels =
            new Dictionary<Piece.PieceCategory, string>();
        private bool categoryRefreshNeeded = true;

        private PieceManager() { }

        public bool AddPiece(Jotunn.Entities.CustomPiece customPiece)
        {
            if (customPiece == null || !customPiece.IsValid())
            {
                return false;
            }

            var name = customPiece.PiecePrefab.name;
            if (pieces.ContainsKey(name))
            {
                return false;
            }

            PrefabManager.Instance.AddPrefab(customPiece.PiecePrefab);
            if (customPiece.PiecePrefab.layer == 0)
            {
                customPiece.PiecePrefab.layer = LayerMask.NameToLayer("piece");
            }

            pieces.Add(name, customPiece);
            TryRegisterPiece(customPiece);
            return true;
        }

        public bool AddPieceTable(Jotunn.Entities.CustomPieceTable customPieceTable)
        {
            if (customPieceTable == null || !customPieceTable.IsValid())
            {
                return false;
            }

            var name = customPieceTable.PieceTablePrefab.name;
            if (customTables.ContainsKey(name))
            {
                return false;
            }

            PrefabManager.Instance.AddPrefab(customPieceTable.PieceTablePrefab);
            customTables.Add(name, customPieceTable);
            pieceTables[name] = customPieceTable.PieceTable;

            foreach (var category in customPieceTable.Categories ??
                Array.Empty<string>())
            {
                AddPieceCategory(category);
            }

            return true;
        }

        public Jotunn.Entities.CustomPiece GetPiece(string pieceName)
        {
            return pieceName != null &&
                pieces.TryGetValue(pieceName, out var piece)
                ? piece
                : null;
        }

        public PieceTable GetPieceTable(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (pieceTables.TryGetValue(name, out var table))
            {
                return table;
            }

            if (customTables.TryGetValue(name, out var custom))
            {
                return custom.PieceTable;
            }

            var prefab = PrefabManager.Instance.GetPrefab(name);
            var direct = prefab ? prefab.GetComponent<PieceTable>() : null;
            if (direct)
            {
                pieceTables[name] = direct;
                return direct;
            }

            var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
            var viaItem = drop?.m_itemData?.m_shared?.m_buildPieces;
            if (viaItem)
            {
                pieceTables[name] = viaItem;
                pieceTables[viaItem.name] = viaItem;
                return viaItem;
            }

            return null;
        }

        public List<PieceTable> GetPieceTables()
        {
            RefreshPieceTables(ObjectDB.instance);
            return pieceTables.Values.Distinct().ToList();
        }

        public Piece.PieceCategory AddPieceCategory(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return default;
            }

            if (Enum.TryParse(name, true, out Piece.PieceCategory vanilla))
            {
                categoryRefreshNeeded = true;
                return vanilla;
            }

            if (categories.TryGetValue(name, out var existing))
            {
                categoryRefreshNeeded = true;
                return existing;
            }

            var used = new HashSet<int>(
                Enum.GetValues(typeof(Piece.PieceCategory))
                    .Cast<Piece.PieceCategory>()
                    .Select(value => (int)value));
            foreach (var value in categories.Values)
            {
                used.Add((int)value);
            }

            var id = 0;
            var all = (int)Piece.PieceCategory.All;
            while (used.Contains(id) || id == all)
            {
                id++;
            }

            var category = (Piece.PieceCategory)id;
            categories[name] = category;
            LocalizationManager.Instance.AddToken(
                GetCategoryToken(name),
                name,
                false);

            categoryRefreshNeeded = true;
            CreateCategoryTabs();
            return category;
        }

        internal void Register(ObjectDB db)
        {
            RefreshPieceTables(db);
            foreach (var custom in pieces.Values.ToArray())
            {
                TryRegisterPiece(custom);
            }
        }

        internal static int MaxCategory()
        {
            var count =
                Enum.GetValues(typeof(Piece.PieceCategory)).Length - 1;
            return count < (int)Piece.PieceCategory.All
                ? count
                : count + 1;
        }

        internal void EnumGetValuesPatch(
            Type enumType,
            ref Array result)
        {
            if (enumType != typeof(Piece.PieceCategory) ||
                categories.Count == 0)
            {
                return;
            }

            var expanded = new Piece.PieceCategory[
                result.Length + categories.Count];
            result.CopyTo(expanded, 0);
            categories.Values.CopyTo(expanded, result.Length);
            result = expanded;
        }

        internal void EnumGetNamesPatch(
            Type enumType,
            ref string[] result)
        {
            if (enumType != typeof(Piece.PieceCategory) ||
                categories.Count == 0)
            {
                return;
            }

            var expanded = new string[
                result.Length + categories.Count];
            Array.Copy(result, expanded, result.Length);
            categories.Keys.CopyTo(expanded, result.Length);
            result = expanded;
        }

        internal void ExpandAvailablePieces(PieceTable table)
        {
            var byCategory =
                GameInternals.AvailablePiecesByCategory(table);
            if (!table || byCategory == null ||
                byCategory.Count == 0)
            {
                return;
            }

            var missing = MaxCategory() - byCategory.Count;
            for (var i = 0; i < missing; i++)
            {
                byCategory.Add(new List<Piece>());
            }
        }

        internal void AdjustPieceTable(PieceTable table)
        {
            var byCategory =
                GameInternals.AvailablePiecesByCategory(table);
            if (!table || byCategory == null)
            {
                return;
            }

            Array.Resize(
                ref table.m_selectedPiece,
                byCategory.Count);
            Array.Resize(
                ref table.m_lastSelectedPiece,
                byCategory.Count);

            ReorderAllCategoryPieces(table);
        }

        internal void RefreshCategoriesIfNeeded()
        {
            if (!categoryRefreshNeeded)
            {
                return;
            }

            categoryRefreshNeeded = false;
            RefreshCategories();
        }

        internal void RefreshCategories()
        {
            CreateCategoryTabs();

            var player = Player.m_localPlayer;
            var table = GameInternals.BuildPieces(player);
            if (!table)
            {
                return;
            }

            UpdatePieceTableCategories(
                table,
                CategoriesInPieceTable(table));

            var hud = Hud.instance;
            if (!hud ||
                hud.m_pieceCategoryTabs == null ||
                hud.m_pieceCategoryTabs.Length == 0 ||
                !hud.m_pieceCategoryRoot ||
                !hud.m_pieceSelectionWindow)
            {
                return;
            }

            var firstTab =
                hud.m_pieceCategoryTabs[0].GetComponent<RectTransform>();
            var categoryRoot =
                hud.m_pieceCategoryRoot.GetComponent<RectTransform>();
            var selectionWindow =
                hud.m_pieceSelectionWindow.GetComponent<RectTransform>();

            if (!firstTab || !categoryRoot || !selectionWindow)
            {
                return;
            }

            var horizontal =
                firstTab.parent.GetComponent<HorizontalLayoutGroup>();
            if (horizontal)
            {
                horizontal.enabled = false;
            }

            const int verticalSpacing = 1;
            var tabSize = firstTab.rect.size;
            var maxHorizontalTabs = Math.Max(
                (int)(categoryRoot.rect.width / Math.Max(tabSize.x, 1f)),
                1);

            var grid =
                firstTab.parent.GetComponent<GridLayoutGroup>();
            if (grid)
            {
                grid.constraintCount = maxHorizontalTabs;
            }

            var tabAnchorX =
                (-tabSize.x * maxHorizontalTabs) / 2f +
                tabSize.x / 2f;
            var tabAnchorY =
                (tabSize.y + verticalSpacing) *
                Mathf.Floor(
                    (float)(table.m_categories.Count - 1) /
                    maxHorizontalTabs) +
                5f;
            var tabAnchor = new Vector2(tabAnchorX, tabAnchorY);

            for (var i = 0; i < table.m_categories.Count &&
                 i < hud.m_pieceCategoryTabs.Length; i++)
            {
                var rect =
                    hud.m_pieceCategoryTabs[i]
                        .GetComponent<RectTransform>();
                if (!rect)
                {
                    continue;
                }

                var x = tabSize.x * (i % maxHorizontalTabs);
                var y = -(tabSize.y + verticalSpacing) *
                    (Mathf.Floor((float)i / maxHorizontalTabs) + 0.5f);
                rect.anchoredPosition =
                    tabAnchor + new Vector2(x, y);
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
            }

            var background =
                selectionWindow.Find("Bkg2") as RectTransform;
            if (background)
            {
                var height =
                    (tabSize.y + verticalSpacing) *
                    Mathf.Max(
                        0,
                        Mathf.FloorToInt(
                            (float)(table.m_categories.Count - 1) /
                            maxHorizontalTabs));
                background.offsetMax =
                    new Vector2(background.offsetMax.x, height);
            }

            var localize = hud.GetComponentInParent<Localize>();
            if (localize)
            {
                localize.RefreshLocalization();
            }
        }

        internal static Piece.UsageTagFlags SafeGetTagById(
            ByUsagePieceList list,
            int id)
        {
            var usageTags = GameInternals.UsageTags(list);
            if (usageTags == null ||
                id < 0 ||
                id >= usageTags.Length)
            {
                // Vanilla checks HasFlag on this value. -1 means every
                // vanilla flag matches while custom-category handling
                // supplies the real custom result in the postfix.
                return (Piece.UsageTagFlags)(-1);
            }

            return usageTags[id];
        }

        internal void UpdateCustomAvailableTags(
            ByUsagePieceList list,
            PieceTable table)
        {
            var usageTags = GameInternals.UsageTags(list);
            var availableTags = GameInternals.AvailableTags(list);
            if (list == null || !table ||
                table.m_availablePieces == null ||
                usageTags == null ||
                availableTags == null)
            {
                return;
            }

            var categoryToName =
                new Dictionary<Piece.PieceCategory, string>();
            foreach (var pair in categories)
            {
                categoryToName[pair.Value] = pair.Key;
            }

            var tagIndexByCategory =
                new Dictionary<Piece.PieceCategory, int>();
            var tags = new Dictionary<int, CustomUsageTag>();

            foreach (var piece in table.m_availablePieces)
            {
                if (!piece ||
                    tagIndexByCategory.ContainsKey(piece.m_category) ||
                    !categoryToName.TryGetValue(
                        piece.m_category,
                        out var categoryName))
                {
                    continue;
                }

                var tagIndex =
                    usageTags.Length +
                    availableTags.Count;
                tagIndexByCategory[piece.m_category] = tagIndex;
                availableTags.Add(tagIndex);
                tags[tagIndex] =
                    new CustomUsageTag(
                        categoryName,
                        piece.m_category);
            }

            customAvailableTags[list] = tags;
        }

        internal bool TryGetCustomCategoryDisplayName(
            ByUsagePieceList list,
            int index,
            out string result)
        {
            result = null;
            var availableTags = GameInternals.AvailableTags(list);
            if (list == null ||
                availableTags == null ||
                index < 0 ||
                index >= availableTags.Count ||
                !customAvailableTags.TryGetValue(list, out var tags))
            {
                return false;
            }

            var tagId = availableTags[index];
            if (!tags.TryGetValue(tagId, out var tag))
            {
                return false;
            }

            result = "$" + GetCategoryToken(tag.Name);
            return true;
        }

        internal void AddPiecesForCustomCategory(
            ByUsagePieceList list,
            int tagId,
            PieceTable table,
            IList<Piece> result)
        {
            if (list == null || !table || result == null ||
                !customAvailableTags.TryGetValue(list, out var tags) ||
                !tags.TryGetValue(tagId, out var tag))
            {
                return;
            }

            foreach (var piece in table.m_availablePieces)
            {
                if (piece &&
                    piece.m_category == tag.Category &&
                    !result.Contains(piece))
                {
                    result.Add(piece);
                }
            }
        }

        internal bool TryGetUsageTag(
            ByUsagePieceList list,
            int id,
            out Piece.UsageTagFlags result)
        {
            result = default;
            var usageTags = GameInternals.UsageTags(list);
            if (usageTags == null ||
                id < 0 ||
                id >= usageTags.Length)
            {
                result = (Piece.UsageTagFlags)(-1);
                return true;
            }

            return false;
        }

        private void RefreshPieceTables(ObjectDB db)
        {
            if (db == null || db.m_items == null)
            {
                return;
            }

            foreach (var item in db.m_items)
            {
                if (!item)
                {
                    continue;
                }

                var drop = item.GetComponent<ItemDrop>();
                var table = drop?.m_itemData?.m_shared?.m_buildPieces;
                if (!table)
                {
                    continue;
                }

                pieceTables[table.name] = table;
                pieceTables[item.name] = table;
            }

            foreach (var pair in customTables)
            {
                pieceTables[pair.Key] = pair.Value.PieceTable;
            }
        }

        private void TryRegisterPiece(Jotunn.Entities.CustomPiece custom)
        {
            if (custom == null || !custom.PiecePrefab)
            {
                return;
            }

            var table = GetPieceTable(custom.PieceTable);
            if (!table || table.m_pieces == null)
            {
                return;
            }

            if (custom.FixReference || custom.FixConfig)
            {
                custom.PiecePrefab.FixReferences(custom.FixReference);
                custom.FixReference = false;
                custom.FixConfig = false;
            }

            if (!string.IsNullOrEmpty(custom.Category) && custom.Piece)
            {
                custom.Piece.m_category =
                    AddPieceCategory(custom.Category);
            }

            if (!table.m_pieces.Contains(custom.PiecePrefab))
            {
                table.m_pieces.Add(custom.PiecePrefab);
            }
        }

        private static HashSet<Piece.PieceCategory>
            CategoriesInPieceTable(PieceTable table)
        {
            var result = new HashSet<Piece.PieceCategory>();
            if (!table || table.m_pieces == null)
            {
                return result;
            }

            foreach (var prefab in table.m_pieces)
            {
                var piece = prefab
                    ? prefab.GetComponent<Piece>()
                    : null;
                if (piece)
                {
                    result.Add(piece.m_category);
                }
            }

            return result;
        }

        private void CreateCategoryTabs()
        {
            var hud = Hud.instance;
            if (!hud ||
                hud.m_pieceCategoryTabs == null ||
                hud.m_pieceCategoryTabs.Length == 0)
            {
                return;
            }

            var maxCategory = MaxCategory();
            for (var i = hud.m_pieceCategoryTabs.Length;
                 i < maxCategory;
                 i++)
            {
                var first = hud.m_pieceCategoryTabs[0];
                if (!first || !first.transform.parent)
                {
                    return;
                }

                var tab = UnityEngine.Object.Instantiate(
                    first,
                    first.transform.parent);
                tab.SetActive(false);

                var handler =
                    tab.GetComponent<UIInputHandler>() ??
                    tab.AddComponent<UIInputHandler>();
                GameInternals.WireCategoryClick(hud, handler);

                var expanded = new GameObject[
                    hud.m_pieceCategoryTabs.Length + 1];
                Array.Copy(
                    hud.m_pieceCategoryTabs,
                    expanded,
                    hud.m_pieceCategoryTabs.Length);
                expanded[expanded.Length - 1] = tab;
                hud.m_pieceCategoryTabs = expanded;
            }

            var player = Player.m_localPlayer;
            if (player && GameInternals.BuildPieces(player))
            {
                GameInternals.UpdateAvailablePiecesList(player);
            }
        }

        private static void ReorderAllCategoryPieces(
            PieceTable table)
        {
            var byCategory =
                GameInternals.AvailablePiecesByCategory(table);
            if (!table ||
                table.m_pieces == null ||
                byCategory == null)
            {
                return;
            }

            var allPieces = table.m_pieces
                .Select(prefab =>
                    prefab ? prefab.GetComponent<Piece>() : null)
                .Where(piece =>
                    piece &&
                    piece.m_category == Piece.PieceCategory.All)
                .ToList();

            foreach (var available in byCategory)
            {
                if (available == null)
                {
                    continue;
                }

                var position = 0;
                foreach (var piece in allPieces)
                {
                    available.Remove(piece);
                    available.Insert(
                        Math.Min(position, available.Count),
                        piece);
                    position++;
                }
            }
        }

        private void UpdatePieceTableCategories(
            PieceTable table,
            HashSet<Piece.PieceCategory> visibleCategories)
        {
            if (!table ||
                visibleCategories == null ||
                table.m_categories == null ||
                table.m_categoryLabels == null)
            {
                return;
            }

            for (var i = table.m_categories.Count - 1;
                 i >= 0;
                 i--)
            {
                if (!visibleCategories.Contains(
                        table.m_categories[i]) ||
                    table.m_categories[i] ==
                        Piece.PieceCategory.All)
                {
                    table.m_categories.RemoveAt(i);
                    if (i < table.m_categoryLabels.Count)
                    {
                        table.m_categoryLabels.RemoveAt(i);
                    }
                }
            }

            foreach (var category in visibleCategories
                .Where(category =>
                    category != Piece.PieceCategory.All)
                .OrderBy(category => (int)category))
            {
                if (table.m_categories.Contains(category))
                {
                    continue;
                }

                table.m_categories.Add(category);
                table.m_categoryLabels.Add(
                    GetCategoryLabel(category));
            }
        }

        private string GetCategoryLabel(
            Piece.PieceCategory category)
        {
            foreach (var pair in categories)
            {
                if (pair.Value == category)
                {
                    return "$" + GetCategoryToken(pair.Key);
                }
            }

            if (!vanillaLabels.ContainsKey(category))
            {
                SearchVanillaLabels();
            }

            return vanillaLabels.TryGetValue(
                category,
                out var label)
                ? label
                : string.Empty;
        }

        private void SearchVanillaLabels()
        {
            foreach (var table in
                Resources.FindObjectsOfTypeAll<PieceTable>())
            {
                if (!table ||
                    table.m_categories == null ||
                    table.m_categoryLabels == null)
                {
                    continue;
                }

                for (var i = 0;
                     i < table.m_categories.Count &&
                     i < table.m_categoryLabels.Count;
                     i++)
                {
                    var category = table.m_categories[i];
                    var label = table.m_categoryLabels[i];
                    if (!vanillaLabels.ContainsKey(category) &&
                        !string.IsNullOrEmpty(label))
                    {
                        vanillaLabels[category] = label;
                    }
                }
            }
        }

        private static string GetCategoryToken(string value)
        {
            return "jotunn_cat_" + NormalizeToken(value);
        }

        private static string NormalizeToken(string value)
        {
            return new string((value ?? string.Empty)
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray());
        }
    }

    public sealed class CommandManager
    {
        private static CommandManager instance;
        public static CommandManager Instance =>
            instance ??= new CommandManager();

        private readonly List<Jotunn.Entities.ConsoleCommand> commands =
            new List<Jotunn.Entities.ConsoleCommand>();

        private CommandManager() { }

        public void AddConsoleCommand(Jotunn.Entities.ConsoleCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Name))
            {
                return;
            }

            if (commands.Any(existing =>
                string.Equals(existing.Name, command.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Logger.LogWarning("Cannot register duplicate console command: " + command.Name);
                return;
            }

            commands.Add(command);
        }

        internal void RegisterAll()
        {
            foreach (var command in commands)
            {
                Register(command);
            }
        }

        private static void Register(Jotunn.Entities.ConsoleCommand command)
        {
            var commandMap = AccessTools.Field(typeof(Terminal), "commands")?.GetValue(null)
                as System.Collections.IDictionary;
            if (commandMap != null && commandMap.Contains(command.Name))
            {
                return;
            }

            new Terminal.ConsoleCommand(
                command.Name,
                command.Help ?? string.Empty,
                args => command.Run(args.Args.Skip(1).ToArray(), args.Context),
                command.IsCheat,
                command.IsNetwork,
                command.OnlyServer,
                command.IsSecret,
                false,
                false,
                command.CommandOptionList,
                false,
                false,
                false);
        }
    }
}
