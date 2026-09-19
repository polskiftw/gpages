/*
 * SPDX-License-Identifier: GPL-3.0-only
 *
 * Hammer Everything compatibility layer.
 *
 * This file incorporates and adapts prefab compatibility data and behavior from:
 * MoreVanillaBuildPrefabs (MVBP), https://github.com/searica/MoreVanillaBuildPrefabs
 * Upstream branch consulted: dev
 * Upstream files include PrefabManagement/PrefabPatcher.cs,
 * PrefabManagement/PrefabConfigManager.cs, Functions/Placement.cs,
 * SnapPoints/SnapPointManager.cs, and Utils/ColliderManager.cs.
 *
 * MVBP is distributed under GNU GPL v3.0. Hammer Everything is GPL-3.0-only.
 * See LICENSE and THIRD_PARTY_NOTICES.md.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace HammerEverythingMod
{
    public sealed partial class HammerEverything
    {
        private const string MvbpTop = "$hud_snappoint_top";
        private const string MvbpBottom = "$hud_snappoint_bottom";
        private const string MvbpCenter = "$hud_snappoint_center";
        private const string MvbpCorner = "$hud_snappoint_corner";
        private const string MvbpEdge = "$hud_snappoint_edge";
        private const string MvbpInner = "$hud_snappoint_inner";
        private const string MvbpOuter = "Outer";

        private static readonly HashSet<string> MvbpPlacementColliderPatchNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Ashland_Steepstair",
                "Ashlands_ArchRoof",
                "Ashlands_Boss_Pillar",
                "Ashlands_Ruins_Floor_6x6_broken2",
                "Ashlands_Ruins_Wall_Windows_Broken_4x6",
                "BossStone_Bonemass",
                "Candle_resin_bogwitch",
                "ancient_skull",
                "blackmarble_stair_corner",
                "blackmarble_stair_corner_left",
                "piece_blackwood_bench",
                "veg_skull_Ashlands"
            };

        private static readonly Dictionary<string, float[]> MvbpPlacementOffsets =
            new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "dvergrprops_pickaxe", new[] { -1f, 0f, 0f } }
            };

        private readonly HashSet<string> _mvbpPatchedPrefabNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<object> _mvbpPatchedPlacementGhosts =
            new HashSet<object>();
        private readonly Dictionary<object, float[]> _mvbpPlacementGhostCorrections =
            new Dictionary<object, float[]>();

        private readonly struct CompatSnapPoint
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;
            public readonly string Name;

            public CompatSnapPoint(float x, float y, float z, string name = null)
            {
                X = x;
                Y = y;
                Z = z;
                Name = name;
            }
        }

        private static CompatSnapPoint MvbpPoint(float x, float y, float z, string name = null)
        {
            return new CompatSnapPoint(x, y, z, name);
        }

        private void ApplyMoreVanillaCompatibilityFixes(object prefab, object piece, string prefabName)
        {
            if (prefab == null || piece == null || string.IsNullOrWhiteSpace(prefabName))
                return;

            if (!_mvbpPatchedPrefabNames.Add(prefabName))
                return;

            ResolveTypes();

            try
            {
                switch (prefabName)
                {
                    case "ArmorStand_Male":
                    case "ArmorStand_Female":
                        AddMvbpSnapPoints(prefab, MvbpPoint(0f, 0f, 0f, "Origin"));
                        break;

                    case "blackmarble_column_3":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(-1f, 4f, -1f), MvbpPoint(1f, 4f, 1f),
                            MvbpPoint(-1f, 4f, 1f), MvbpPoint(1f, 4f, -1f),
                            MvbpPoint(-1f, 2f, -1f), MvbpPoint(1f, 2f, 1f),
                            MvbpPoint(-1f, 2f, 1f), MvbpPoint(1f, 2f, -1f),
                            MvbpPoint(-1f, 0f, -1f), MvbpPoint(1f, 0f, 1f),
                            MvbpPoint(-1f, 0f, 1f), MvbpPoint(1f, 0f, -1f),
                            MvbpPoint(-1f, -2f, -1f), MvbpPoint(1f, -2f, 1f),
                            MvbpPoint(-1f, -2f, 1f), MvbpPoint(1f, -2f, -1f),
                            MvbpPoint(-1f, -4f, -1f), MvbpPoint(1f, -4f, 1f),
                            MvbpPoint(-1f, -4f, 1f), MvbpPoint(1f, -4f, -1f));
                        break;

                    case "blackmarble_creep_stair":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(-1f, 1f, -1f), MvbpPoint(1f, 1f, -1f),
                            MvbpPoint(-1f, 0f, -1f), MvbpPoint(1f, 0f, -1f),
                            MvbpPoint(-1f, 0f, 1f), MvbpPoint(1f, 0f, 1f));
                        break;

                    case "blackmarble_floor_large":
                    {
                        List<CompatSnapPoint> points = new List<CompatSnapPoint>();
                        for (int y = -1; y <= 1; y += 2)
                        {
                            for (int x = -4; x <= 4; x += 2)
                            {
                                for (int z = -4; z <= 4; z += 2)
                                    points.Add(MvbpPoint(x, y, z));
                            }
                        }
                        AddMvbpSnapPoints(prefab, points.ToArray());
                        break;
                    }

                    case "blackmarble_tile_floor_1x1":
                        SetMvbpChildLocalPosition(prefab, "_snappoint", 0.5f, 0.1f, 0.5f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (1)", 0.5f, 0.1f, -0.5f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (2)", -0.5f, 0.1f, 0.5f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (3)", -0.5f, 0.1f, -0.5f);
                        break;

                    case "blackmarble_tile_floor_2x2":
                        SetMvbpChildLocalPosition(prefab, "_snappoint", 1f, 0.1f, 1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (1)", 1f, 0.1f, -1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (2)", -1f, 0.1f, 1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (3)", -1f, 0.1f, -1f);
                        break;

                    case "blackmarble_tile_wall_1x1":
                        SetMvbpChildLocalPosition(prefab, "_snappoint", 0.5f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (1)", 0.5f, 0.95f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (2)", -0.5f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (3)", -0.5f, 0.95f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (4)", 0f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (5)", 0.5f, 0.45f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (6)", 0f, 0.95f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (7)", -0.5f, 0.45f, 0.1f);
                        break;

                    case "blackmarble_tile_wall_2x2":
                        SetMvbpChildLocalPosition(prefab, "_snappoint", 1f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (1)", 1f, 1.95f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (2)", -1f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (3)", -1f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (4)", 0.5f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (5)", 0.5f, 0.95f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (6)", -0.5f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (7)", -0.5f, 0.95f, 0.1f);
                        break;

                    case "blackmarble_tile_wall_2x4":
                        SetMvbpChildLocalPosition(prefab, "_snappoint", 1f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (1)", 1f, 3.95f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (2)", -1f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (3)", -1f, 3.95f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (4)", 0.5f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (5)", 0.5f, 1.95f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (6)", -0.5f, -0.05f, 0.1f);
                        SetMvbpChildLocalPosition(prefab, "_snappoint (7)", -0.5f, 1.95f, 0.1f);
                        break;

                    case "dungeon_queen_door":
                        AddMvbpSnapPoints(prefab, MvbpPoint(2.5f, 0f, 0f), MvbpPoint(-2.5f, 0f, 0f));
                        break;

                    case "dungeon_sunkencrypt_irongate":
                        AddMvbpSnapPoints(prefab, MvbpPoint(1f, -0.4f, 0f), MvbpPoint(-1f, -0.4f, 0f));
                        break;

                    case "sunken_crypt_gate":
                        AddMvbpSnapPoints(prefab, MvbpPoint(1f, 0f, 0f), MvbpPoint(-1f, 0f, 0f));
                        break;

                    case "dvergrprops_wood_beam":
                        AddMvbpSnapPoints(prefab, MvbpPoint(3f, 0f, 0f), MvbpPoint(-3f, 0f, 0f));
                        break;

                    case "dvergrprops_wood_pole":
                        AddMvbpSnapPoints(prefab, MvbpPoint(0f, 2f, 0f), MvbpPoint(0f, -2f, 0f));
                        break;

                    case "dvergrprops_wood_wall":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(2.2f, 2f, 0f), MvbpPoint(-2.2f, 2f, 0f),
                            MvbpPoint(2.2f, -2f, 0f), MvbpPoint(-2.2f, -2f, 0f));
                        break;

                    case "dvergrtown_arch":
                        AddMvbpSnapPoints(prefab, MvbpPoint(1f, 0.5f, 0f));
                        break;

                    case "dvergrtown_secretdoor":
                    case "dvergrtown_slidingdoor":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(2f, 0f, 0f), MvbpPoint(-2f, 0f, 0f),
                            MvbpPoint(2f, 4f, 0f), MvbpPoint(-2f, 4f, 0f));
                        break;

                    case "dvergrtown_stair_corner_wood_left":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(0.25f, 0f, -0.25f),
                            MvbpPoint(0.25f, 0f, 0.25f),
                            MvbpPoint(0.25f, 1.1f, -0.25f),
                            MvbpPoint(0.25f, 1.1f, 0.25f),
                            MvbpPoint(0.25f, 0f, 2f),
                            MvbpPoint(-0.25f, 1.1f, -0.25f),
                            MvbpPoint(-2f, 1.1f, -0.25f));
                        break;

                    case "dvergrprops_shelf":
                    case "dvergrprops_table":
                        SetMvbpSupports(prefab, true);
                        break;

                    case "dvergrtown_wood_beam":
                        AddMvbpSnapPoints(prefab, MvbpPoint(3f, 0f, 0f), MvbpPoint(-3f, 0f, 0f));
                        break;

                    case "dvergrtown_wood_pole":
                        AddMvbpSnapPoints(prefab, MvbpPoint(0f, -2f, 0f), MvbpPoint(0f, 2f, 0f));
                        break;

                    case "dvergrtown_wood_stake":
                        AddMvbpSnapPoints(prefab, MvbpPoint(0f, 0f, 0f));
                        break;

                    case "dvergrtown_wood_wall01":
                        SetMvbpChildLocalPosition(prefab, "wallcollider", 0f, 0f, 0f);
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(-3f, -2.7f, 0f), MvbpPoint(3f, -2.7f, 0f),
                            MvbpPoint(-3f, 2.7f, 0f), MvbpPoint(3f, 2.7f, 0f));
                        break;

                    case "dvergrtown_wood_wall02":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(-3f, -0.5f, 0f), MvbpPoint(3f, -0.5f, 0f),
                            MvbpPoint(-3f, 4.5f, 0f), MvbpPoint(3f, 4.5f, 0f));
                        break;

                    case "dvergrtown_wood_wall03":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(1.1f, 0f, 0f), MvbpPoint(-1.1f, 0f, 0f),
                            MvbpPoint(2f, 2f, 0f), MvbpPoint(-2f, 2f, 0f),
                            MvbpPoint(1.1f, 4f, 0f), MvbpPoint(-1.1f, 4f, 0f));
                        break;

                    case "goblin_roof_45d":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(1f, 0f, 1f), MvbpPoint(-1f, 0f, 1f),
                            MvbpPoint(1f, 2f, -1f), MvbpPoint(-1f, 2f, -1f));
                        break;

                    case "goblin_roof_45d_corner":
                        AddMvbpSnapPoints(prefab, MvbpPoint(-1f, 0f, -1f), MvbpPoint(1f, 0f, 1f));
                        break;

                    case "goblin_woodwall_1m":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(-0.5f, 0f, 0f), MvbpPoint(0.5f, 0f, 0f),
                            MvbpPoint(-0.5f, 2f, 0f), MvbpPoint(0.5f, 2f, 0f));
                        break;

                    case "goblin_woodwall_2m":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(-1f, 0f, 0f), MvbpPoint(1f, 0f, 0f),
                            MvbpPoint(-1f, 2f, 0f), MvbpPoint(1f, 2f, 0f));
                        break;

                    case "Ice_floor":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(2f, 1f, 2f), MvbpPoint(-2f, 1f, -2f),
                            MvbpPoint(2f, 1f, -2f), MvbpPoint(-2f, 1f, 2f),
                            MvbpPoint(2f, -1f, 2f), MvbpPoint(-2f, -1f, -2f),
                            MvbpPoint(2f, -1f, -2f), MvbpPoint(-2f, -1f, 2f));
                        break;

                    case "turf_roof_top":
                        AddMvbpSnapPoints(prefab, MvbpPoint(1f, 0.5f, 0f), MvbpPoint(-1f, 0.5f, 0f));
                        break;

                    case "stone_floor":
                    {
                        List<CompatSnapPoint> points = new List<CompatSnapPoint>();
                        for (float y = -0.5f; y <= 0.5f; y += 1f)
                        {
                            for (int x = -2; x <= 2; x++)
                            {
                                for (int z = -2; z <= 2; z++)
                                {
                                    if (!(Math.Abs(x) == 2 && Math.Abs(z) == 2))
                                        points.Add(MvbpPoint(x, y, z));
                                }
                            }
                        }
                        AddMvbpSnapPoints(prefab, points.ToArray());
                        break;
                    }

                    case "dvergrprops_hooknchain":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(0f, 2.5f, 0f, MvbpTop),
                            MvbpPoint(0f, -2f, 0f, "Hook"));
                        break;

                    case "barrell":
                        AddMvbpSnapPoints(prefab, MvbpPoint(0f, -1f, 0f));
                        break;

                    case "goblin_strawpile":
                        AddMvbpBoxCollider(prefab, 0f, 0f, 0f, 1.5f, 0.02f, 1.5f);
                        break;

                    case "mountainkit_chair":
                        AddMvbpChair(prefab, 0f, 0f, 0f);
                        break;

                    case "dvergrprops_chair":
                        AddMvbpChair(prefab, 0f, -0.15f, 0f);
                        break;

                    case "dvergrprops_stool":
                        AddMvbpChair(prefab, 0f, -0.1f, 0f);
                        break;

                    case "Ashland_Stair":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(2f, 0f, -2f, MvbpBottom + " 1"),
                            MvbpPoint(-2f, 0f, -2f, MvbpBottom + " 2"),
                            MvbpPoint(-2f, 0f, 2f, MvbpBottom + " 3"),
                            MvbpPoint(2f, 0f, 2f, MvbpBottom + " 4"),
                            MvbpPoint(2f, 2f, -2f, MvbpTop + " 1"),
                            MvbpPoint(-2f, 2f, -2f, MvbpTop + " 2"));
                        break;

                    case "Ashlands_Fortress_Floor":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(1f, -0.5f, -1f, MvbpBottom + " 1"),
                            MvbpPoint(-1f, -0.5f, -1f, MvbpBottom + " 2"),
                            MvbpPoint(-1f, -0.5f, 1f, MvbpBottom + " 3"),
                            MvbpPoint(1f, -0.5f, 1f, MvbpBottom + " 4"),
                            MvbpPoint(1f, 0.5f, -1f, MvbpTop + " 1"),
                            MvbpPoint(-1f, 0.5f, -1f, MvbpTop + " 2"),
                            MvbpPoint(-1f, 0.5f, 1f, MvbpTop + " 3"),
                            MvbpPoint(1f, 0.5f, 1f, MvbpTop + " 4"),
                            MvbpPoint(0f, 0f, 0f, MvbpCenter));
                        break;

                    case "Ashlands_Wall_2x2":
                    case "Ashlands_Wall_2x2_top":
                    case "Ashlands_Wall_2x2_edge2":
                    case "Ashlands_Wall_2x2_edge2_top":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(-1f, 0f, -0.5f, MvbpInner + " " + MvbpBottom + " 1"),
                            MvbpPoint(1f, 0f, -0.5f, MvbpInner + " " + MvbpBottom + " 2"),
                            MvbpPoint(-1f, 2f, -0.5f, MvbpInner + " " + MvbpTop + " 1"),
                            MvbpPoint(1f, 2f, -0.5f, MvbpInner + " " + MvbpTop + " 2"),
                            MvbpPoint(-1f, 0f, 0.5f, MvbpOuter + " " + MvbpBottom + " 1"),
                            MvbpPoint(1f, 0f, 0.5f, MvbpOuter + " " + MvbpBottom + " 2"),
                            MvbpPoint(-1f, 2f, 0.5f, MvbpOuter + " " + MvbpTop + " 1"),
                            MvbpPoint(1f, 2f, 0.5f, MvbpOuter + " " + MvbpTop + " 2"));
                        break;

                    case "Ashlands_Wall_2x2_edge":
                    case "Ashlands_Wall_2x2_edge_top":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(1f, 0f, 0.5f, MvbpInner + " " + MvbpBottom + " 1"),
                            MvbpPoint(-1f, 0f, 0.5f, MvbpInner + " " + MvbpBottom + " 2"),
                            MvbpPoint(1f, 2f, 0.5f, MvbpInner + " " + MvbpTop + " 1"),
                            MvbpPoint(-1f, 2f, 0.5f, MvbpInner + " " + MvbpTop + " 2"),
                            MvbpPoint(1f, 0f, -0.5f, MvbpOuter + " " + MvbpBottom + " 1"),
                            MvbpPoint(-1f, 0f, -0.5f, MvbpOuter + " " + MvbpBottom + " 2"),
                            MvbpPoint(1f, 2f, -0.5f, MvbpOuter + " " + MvbpTop + " 1"),
                            MvbpPoint(-1f, 2f, -0.5f, MvbpOuter + " " + MvbpTop + " 2"));
                        break;

                    case "Ashlands_Wall_2x2_cornerR":
                    case "Ashlands_Wall_2x2_cornerR_top":
                    {
                        float xOffset = -0.25f;
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(xOffset, 0f, -1f, MvbpInner + " " + MvbpBottom + " 1"),
                            MvbpPoint(xOffset, 0f, 0f, MvbpInner + " " + MvbpBottom + " " + MvbpCorner),
                            MvbpPoint(xOffset + 1f, 0f, 0f, MvbpInner + " " + MvbpBottom + " 2"),
                            MvbpPoint(xOffset, 2f, -1f, MvbpInner + " " + MvbpTop + " 1"),
                            MvbpPoint(xOffset, 2f, 0f, MvbpInner + " " + MvbpTop + " " + MvbpCorner),
                            MvbpPoint(xOffset + 1f, 2f, 0f, MvbpInner + " " + MvbpTop + " 2"),
                            MvbpPoint(xOffset - 1f, 0f, -1f, MvbpOuter + " " + MvbpBottom + " 1"),
                            MvbpPoint(xOffset - 1f, 0f, 1f, MvbpOuter + " " + MvbpBottom + " " + MvbpCorner),
                            MvbpPoint(xOffset + 1f, 0f, 1f, MvbpOuter + " " + MvbpBottom + " 2"),
                            MvbpPoint(xOffset - 1f, 2f, -1f, MvbpOuter + " " + MvbpTop + " 1"),
                            MvbpPoint(xOffset - 1f, 2f, 1f, MvbpOuter + " " + MvbpTop + " " + MvbpCorner),
                            MvbpPoint(xOffset + 1f, 2f, 1f, MvbpOuter + " " + MvbpTop + " 2"));
                        break;
                    }

                    case "Ashlands_Wall_2x2_cornerL":
                    case "Ashlands_Wall_2x2_cornerL_top":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(0f, 0f, -1.25f, MvbpInner + " " + MvbpBottom + " 1"),
                            MvbpPoint(0f, 0f, -0.5f, MvbpInner + " " + MvbpBottom + " " + MvbpCorner),
                            MvbpPoint(1.25f, 0f, -0.5f, MvbpInner + " " + MvbpBottom + " 2"),
                            MvbpPoint(0f, 2f, -1.25f, MvbpInner + " " + MvbpTop + " 1"),
                            MvbpPoint(0f, 2f, -0.5f, MvbpInner + " " + MvbpTop + " " + MvbpCorner),
                            MvbpPoint(1.25f, 2f, -0.5f, MvbpInner + " " + MvbpTop + " 2"),
                            MvbpPoint(-1f, 0f, -1.25f, MvbpOuter + " " + MvbpBottom + " 1"),
                            MvbpPoint(-1f, 0f, 0.5f, MvbpOuter + " " + MvbpBottom + " " + MvbpCorner),
                            MvbpPoint(1.25f, 0f, 0.5f, MvbpOuter + " " + MvbpBottom + " 2"),
                            MvbpPoint(-1f, 2f, -1.25f, MvbpOuter + " " + MvbpTop + " 1"),
                            MvbpPoint(-1f, 2f, 0.5f, MvbpOuter + " " + MvbpTop + " " + MvbpCorner),
                            MvbpPoint(1.25f, 2f, 0.5f, MvbpOuter + " " + MvbpTop + " 2"));
                        break;

                    case "Ashlands_Fortress_Wall_Spikes":
                        AddMvbpSnapPoints(prefab,
                            MvbpPoint(0f, 0f, -2f, MvbpEdge + " 1"),
                            MvbpPoint(0f, 0f, 0f, MvbpCenter),
                            MvbpPoint(0f, 0f, 2f, MvbpEdge + " 2"));
                        break;
                }

                ApplyMvbpComfortFixes(piece, prefabName);

                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogInfo("Applied MoreVanilla compatibility data: " + prefabName);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "MoreVanilla compatibility patch failed for " + prefabName +
                    ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void AddMvbpSnapPoints(object prefab, params CompatSnapPoint[] points)
        {
            if (prefab == null || points == null || points.Length == 0 ||
                _gameObjectType == null || _transformType == null)
                return;

            object prefabTransform = GetPropertyValue(prefab, "transform");
            if (prefabTransform == null)
                return;

            for (int i = 0; i < points.Length; i++)
            {
                CompatSnapPoint point = points[i];
                string objectName = string.IsNullOrEmpty(point.Name)
                    ? "HammerEverything MVBP Snappoint " + (i + 1).ToString(CultureInfo.InvariantCulture)
                    : point.Name;

                object snapPoint = Activator.CreateInstance(_gameObjectType, new object[] { objectName });
                if (snapPoint == null)
                    continue;

                object snapTransform = GetPropertyValue(snapPoint, "transform");
                if (snapTransform == null)
                {
                    DestroyUnityObjectImmediate(snapPoint);
                    continue;
                }

                SetPropertyIfExists(snapTransform, "parent", prefabTransform);
                SetPropertyIfExists(
                    snapTransform,
                    "localPosition",
                    CreateVector3(point.X, point.Y, point.Z));

                try
                {
                    SetPropertyIfExists(snapPoint, "tag", "snappoint");
                }
                catch
                {
                    DestroyUnityObjectImmediate(snapPoint);
                    continue;
                }

                SetGameObjectActive(snapPoint, false);
            }
        }

        private void SetMvbpChildLocalPosition(
            object prefab,
            string childPath,
            float x,
            float y,
            float z)
        {
            object root = GetPropertyValue(prefab, "transform");
            if (root == null)
                return;

            object child = InvokeInstanceWithOptionalTail(root, "Find", childPath);
            if (child != null)
                SetPropertyIfExists(child, "localPosition", CreateVector3(x, y, z));
        }

        private void AddMvbpBoxCollider(
            object gameObject,
            float centerX,
            float centerY,
            float centerZ,
            float sizeX,
            float sizeY,
            float sizeZ)
        {
            ResolveTypes();
            if (gameObject == null || _boxColliderType == null)
                return;

            object collider = AddComponent(gameObject, _boxColliderType);
            if (collider == null)
                return;

            SetPropertyIfExists(collider, "center", CreateVector3(centerX, centerY, centerZ));
            SetPropertyIfExists(collider, "size", CreateVector3(sizeX, sizeY, sizeZ));
        }

        private void SetMvbpSupports(object prefab, bool supports)
        {
            Type wearNTearType = FindLoadedType("WearNTear");
            object wearNTear = GetComponent(prefab, wearNTearType);
            if (wearNTear != null)
                SetFieldIfExists(wearNTear, "m_supports", supports);
        }

        private void AddMvbpChair(object prefab, float x, float y, float z)
        {
            Type chairType = FindLoadedType("Chair");
            if (chairType == null || prefab == null || _gameObjectType == null)
                return;

            object chair = GetComponent(prefab, chairType) ?? AddComponent(prefab, chairType);
            if (chair == null)
                return;

            object attachPoint = Activator.CreateInstance(
                _gameObjectType,
                new object[] { "HammerEverything MVBP attachPoint" });

            object prefabTransform = GetPropertyValue(prefab, "transform");
            object attachTransform = GetPropertyValue(attachPoint, "transform");
            if (attachTransform == null || prefabTransform == null)
            {
                DestroyUnityObjectImmediate(attachPoint);
                return;
            }

            SetPropertyIfExists(attachTransform, "parent", prefabTransform);
            SetPropertyIfExists(attachTransform, "localPosition", CreateVector3(x, y, z));
            SetFieldIfExists(chair, "m_attachPoint", attachTransform);
        }

        private void ApplyMvbpComfortFixes(object piece, string prefabName)
        {
            if (piece == null || string.IsNullOrEmpty(prefabName))
                return;

            switch (prefabName)
            {
                case "dvergrprops_chair":
                case "dvergrprops_stool":
                    SetMvbpEnumField(piece, "m_comfortGroup", "Chair");
                    SetFieldIfExists(piece, "m_comfort", 2);
                    break;

                case "mountainkit_chair":
                    SetMvbpEnumField(piece, "m_comfortGroup", "Chair");
                    SetFieldIfExists(piece, "m_comfort", 1);
                    break;

                case "dvergrprops_bed":
                    SetMvbpEnumField(piece, "m_comfortGroup", "Bed");
                    SetFieldIfExists(piece, "m_comfort", 2);
                    break;

                case "goblin_bed":
                    SetMvbpEnumField(piece, "m_comfortGroup", "Bed");
                    SetFieldIfExists(piece, "m_comfort", 1);
                    break;

                case "ArmorStand_Female":
                case "ArmorStand_Male":
                    SetMvbpEnumField(piece, "m_comfortGroup", "None");
                    SetFieldIfExists(piece, "m_comfort", 2);
                    break;
            }
        }

        private static void SetMvbpEnumField(object instance, string fieldName, string valueName)
        {
            if (instance == null)
                return;

            FieldInfo field = instance.GetType().GetField(fieldName, AnyInstance);
            if (field == null || !field.FieldType.IsEnum)
                return;

            try
            {
                field.SetValue(instance, Enum.Parse(field.FieldType, valueName));
            }
            catch
            {
                // Valheim enum changed; leave vanilla value untouched.
            }
        }

        private void ApplyMoreVanillaPlacementGhostSetup(object player)
        {
            if (player == null)
                return;

            object ghost = GetFieldValue(player, "m_placementGhost");
            if (ghost == null || _mvbpPatchedPlacementGhosts.Contains(ghost))
                return;

            string name = NormalizeInstanceName(GetUnityName(ghost));
            if (string.IsNullOrEmpty(name) || !MvbpPlacementColliderPatchNames.Contains(name))
                return;

            if (TryAddMvbpPlacementBoundsCollider(ghost))
            {
                _mvbpPatchedPlacementGhosts.Add(ghost);

                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogInfo("Applied MoreVanilla placement collider patch: " + name);
            }
        }

        private bool TryAddMvbpPlacementBoundsCollider(object ghost)
        {
            ResolveTypes();
            if (ghost == null || _boxColliderType == null)
                return false;

            if (!TryGetVisualBounds(
                    ghost,
                    out float minX,
                    out float minY,
                    out float minZ,
                    out float maxX,
                    out float maxY,
                    out float maxZ))
            {
                return false;
            }

            object transform = GetPropertyValue(ghost, "transform");
            if (transform == null)
                return false;

            object worldCenter = CreateVector3(
                (minX + maxX) * 0.5f,
                (minY + maxY) * 0.5f,
                (minZ + maxZ) * 0.5f);

            object localCenter = InvokeInstanceWithOptionalTail(
                transform,
                "InverseTransformPoint",
                worldCenter);

            if (localCenter == null)
                return false;

            object collider = AddComponent(ghost, _boxColliderType);
            if (collider == null)
                return false;

            SetPropertyIfExists(collider, "center", localCenter);
            SetPropertyIfExists(
                collider,
                "size",
                CreateVector3(
                    Math.Max(0.01f, maxX - minX),
                    Math.Max(0.01f, maxY - minY),
                    Math.Max(0.01f, maxZ - minZ)));

            return true;
        }

        private void ApplyMoreVanillaPlacementOffset(object player)
        {
            if (player == null)
                return;

            object ghost = GetFieldValue(player, "m_placementGhost");
            if (ghost == null)
                return;

            string name = NormalizeInstanceName(GetUnityName(ghost));
            if (string.IsNullOrEmpty(name) ||
                !MvbpPlacementOffsets.TryGetValue(name, out float[] offset) ||
                offset == null ||
                offset.Length != 3)
            {
                _mvbpPlacementGhostCorrections.Remove(ghost);
                return;
            }

            object transform = GetPropertyValue(ghost, "transform");
            object position = GetPropertyValue(transform, "position");
            if (transform == null || position == null)
                return;

            float x = GetSingleMember(position, "x");
            float y = GetSingleMember(position, "y");
            float z = GetSingleMember(position, "z");

            if (!AreFinite(x, y, z))
                return;

            if (_mvbpPlacementGhostCorrections.TryGetValue(ghost, out float[] previous) &&
                previous != null &&
                previous.Length == 3 &&
                Math.Abs(x - previous[0]) < 0.0005f &&
                Math.Abs(y - previous[1]) < 0.0005f &&
                Math.Abs(z - previous[2]) < 0.0005f)
            {
                return;
            }

            object localOffset = CreateVector3(offset[0], offset[1], offset[2]);
            object rotatedOffset = InvokeInstanceWithOptionalTail(
                transform,
                "TransformDirection",
                localOffset) ?? localOffset;

            float dx = GetSingleMember(rotatedOffset, "x");
            float dy = GetSingleMember(rotatedOffset, "y");
            float dz = GetSingleMember(rotatedOffset, "z");

            if (!AreFinite(dx, dy, dz))
                return;

            float correctedX = x + dx;
            float correctedY = y + dy;
            float correctedZ = z + dz;

            SetPropertyIfExists(
                transform,
                "position",
                CreateVector3(correctedX, correctedY, correctedZ));

            _mvbpPlacementGhostCorrections[ghost] =
                new[] { correctedX, correctedY, correctedZ };

            if (_verboseLogging != null && _verboseLogging.Value)
                Logger.LogInfo("Applied MoreVanilla placement offset: " + name);
        }
    }
}
