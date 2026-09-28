using BepInEx;
using BepInEx.Configuration;
using ServersideQoL;
using ServersideQoL.Processors;
using ServersideQoL.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ServerTorchRefillMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class ServerTorchRefillPlugin : ServersideQoLPluginBase<ServerTorchRefillPlugin, ModConfig>
    {
        public const string PluginGuid = "claire.valheim.servertorchrefill";
        public const string PluginName = "Server Torch Refill";
        public const string PluginVersion = "1.0.0";

        protected override ModConfig CreateConfigSingleton(ConfigFile configFile, ServersideQoL.Logger logger)
            => new ModConfig(configFile, logger);

        protected override void RegisterProcessors(IProcessorCollection processors)
            => processors.Add<TorchRefillProcessor>();
    }

    public sealed class ModConfig : ConfigBase<ModConfig>
    {
        public override ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<float> Range { get; }
        public ConfigEntry<float> RefillBelowPercent { get; }
        public ConfigEntry<float> RefillToPercent { get; }
        public ConfigEntry<int> LeaveFuelInContainer { get; }
        public ConfigEntry<float> CheckIntervalSeconds { get; }
        public ConfigEntry<bool> IncludeAllFireplaces { get; }
        public ConfigEntry<string> LightingNameTokens { get; }

        public ModConfig(ConfigFile cfg, ServersideQoL.Logger logger) : base(cfg, logger)
        {
            Enabled = BindEx(
                cfg,
                true,
                "Enables/disables Server Torch Refill.");

            Range = BindEx(
                cfg,
                10f,
                "Maximum distance in metres between a light and a storage container used as its fuel source. Set to 0 to disable refilling.");

            RefillBelowPercent = BindEx(
                cfg,
                0.25f,
                "Start refilling when current fuel is at or below this fraction of capacity. Values are clamped to 0..1.");

            RefillToPercent = BindEx(
                cfg,
                1f,
                "Refill toward this fraction of the light's capacity. Values are clamped to 0..1 and never below RefillBelowPercent.");

            LeaveFuelInContainer = BindEx(
                cfg,
                0,
                "Minimum number of matching fuel items to leave behind across each candidate container.");

            CheckIntervalSeconds = BindEx(
                cfg,
                30f,
                "Fallback interval in seconds between checks. Nearby container changes also wake affected lights immediately.");

            IncludeAllFireplaces = BindEx(
                cfg,
                false,
                "If true, refill every refillable Fireplace (including campfires/hearths/bonfires). If false, only names matching LightingNameTokens are handled.");

            LightingNameTokens = BindEx(
                cfg,
                "torch,sconce,brazier,candle,jackoturnip,lantern",
                "Comma-separated case-insensitive prefab/localization-name fragments used to identify lighting when IncludeAllFireplaces is false. Fuel type is never inferred from these names; the Fireplace's real m_fuelItem is always used.");
        }
    }

    [Processor("7b02f823-3e61-4e45-9c64-b879d8f40ed8")]
    [RunAfter<ContainerRegistryProcessor>]
    public sealed class TorchRefillProcessor : Processor<ProcessorPrefabInfo<Fireplace>>
    {
        private SectorDictionary<SharedItemDataKey, HashSet<ServersideQoLZDO>> _containersByItemName;
        private SectorDictionary<HashSet<ServersideQoLZDO>> _lights;
        private string[] _lightingTokens = Array.Empty<string>();

        protected override void Initialize()
        {
            ContainerRegistryProcessor registry = Instance<ContainerRegistryProcessor>();
            registry.ContainerChanged -= OnContainerChanged;

            float sectorWidth = Mathf.Max(1f, ModConfig.Instance.Range.Value);
            _lights = new SectorDictionary<HashSet<ServersideQoLZDO>>(sectorWidth);
            _containersByItemName = registry.GetContainersByItemName(_lights.SectorWidth);
            _lightingTokens = ParseTokens(ModConfig.Instance.LightingNameTokens.Value);

            registry.ContainerChanged += OnContainerChanged;
        }

        protected override ProcessResult Process(
            ServersideQoLZDO zdo,
            IReadOnlyList<Peer> peers,
            ProcessorPrefabInfo<Fireplace> prefabInfo)
        {
            ModConfig cfg = ModConfig.Instance;
            Fireplace fireplace = prefabInfo.Component;

            if (!cfg.Enabled.Value ||
                fireplace == null ||
                fireplace.m_fuelItem == null ||
                fireplace.m_fuelItem.m_itemData == null ||
                fireplace.m_fuelItem.m_itemData.m_shared == null ||
                fireplace.m_maxFuel <= 0f ||
                fireplace.m_infiniteFuel ||
                !fireplace.m_canRefill)
            {
                return ProcessResult.UnregisterProcessor;
            }

            if (!cfg.IncludeAllFireplaces.Value &&
                !LooksLikeLighting(prefabInfo.PrefabInfo.PrefabName, fireplace.m_name))
            {
                return ProcessResult.UnregisterProcessor;
            }

            _lights?.TryAdd(zdo);

            float interval = Mathf.Max(5f, cfg.CheckIntervalSeconds.Value);
            ProcessResult result = ScheduleReprocessing(interval);

            float range = Mathf.Max(0f, cfg.Range.Value);
            if (range <= 0f || _containersByItemName == null)
                return result;

            float maxFuel = zdo.Fields<Fireplace>().GetFloat(static () => x => x.m_maxFuel);
            if (maxFuel <= 0f)
                return result;

            float currentFuel = Mathf.Max(0f, zdo.Vars.GetFuel());
            float refillBelow = Mathf.Clamp01(cfg.RefillBelowPercent.Value);
            float refillTo = Mathf.Max(refillBelow, Mathf.Clamp01(cfg.RefillToPercent.Value));

            if ((currentFuel / maxFuel) > refillBelow)
                return result;

            float targetFuel = Mathf.Min(maxFuel, maxFuel * refillTo);
            int fuelNeeded = Mathf.CeilToInt(targetFuel - currentFuel);
            if (fuelNeeded <= 0)
                return result;

            ItemDrop.ItemData fuelItem = fireplace.m_fuelItem.m_itemData;
            Vector3 lightPosition = zdo.ZDO.GetPosition();
            float rangeSqr = range * range;
            ContainerRegistryProcessor registry = Instance<ContainerRegistryProcessor>();

            foreach (HashSet<ServersideQoLZDO> containers in
                     _containersByItemName.EnumerateAdjacent((lightPosition, fuelItem.m_shared)))
            {
                foreach (ServersideQoLZDO containerZdo in containers)
                {
                    if (fuelNeeded <= 0)
                        break;

                    ContainerState containerState = registry.GetState(containerZdo);
                    if (containerState == null)
                        continue;

                    // Never mutate a chest while a player has it open.
                    if (containerZdo.Vars.GetInUse())
                        continue;

                    if (Utils.DistanceSqr(lightPosition, containerZdo.ZDO.GetPosition()) > rangeSqr)
                        continue;

                    ContainerState.IInventory inventory = containerState.GetInventory();
                    int leave = Math.Max(0, cfg.LeaveFuelInContainer.Value);
                    int addFuel = 0;
                    bool requestOwnership = false;
                    List<ItemDrop.ItemData> removeSlots = null;

                    foreach (ItemDrop.ItemData slot in inventory.Items
                                 .Where(x => new ItemDataKey(x) == fuelItem)
                                 .OrderBy(x => x.m_stack))
                    {
                        int leaveHere = Math.Min(slot.m_stack, leave);
                        int take = Math.Min(fuelNeeded, slot.m_stack - leaveHere);
                        leave -= leaveHere;

                        if (take <= 0)
                            continue;

                        if (!containerZdo.IsOwnerOrUnassigned())
                        {
                            requestOwnership = true;
                            break;
                        }

                        slot.m_stack -= take;
                        addFuel += take;
                        fuelNeeded -= take;

                        if (slot.m_stack == 0)
                            (removeSlots ??= new List<ItemDrop.ItemData>()).Add(slot);

                        if (fuelNeeded == 0)
                            break;
                    }

                    if (requestOwnership)
                    {
                        result |= ScheduleReprocessing(registry.RequestOwnership(containerZdo, default));
                        continue;
                    }

                    if (addFuel <= 0)
                        continue;

                    if (removeSlots != null)
                    {
                        foreach (ItemDrop.ItemData remove in removeSlots)
                            inventory.Items.Remove(remove);
                    }

                    // Match ServersideQoL's own AutoProcess ownership pattern:
                    // make the target ZDO unowned, write the authoritative fuel,
                    // then serialize/send the chest inventory change.
                    zdo.ReleaseOwnership();
                    currentFuel = Mathf.Min(maxFuel, currentFuel + addFuel);
                    zdo.Vars.SetFuel(currentFuel);
                    inventory.Save();
                }

                if (fuelNeeded <= 0)
                    break;
            }

            return result;
        }

        private void OnContainerChanged(ServersideQoLZDO containerZdo, ContainerState state)
        {
            if (_lights == null)
                return;

            float range = Mathf.Max(0f, ModConfig.Instance.Range.Value);
            if (range <= 0f)
                return;

            float rangeSqr = range * range;
            Vector3 containerPosition = containerZdo.ZDO.GetPosition();

            foreach (HashSet<ServersideQoLZDO> lights in _lights.EnumerateAdjacent(containerPosition))
            {
                foreach (ServersideQoLZDO light in lights)
                {
                    if (Utils.DistanceSqr(light.ZDO.GetPosition(), containerPosition) <= rangeSqr)
                        ScheduleReprocessing(light);
                }
            }
        }

        private bool LooksLikeLighting(string prefabName, string localizedName)
        {
            if (_lightingTokens == null || _lightingTokens.Length == 0)
                return false;

            string prefab = prefabName ?? string.Empty;
            string display = localizedName ?? string.Empty;

            foreach (string token in _lightingTokens)
            {
                if (prefab.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    display.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
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
