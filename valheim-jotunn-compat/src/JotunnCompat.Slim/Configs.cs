using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;

namespace Jotunn.Configs
{
    public class RequirementConfig
    {
        public string Item { get; set; } = string.Empty;
        public int Amount { get; set; } = 1;
        public int AmountPerLevel { get; set; }
        public bool Recover { get; set; } = true;

        public RequirementConfig() { }

        public RequirementConfig(
            string item,
            int amount,
            int amountPerLevel = 0,
            bool recover = true)
        {
            Item = item;
            Amount = amount;
            AmountPerLevel = amountPerLevel;
            Recover = recover;
        }

        public bool IsValid() =>
            !string.IsNullOrEmpty(Item) && (Amount > 0 || AmountPerLevel > 0);

        public Piece.Requirement GetRequirement()
        {
            var prefab = PrefabManager.Instance.GetPrefab(Item);
            return new Piece.Requirement
            {
                m_resItem = prefab ? prefab.GetComponent<ItemDrop>() : null,
                m_amount = Amount,
                m_amountPerLevel = AmountPerLevel,
                m_recover = Recover
            };
        }
    }

    public class ItemConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Item { get; internal set; }
        public int Amount { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public string PieceTable { get; set; } = string.Empty;
        public string CraftingStation { get; set; } = string.Empty;
        public string RepairStation { get; set; } = string.Empty;
        public int MinStationLevel { get; set; } = 1;
        public bool RequireOnlyOneIngredient { get; set; }
        public int QualityResultAmountMultiplier { get; set; } = 1;
        public float Weight { get; set; } = -1f;
        public int StackSize = -1;
        public Sprite[] Icons { get; set; }
        public Texture2D StyleTex { get; set; }
        public RequirementConfig[] Requirements { get; set; } =
            Array.Empty<RequirementConfig>();

        public Sprite Icon
        {
            get => Icons != null && Icons.Length > 0 ? Icons[0] : null;
            set
            {
                if (Icons == null || Icons.Length == 0)
                {
                    Icons = new[] { value };
                }
                else
                {
                    Icons[0] = value;
                }
            }
        }

        public ItemConfig() { }

        public void Apply(GameObject prefab)
        {
            if (!prefab)
            {
                return;
            }

            var itemDrop = prefab.GetComponent<ItemDrop>();
            if (!itemDrop || itemDrop.m_itemData == null ||
                itemDrop.m_itemData.m_shared == null)
            {
                Logger.LogWarning("ItemConfig target has no usable ItemDrop: " + prefab.name);
                return;
            }

            Item = prefab.name;
            var shared = itemDrop.m_itemData.m_shared;

            if (!string.IsNullOrEmpty(Name))
            {
                shared.m_name = Name;
            }

            if (!string.IsNullOrEmpty(Description))
            {
                shared.m_description = Description;
            }

            if (!string.IsNullOrEmpty(PieceTable))
            {
                shared.m_buildPieces = PieceManager.Instance.GetPieceTable(PieceTable);
            }

            if (Weight >= 0f)
            {
                shared.m_weight = Weight;
            }

            if (StackSize >= 1)
            {
                shared.m_maxStackSize = StackSize;
            }

            if (Icons != null && Icons.Length > 0)
            {
                shared.m_icons = Icons;
            }
        }

        public Piece.Requirement[] GetRequirements()
        {
            return (Requirements ?? Array.Empty<RequirementConfig>())
                .Where(r => r != null && r.IsValid())
                .Select(r => r.GetRequirement())
                .ToArray();
        }

        public Recipe GetRecipe()
        {
            var prefab = PrefabManager.Instance.GetPrefab(Item);
            return GetRecipe(prefab);
        }

        internal Recipe GetRecipe(GameObject itemPrefab)
        {
            if (!itemPrefab || Requirements == null || Requirements.Length == 0)
            {
                return null;
            }

            var drop = itemPrefab.GetComponent<ItemDrop>();
            if (!drop)
            {
                return null;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + itemPrefab.name;
            recipe.m_item = drop;
            recipe.m_amount = Amount;
            recipe.m_enabled = Enabled;
            recipe.m_craftingStation = ResolveStation(CraftingStation);
            recipe.m_repairStation = ResolveStation(RepairStation);
            recipe.m_minStationLevel = MinStationLevel;
            recipe.m_resources = GetRequirements();
            recipe.m_requireOnlyOneIngredient = RequireOnlyOneIngredient;
            recipe.m_qualityResultAmountMultiplier = QualityResultAmountMultiplier;
            return recipe;
        }

        private static CraftingStation ResolveStation(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var prefab = PrefabManager.Instance.GetPrefab(name);
            return prefab ? prefab.GetComponent<CraftingStation>() : null;
        }
    }

    public class RecipeConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Item { get; set; } = string.Empty;
        public int Amount { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public string CraftingStation { get; set; } = string.Empty;
        public string RepairStation { get; set; } = string.Empty;
        public int MinStationLevel { get; set; } = 1;
        public bool RequireOnlyOneIngredient { get; set; }
        public int QualityResultAmountMultiplier { get; set; } = 1;
        public RequirementConfig[] Requirements { get; set; } =
            Array.Empty<RequirementConfig>();

        public RecipeConfig() { }

        public Piece.Requirement[] GetRequirements()
        {
            return (Requirements ?? Array.Empty<RequirementConfig>())
                .Where(r => r != null && r.IsValid())
                .Select(r => r.GetRequirement())
                .ToArray();
        }

        public Recipe GetRecipe()
        {
            if (string.IsNullOrEmpty(Item))
            {
                return null;
            }

            var itemPrefab = PrefabManager.Instance.GetPrefab(Item);
            var itemDrop = itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (!itemDrop)
            {
                return null;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = string.IsNullOrEmpty(Name) ? "Recipe_" + Item : Name;
            recipe.m_item = itemDrop;
            recipe.m_amount = Amount;
            recipe.m_enabled = Enabled;
            recipe.m_craftingStation = ResolveStation(CraftingStation);
            recipe.m_repairStation = ResolveStation(RepairStation);
            recipe.m_minStationLevel = MinStationLevel;
            recipe.m_resources = GetRequirements();
            recipe.m_requireOnlyOneIngredient = RequireOnlyOneIngredient;
            recipe.m_qualityResultAmountMultiplier = QualityResultAmountMultiplier;
            return recipe;
        }

        private static CraftingStation ResolveStation(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var prefab = PrefabManager.Instance.GetPrefab(name);
            return prefab ? prefab.GetComponent<CraftingStation>() : null;
        }
    }

    public class PieceConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public bool AllowedInDungeons { get; set; }
        public string PieceTable { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string[] Usage { get; set; } = Array.Empty<string>();
        public string CraftingStation { get; set; } = string.Empty;
        public string ExtendStation { get; set; } = string.Empty;
        public Sprite Icon { get; set; }
        public RequirementConfig[] Requirements { get; set; } =
            Array.Empty<RequirementConfig>();

        public PieceConfig() { }

        public void Apply(GameObject prefab)
        {
            if (!prefab)
            {
                return;
            }

            var piece = prefab.GetComponent<Piece>();
            if (!piece)
            {
                Logger.LogWarning("PieceConfig target has no Piece: " + prefab.name);
                return;
            }

            piece.m_enabled = Enabled;
            piece.m_allowedInDungeons = AllowedInDungeons;

            if (!string.IsNullOrEmpty(Name))
            {
                piece.m_name = Name;
            }

            if (!string.IsNullOrEmpty(Description))
            {
                piece.m_description = Description;
            }

            if (Icon)
            {
                piece.m_icon = Icon;
            }

            if (Requirements != null && Requirements.Length > 0)
            {
                piece.m_resources = GetRequirements();
            }

            if (!string.IsNullOrEmpty(CraftingStation))
            {
                var stationPrefab = PrefabManager.Instance.GetPrefab(CraftingStation);
                piece.m_craftingStation = stationPrefab
                    ? stationPrefab.GetComponent<CraftingStation>()
                    : null;
            }

            if (!string.IsNullOrEmpty(ExtendStation))
            {
                var extension = prefab.GetComponent<StationExtension>() ??
                    prefab.AddComponent<StationExtension>();
                var stationPrefab = PrefabManager.Instance.GetPrefab(ExtendStation);
                extension.m_craftingStation = stationPrefab
                    ? stationPrefab.GetComponent<CraftingStation>()
                    : null;
            }

            if (!string.IsNullOrEmpty(Category))
            {
                piece.m_category = PieceManager.Instance.AddPieceCategory(Category);
            }
        }

        public Piece.Requirement[] GetRequirements()
        {
            return (Requirements ?? Array.Empty<RequirementConfig>())
                .Where(r => r != null && r.IsValid())
                .Select(r => r.GetRequirement())
                .ToArray();
        }
    }

    public class PieceTableConfig
    {
        public bool UseCategories { get; set; } = true;
        public bool UseCustomCategories { get; set; }
        public string[] CustomCategories { get; set; } = Array.Empty<string>();
        public bool CanRemovePieces { get; set; } = true;
        public bool GuessUsage { get; set; } = true;

        public PieceTableConfig() { }

        public string[] GetCategories()
        {
            if (!UseCustomCategories)
            {
                return Array.Empty<string>();
            }

            return CustomCategories ?? Array.Empty<string>();
        }

        public void Apply(GameObject prefab)
        {
            var table = prefab ? prefab.GetComponent<PieceTable>() : null;
            if (table)
            {
                table.m_canRemovePieces = CanRemovePieces;
            }
        }
    }

    public class LocationConfig
    {
        public Heightmap.Biome Biome { get; set; }
        public Heightmap.BiomeArea BiomeArea { get; set; } = Heightmap.BiomeArea.Everything;
        public bool Priotized { get; set; }
        public int Quantity { get; set; }

        private float? exteriorRadius;
        public float ExteriorRadius
        {
            get => exteriorRadius ?? 10f;
            set => exteriorRadius = value;
        }
        internal bool HasExteriorRadius => exteriorRadius.HasValue;

        public bool CenterFirst { get; set; }
        public bool InForest { get; set; }
        public float ForestTresholdMin { get; set; }
        public float ForestTrasholdMax { get; set; } = 1f;
        public bool Unique { get; set; }
        public float MinAltitude { get; set; } = -1000f;
        public float MaxAltitude { get; set; } = 1000f;
        public float MinDistance { get; set; }
        public float MaxDistance { get; set; }
        public string Group { get; set; } = string.Empty;
        public float MinDistanceFromSimilar { get; set; }
        public float MinTerrainDelta { get; set; }
        public float MaxTerrainDelta { get; set; } = 2f;
        public bool SlopeRotation { get; set; }

        public bool HasInterior { get; set; }

        private float? interiorRadius;
        public float InteriorRadius
        {
            get => interiorRadius ?? 0f;
            set => interiorRadius = value;
        }
        internal bool HasInteriorRadius => interiorRadius.HasValue;

        public string InteriorEnvironment { get; set; }
        public bool RandomRotation { get; set; } = true;
        public bool SnapToWater { get; set; }
        public bool IconPlaced { get; set; }
        public bool IconAlways { get; set; }

        private bool? clearArea;
        public bool ClearArea
        {
            get => clearArea ?? false;
            set => clearArea = value;
        }
        internal bool HasClearArea => clearArea.HasValue;

        internal ZoneSystem.ZoneLocation GetZoneLocation()
        {
            return new ZoneSystem.ZoneLocation
            {
                m_biome = Biome,
                m_biomeArea = BiomeArea,
                m_quantity = Quantity,
                m_prioritized = Priotized,
                m_interiorRadius = InteriorRadius,
                m_exteriorRadius = ExteriorRadius,
                m_clearArea = ClearArea,
                m_centerFirst = CenterFirst,
                m_forestTresholdMin = ForestTresholdMin,
                m_forestTresholdMax = ForestTrasholdMax,
                m_unique = Unique,
                m_minAltitude = MinAltitude,
                m_maxAltitude = MaxAltitude,
                m_minDistance = MinDistance,
                m_maxDistance = MaxDistance,
                m_group = Group,
                m_inForest = InForest,
                m_minTerrainDelta = MinTerrainDelta,
                m_maxTerrainDelta = MaxTerrainDelta,
                m_minDistanceFromSimilar = MinDistanceFromSimilar,
                m_slopeRotation = SlopeRotation,
                m_randomRotation = RandomRotation,
                m_snapToWater = SnapToWater,
                m_iconPlaced = IconPlaced,
                m_iconAlways = IconAlways
            };
        }
    }

    public class RoomConfig
    {
        public string ThemeName { get; set; }
        public bool? Enabled { get; set; }
        public bool? Entrance { get; set; }
        public bool? Endcap { get; set; }
        public bool? Divider { get; set; }
        public int? EndcapPrio { get; set; }
        public int? MinPlaceOrder { get; set; }
        public float? Weight { get; set; }
        public bool? FaceCenter { get; set; }
        public bool? Perimeter { get; set; }

        public RoomConfig() { }
        public RoomConfig(string themeName) { ThemeName = themeName; }

        internal Room Apply(Room room)
        {
            if (Enabled.HasValue) room.m_enabled = Enabled.Value;
            if (Entrance.HasValue) room.m_entrance = Entrance.Value;
            if (Endcap.HasValue) room.m_endCap = Endcap.Value;
            if (Divider.HasValue) room.m_divider = Divider.Value;
            if (EndcapPrio.HasValue) room.m_endCapPrio = EndcapPrio.Value;
            if (MinPlaceOrder.HasValue) room.m_minPlaceOrder = MinPlaceOrder.Value;
            if (Weight.HasValue) room.m_weight = Weight.Value;
            if (FaceCenter.HasValue) room.m_faceCenter = FaceCenter.Value;
            if (Perimeter.HasValue) room.m_perimeter = Perimeter.Value;
            return room;
        }
    }
}
