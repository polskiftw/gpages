using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

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

        private readonly Dictionary<string, Jotunn.Entities.CustomPiece> pieces =
            new Dictionary<string, Jotunn.Entities.CustomPiece>();
        private readonly Dictionary<string, Jotunn.Entities.CustomPieceTable> customTables =
            new Dictionary<string, Jotunn.Entities.CustomPieceTable>();
        private readonly Dictionary<string, PieceTable> pieceTables =
            new Dictionary<string, PieceTable>();
        private readonly Dictionary<string, Piece.PieceCategory> categories =
            new Dictionary<string, Piece.PieceCategory>(
                StringComparer.OrdinalIgnoreCase);

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
                return vanilla;
            }

            if (categories.TryGetValue(name, out var existing))
            {
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
            while (used.Contains(id) || id == 100)
            {
                id++;
            }

            var category = (Piece.PieceCategory)id;
            categories[name] = category;
            LocalizationManager.Instance.AddToken(
                "jotunn_cat_" + NormalizeToken(name),
                name,
                false);
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
                custom.Piece.m_category = AddPieceCategory(custom.Category);
            }

            if (!table.m_pieces.Contains(custom.PiecePrefab))
            {
                table.m_pieces.Add(custom.PiecePrefab);
            }
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
