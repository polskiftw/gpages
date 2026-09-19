using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

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

        public bool PlayerIsAdmin =>
            ZNet.instance == null || ZNet.instance.IsServer();
    }

    public sealed class PieceManager
    {
        private static PieceManager instance;
        public static PieceManager Instance =>
            instance ??= new PieceManager();

        private PieceManager() { }
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
