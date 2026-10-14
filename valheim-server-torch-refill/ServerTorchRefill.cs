using BepInEx;
using BepInEx.Configuration;
using ServersideQoL;
using ServersideQoL.Processors;
using ServersideQoL.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace ServerTorchRefillMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class ServerTorchRefillPlugin : ServersideQoLPluginBase<ServerTorchRefillPlugin, ModConfig>
    {
        public const string PluginGuid = "claire.valheim.servertorchrefill";
        public const string PluginName = "Server Torch Refill";
        public const string PluginVersion = "1.2.0";

        protected override ModConfig CreateConfigSingleton(ConfigFile configFile, ServersideQoL.Logger logger)
            => new ModConfig(configFile, logger);

        protected override void RegisterProcessors(IProcessorCollection processors)
            => processors.Add<TorchRefillProcessor>();
    }

    public sealed class ModConfig : ConfigBase<ModConfig>
    {
        public override ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<float> Range { get; }
        public ConfigEntry<bool> RefillWheneverMissing { get; }
        public ConfigEntry<float> RefillBelowPercent { get; }
        public ConfigEntry<float> RefillToPercent { get; }
        public ConfigEntry<int> LeaveFuelInContainer { get; }
        public ConfigEntry<float> CheckIntervalSeconds { get; }
        public ConfigEntry<bool> IncludeAllFireplaces { get; }
        public ConfigEntry<string> LightingNameTokens { get; }
        public ConfigEntry<bool> LogRefills { get; }
        public ConfigEntry<bool> DiagnosticLogs { get; }
        public ConfigEntry<bool> AnnounceInGame { get; }

        public ModConfig(ConfigFile cfg, ServersideQoL.Logger logger) : base(cfg, logger)
        {
            Enabled = BindEx(cfg, true,
                "Enable server-side refilling.");

            Range = BindEx(cfg, 10f,
                "Maximum distance in metres from a fireplace to a source chest. 0 disables refilling.");

            // A NEW setting, deliberately default true even for existing v1.1 config files.
            // This avoids carrying forward the old 25% default that makes fresh torches seem broken.
            RefillWheneverMissing = BindEx(cfg, true,
                "Refill any eligible torch with room for at least one fuel item. This overrides RefillBelowPercent unless set false.");

            RefillBelowPercent = BindEx(cfg, 0.25f,
                "Legacy low-fuel trigger (0..1), used only when RefillWheneverMissing is false.");

            RefillToPercent = BindEx(cfg, 1f,
                "Refill to this fraction of capacity (0..1).");

            LeaveFuelInContainer = BindEx(cfg, 0,
                "How many matching items to leave in each source chest.");

            CheckIntervalSeconds = BindEx(cfg, 30f,
                "Minimum retry period in seconds when a torch needs fuel. Full torches are checked much less often, and chest ownership retries are never fast-loops.");

            IncludeAllFireplaces = BindEx(cfg, false,
                "If true, also refill campfires, hearths, and other refillable fireplaces.");

            LightingNameTokens = BindEx(cfg,
                "torch,sconce,brazier,candle,jackoturnip,lantern",
                "Comma-separated prefab/name fragments used to identify lighting. Actual fuel type comes from the Fireplace component.");

            LogRefills = BindEx(cfg, true,
                "Log successful fuel transfers in server console.");

            // Preserved for old config compatibility, but skipped-refill reasons are
            // ALWAYS logged (bounded), regardless of the value of DiagnosticLogs.
            DiagnosticLogs = BindEx(cfg, false,
                "Legacy v1.1 diagnostic switch, retained for existing config files. In v1.2 basic diagnostic messages and workload reports are always on regardless of this setting.");

            AnnounceInGame = BindEx(cfg, true,
                "Send at most one nearby vanilla-client message every 90s for unlit lights that cannot refuel, plus sparse success notices. Does not require a client mod.");
        }
    }

    [Processor("7b02f823-3e61-4e45-9c64-b879d8f40ed8")]
    [RunAfter<ContainerRegistryProcessor>]
    public sealed class TorchRefillProcessor : Processor<ProcessorPrefabInfo<Fireplace>>
    {
        // A spatial chest-only index, not a global item index. Index construction
        // does NOT deserialize the inventory of every registered chest.
        private SectorDictionary<HashSet<ServersideQoLZDO>> _containers;
        private string[] _lightingTokens = Array.Empty<string>();
        private readonly HashSet<ServersideQoLZDO> _warnedLights = new HashSet<ServersideQoLZDO>();
        private readonly HashSet<string> _ignoredPrefabs = new HashSet<string>(StringComparer.Ordinal);

        private double _nextSummaryAt;
        private double _nextInGameAt;
        private int _firstWarnings;
        private int _suppressedWarnings;
        private int _successLogs;

        private long _checks;
        private long _needsFuel;
        private long _nearbyChests;
        private long _inventoryReads;
        private long _ownershipDelays;
        private long _transfers;
        private long _missingFuel;
        private long _scanTicks;

        protected override void Initialize()
        {
            foreach (ServersideQoLZDO light in _warnedLights)
                light.Destroyed -= OnLightDestroyed;
            _warnedLights.Clear();
            _ignoredPrefabs.Clear();

            ModConfig cfg = ModConfig.Instance;
            _containers = Instance<ContainerRegistryProcessor>()
                .GetContainers(Mathf.Max(1f, cfg.Range.Value));
            _lightingTokens = ParseTokens(cfg.LightingNameTokens.Value);

            _checks = _needsFuel = _nearbyChests = _inventoryReads = 0;
            _ownershipDelays = _transfers = _missingFuel = _scanTicks = 0;
            _firstWarnings = _suppressedWarnings = _successLogs = 0;
            _nextSummaryAt = Time.realtimeSinceStartupAsDouble + 120.0;
            _nextInGameAt = 0.0;

            // Warning level intentionally bypasses BepInEx Info/Debug filters.
            Logger.LogWarning($"[Server Torch Refill v1.2.0] RUNNING. Config: {cfg.ConfigFile.ConfigFilePath}. " +
                $"Range={cfg.Range.Value:0.#}m; refill whenever missing={cfg.RefillWheneverMissing.Value}; " +
                "fuel-transfer and missing-source messages are enabled by default. " +
                "No client installation required.");
        }

        protected override ProcessResult Process(
            ServersideQoLZDO zdo,
            IReadOnlyList<Peer> peers,
            ProcessorPrefabInfo<Fireplace> prefabInfo)
        {
            ModConfig cfg = ModConfig.Instance;
            Fireplace fireplace = prefabInfo.Component;

            if (!cfg.Enabled.Value || fireplace == null)
                return ProcessResult.UnregisterProcessor;

            // Name-filter before fuel setup: avoid touching anything unrelated.
            if (!cfg.IncludeAllFireplaces.Value &&
                !LooksLikeLighting(prefabInfo.PrefabInfo.PrefabName, fireplace.m_name))
                return ProcessResult.UnregisterProcessor;

            // The prefab really contains a Fireplace component, but some lights
            // (notably vanilla resin candles) are not refillable via normal fuel.
            if (fireplace.m_fuelItem == null ||
                fireplace.m_fuelItem.m_itemData == null ||
                fireplace.m_fuelItem.m_itemData.m_shared == null ||
                fireplace.m_maxFuel <= 0f ||
                fireplace.m_infiniteFuel || !fireplace.m_canRefill)
            {
                string name = prefabInfo.PrefabInfo.PrefabName;
                if (_ignoredPrefabs.Count < 12 && _ignoredPrefabs.Add(name))
                    Logger.LogWarning($"[Server Torch Refill] SKIPPED {name}: it has no normal refillable fuel source (infinite / m_canRefill=false / missing fuel item). This is expected for vanilla resin candles.");
                return ProcessResult.UnregisterProcessor;
            }

            MaybeWriteSummary();
            _checks++;

            float interval = Mathf.Max(30f, cfg.CheckIntervalSeconds.Value);
            float maxFuel = zdo.Fields<Fireplace>().GetFloat(static () => x => x.m_maxFuel);
            if (maxFuel <= 0f)
                return ScheduleReprocessing(Mathf.Max(180f, interval));

            float fuel = Mathf.Max(0f, zdo.Vars.GetFuel());
            float trigger = cfg.RefillWheneverMissing.Value ? Mathf.Clamp01(cfg.RefillToPercent.Value) : Mathf.Clamp01(cfg.RefillBelowPercent.Value);
            float refillTo = Mathf.Max(trigger, Mathf.Clamp01(cfg.RefillToPercent.Value));
            float target = Mathf.Min(maxFuel, maxFuel * refillTo);
            int needed = FuelPolicy.UnitsNeeded(fuel, maxFuel, trigger, refillTo);

            if (needed <= 0)
            {
                // Full / not eligible: do not search ANY inventory and do not
                // run again every 30 s. ZDO changes are still processed promptly
                // by the framework's changed-object tracking.
                return ScheduleReprocessing(Mathf.Max(180f, interval));
            }

            _needsFuel++;
            float range = Mathf.Max(0f, cfg.Range.Value);
            Vector3 pos = zdo.ZDO.GetPosition();
            ItemDrop.ItemData fuelItem = fireplace.m_fuelItem.m_itemData;
            int request = needed;
            bool wasDark = fuel <= 0.001f;

            if (range <= 0f || _containers == null)
            {
                WarnOnce(zdo, $"No fuel lookup: Range={range:0.#}m is disabled or the chest index is unavailable.");
                AnnounceFailure(peers, zdo, fuel, "Torch Refill: no chest range configured");
                return ScheduleReprocessing(Mathf.Max(60f, interval));
            }

            long start = Stopwatch.GetTimestamp();
            ContainerRegistryProcessor registry = Instance<ContainerRegistryProcessor>();
            float rangeSqr = range * range;
            int nearby = 0;
            int open = 0;
            int matching = 0;
            int reserved = 0;
            int awaitingOwnership = 0;
            int addedTotal = 0;

            foreach (HashSet<ServersideQoLZDO> group in _containers.EnumerateAdjacent(pos))
            {
                foreach (ServersideQoLZDO candidate in group)
                {
                    if (needed <= 0)
                        break;

                    if (Utils.DistanceSqr(pos, candidate.ZDO.GetPosition()) > rangeSqr)
                        continue;

                    ContainerState state = registry.GetState(candidate);
                    if (state == null)
                        continue;

                    nearby++;
                    _nearbyChests++;

                    if (candidate.Vars.GetInUse())
                    {
                        open++;
                        continue;
                    }

                    ContainerState.IInventory inventory = state.GetInventory();
                    _inventoryReads++;
                    int leave = Math.Max(0, cfg.LeaveFuelInContainer.Value);
                    int consumed = 0;
                    bool mustRequestOwnership = false;
                    List<ItemDrop.ItemData> emptied = null;

                    // No LINQ/sort/allocation in the hot path. One pass over only
                    // nearby closed chests that the SQoL registry permits.
                    foreach (ItemDrop.ItemData slot in inventory.Items)
                    {
                        if (slot == null || slot.m_stack <= 0 ||
                            !(new ItemDataKey(slot) == fuelItem))
                            continue;

                        matching += slot.m_stack;
                        int leaveHere = Math.Min(leave, slot.m_stack);
                        leave -= leaveHere;
                        reserved += leaveHere;
                        int take = Math.Min(needed, slot.m_stack - leaveHere);
                        if (take <= 0)
                            continue;

                        if (!candidate.IsOwnerOrUnassigned())
                        {
                            mustRequestOwnership = true;
                            break;
                        }

                        slot.m_stack -= take;
                        consumed += take;
                        needed -= take;
                        if (slot.m_stack == 0)
                            (emptied ??= new List<ItemDrop.ItemData>()).Add(slot);

                        if (needed <= 0)
                            break;
                    }

                    if (mustRequestOwnership)
                    {
                        awaitingOwnership++;
                        // Request at the SQoL-managed rate, but DO NOT turn its
                        // 0.1-second returned delay into a tight rescheduling loop.
                        registry.RequestOwnership(candidate, default);
                        continue;
                    }

                    if (consumed <= 0)
                        continue;

                    if (emptied != null)
                        foreach (ItemDrop.ItemData entry in emptied)
                            inventory.Items.Remove(entry);

                    zdo.ReleaseOwnership();
                    fuel = Mathf.Min(target, fuel + consumed);
                    zdo.Vars.SetFuel(fuel);
                    inventory.Save();
                    addedTotal += consumed;
                    _transfers++;

                    if (_warnedLights.Remove(zdo))
                        zdo.Destroyed -= OnLightDestroyed;

                    if (cfg.LogRefills.Value && _successLogs++ < 12)
                        Logger.LogMessage($"[Server Torch Refill] REFILLED {prefabInfo.PrefabInfo.PrefabName} " +
                            $"at {pos}: +{consumed} {fuelItem.m_shared.m_name}, now {fuel:0.##}/{maxFuel:0.##}; " +
                            $"source chest {candidate.ZDO.GetPosition()}.");
                }

                if (needed <= 0)
                    break;
            }

            _scanTicks += Stopwatch.GetTimestamp() - start;

            if (addedTotal == 0)
            {
                _missingFuel++;
                if (awaitingOwnership != 0)
                    _ownershipDelays++;

                string reason;
                if (nearby == 0)
                    reason = $"No eligible NON-PRIVATE chest within {range:0.#}m (personal/private containers are excluded by ServersideQoL).";
                else if (open == nearby)
                    reason = $"All {nearby} nearby eligible chests are currently open; close a chest to allow refilling.";
                else if (matching == 0)
                    reason = $"{nearby} nearby chest(s), but none contains matching {fuelItem.m_shared.m_name}.";
                else if (awaitingOwnership > 0)
                    reason = $"Found {fuelItem.m_shared.m_name}, but {awaitingOwnership} chest(s) are client-owned; waiting for ownership.";
                else
                    reason = $"Matching fuel exists but cannot be taken (reserved {reserved} item(s) by LeaveFuelInContainer, or chest unavailable).";

                WarnOnce(zdo, $"{prefabInfo.PrefabInfo.PrefabName} at {pos}: fuel {fuel:0.##}/{maxFuel:0.##}; needs {request} x {fuelItem.m_shared.m_name}. {reason}");
                AnnounceFailure(peers, zdo, fuel, $"Torch Refill: {reason}");
            }
            else
            {
                if (wasDark && fuel > 0f)
                    AnnounceSuccess(peers, zdo, $"Torch Refill: lit {prefabInfo.PrefabInfo.PrefabName} from a nearby chest.");
                if (needed == 0)
                    return ScheduleReprocessing(Mathf.Max(180f, interval));
            }

            // Retry modestly while dark, and less often while partially lit.
            // Ownership retries are NEVER faster than 10s even if the upstream
            // container registry suggests 0.1s. Other repeated failures wait
            // at least 30-60s, dramatically reducing idle workload.
            float retry = awaitingOwnership > 0 ? Mathf.Max(10f, interval) :
                fuel <= 0.001f ? interval : Mathf.Max(60f, interval);
            return ScheduleReprocessing(retry);
        }

        private void WarnOnce(ServersideQoLZDO zdo, string message)
        {
            if (_warnedLights.Contains(zdo))
                return;

            if (_warnedLights.Count < 256)
            {
                _warnedLights.Add(zdo);
                zdo.Destroyed += OnLightDestroyed;
            }

            if (_firstWarnings++ < 12)
                Logger.LogWarning($"[Server Torch Refill] NOT REFILLED: {message}");
            else
                _suppressedWarnings++;
        }

        private void OnLightDestroyed(ServersideQoLZDO zdo)
        {
            _warnedLights.Remove(zdo);
            zdo.Destroyed -= OnLightDestroyed;
        }

        private void AnnounceFailure(IReadOnlyList<Peer> peers, ServersideQoLZDO zdo, float fuel, string message)
        {
            if (fuel > 0.001f)
                return;
            Announce(peers, zdo, message);
        }

        private void AnnounceSuccess(IReadOnlyList<Peer> peers, ServersideQoLZDO zdo, string message)
        {
            Announce(peers, zdo, message);
        }

        private void Announce(IReadOnlyList<Peer> peers, ServersideQoLZDO zdo, string message)
        {
            if (!ModConfig.Instance.AnnounceInGame.Value ||
                Time.realtimeSinceStartupAsDouble < _nextInGameAt)
                return;

            _nextInGameAt = Time.realtimeSinceStartupAsDouble + 90.0;
            ShowMessage(peers, zdo, message, MessageTypes.TopLeftFar);
        }

        private void MaybeWriteSummary()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextSummaryAt)
                return;

            _nextSummaryAt = now + 120.0;
            double scanMs = _scanTicks * 1000.0 / Stopwatch.Frequency;
            Logger.LogWarning($"[Server Torch Refill] 2-minute activity: checked={_checks}, needFuel={_needsFuel}, " +
                $"nearbyChests={_nearbyChests}, inventoryReads={_inventoryReads}, refills={_transfers}, " +
                $"unfilled={_missingFuel}, waitingForOwnership={_ownershipDelays}, scanTime={scanMs:0.#}ms, " +
                $"suppressedWarnings={_suppressedWarnings}. " +
                "If checked=0, no eligible lights were processed near players.");

            _checks = _needsFuel = _nearbyChests = _inventoryReads = 0;
            _ownershipDelays = _transfers = _missingFuel = _scanTicks = 0;
            _suppressedWarnings = 0;
            _firstWarnings = 0;
            _successLogs = 0;
        }

        private bool LooksLikeLighting(string prefabName, string localizedName)
        {
            string prefab = prefabName ?? string.Empty;
            string display = localizedName ?? string.Empty;

            foreach (string token in _lightingTokens)
            {
                if (prefab.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    display.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string[] ParseTokens(string value)
            => (value ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
