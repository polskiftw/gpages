using UnityEngine;

namespace Jotunn.Configs
{
    public class ItemConfig
    {
        public ItemConfig() { }
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
