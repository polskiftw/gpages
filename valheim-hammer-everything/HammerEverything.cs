using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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
        public const string PluginVersion = "1.4.2";

        private static readonly BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const string CustomBuildCategoryName = "Hammer Everything";
        private static HammerEverything _instance;

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
                { "dvergrprops_lantern", "Dvergr Lantern" },
                { "goblin_bed", "Fuling Bed" }
            };

        private sealed class RecipeIngredient
        {
            public readonly string ItemPrefab;
            public readonly int Amount;

            public RecipeIngredient(string itemPrefab, int amount)
            {
                ItemPrefab = itemPrefab;
                Amount = amount;
            }
        }

        private static RecipeIngredient[] Recipe(params RecipeIngredient[] ingredients)
        {
            return ingredients;
        }

        private static RecipeIngredient Ingredient(string itemPrefab, int amount)
        {
            return new RecipeIngredient(itemPrefab, amount);
        }

        // Hand-authored survival recipes for vanilla props which have no usable
        // developer Piece recipe. Existing non-empty vanilla m_resources arrays
        // always win over this table.
        private static readonly Dictionary<string, RecipeIngredient[]> CuratedRecipes =
            new Dictionary<string, RecipeIngredient[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "barrell", Recipe(Ingredient("Wood", 10), Ingredient("BarrelRings", 1)) },

                { "dvergrprops_barrel", Recipe(Ingredient("FineWood", 8), Ingredient("Copper", 2)) },
                { "dvergrprops_crate", Recipe(Ingredient("FineWood", 6), Ingredient("Copper", 1)) },
                { "dvergrprops_crate_long", Recipe(Ingredient("FineWood", 10), Ingredient("Copper", 2)) },
                { "dvergrprops_crate_ashlands", Recipe(Ingredient("Blackwood", 6), Ingredient("FlametalNew", 1)) },

                { "dvergrprops_bed", Recipe(Ingredient("Wood", 8), Ingredient("Copper", 2)) },
                { "dvergrprops_chair", Recipe(Ingredient("Wood", 4), Ingredient("Copper", 1)) },
                { "dvergrprops_stool", Recipe(Ingredient("Wood", 4), Ingredient("Copper", 1)) },
                { "dvergrprops_table", Recipe(Ingredient("Wood", 6), Ingredient("Copper", 2)) },
                { "dvergrprops_shelf", Recipe(Ingredient("Wood", 4), Ingredient("Copper", 1)) },

                { "dvergrprops_banner", Recipe(Ingredient("JuteBlue", 4), Ingredient("FineWood", 1)) },
                { "dvergrprops_curtain", Recipe(Ingredient("JuteBlue", 4), Ingredient("FineWood", 1)) },
                { "dvergrprops_hooknchain", Recipe(Ingredient("Copper", 2), Ingredient("Chain", 1)) },

                { "dvergrprops_lantern", Recipe(Ingredient("Lantern", 1)) },
                { "dvergrprops_lantern_standing", Recipe(Ingredient("Lantern", 1), Ingredient("Copper", 2)) },

                { "dvergrprops_pickaxe", Recipe(Ingredient("YggdrasilWood", 1), Ingredient("Iron", 2)) },
                { "dvergrprops_wood_beam", Recipe(Ingredient("YggdrasilWood", 4)) },
                { "dvergrprops_wood_pole", Recipe(Ingredient("YggdrasilWood", 4), Ingredient("Copper", 2)) },
                { "dvergrprops_wood_stake", Recipe(Ingredient("YggdrasilWood", 2), Ingredient("Iron", 1)) },
                { "dvergrprops_wood_stakewall", Recipe(Ingredient("YggdrasilWood", 8), Ingredient("Iron", 4)) },
                { "dvergrprops_wood_wall", Recipe(Ingredient("YggdrasilWood", 20), Ingredient("Copper", 10)) },

                // These already carry Piece components in current Valheim. The
                // entries below are only fallbacks if a future game build leaves
                // the component present but strips its serialized recipe.
                { "dvergrprops_wood_floor", Recipe(Ingredient("Wood", 2)) },
                { "dvergrprops_wood_stair", Recipe(Ingredient("Wood", 2)) },

                // Decorative stone statues. These prefabs have no Piece component
                // in vanilla, so there is no developer recipe to preserve.
                { "StatueCorgi", Recipe(Ingredient("Stone", 5)) },
                { "StatueDeer", Recipe(Ingredient("Stone", 5)) },
                { "StatueHare", Recipe(Ingredient("Stone", 5)) },
                { "StatueSeed", Recipe(Ingredient("Stone", 5)) },
                { "StatueEvil", Recipe(Ingredient("Stone", 8)) },
                { "StatueFreya", Recipe(Ingredient("Stone", 20)) },
                { "StatueFreya_broken_left", Recipe(Ingredient("Stone", 10)) },
                { "StatueFreya_broken_right", Recipe(Ingredient("Stone", 10)) },
                { "StatueThor", Recipe(Ingredient("Stone", 20)) },
                { "StatueThor_broken_bottom", Recipe(Ingredient("Stone", 10)) },
                { "StatueThor_broken_top", Recipe(Ingredient("Stone", 10)) },

                // Morkhalla / Deep North location props. These costs deliberately
                // use the biome's normal material vocabulary rather than generic
                // Wood/Stone-only placeholders.
                { "Morkhalla_Banner1", Recipe(Ingredient("ElakingHairBundle", 4), Ingredient("Stone", 2)) },
                { "Morkhalla_Banner2", Recipe(Ingredient("ElakingHairBundle", 4), Ingredient("Stone", 2)) },
                { "Morkhalla_Bedroll1", Recipe(Ingredient("ElakingHairBundle", 4), Ingredient("WolfPelt", 2)) },
                { "Morkhalla_Bedroll2", Recipe(Ingredient("ElakingHairBundle", 4), Ingredient("WolfPelt", 2)) },
                { "Morkhalla_Bench", Recipe(Ingredient("Frostwood", 6), Ingredient("Iron", 1)) },
                { "Morkhalla_bridge_davinci", Recipe(Ingredient("Frostwood", 24), Ingredient("FlametalNew", 2), Ingredient("Gold", 2)) },
                { "Morkhalla_Chain", Recipe(Ingredient("Iron", 4)) },

                // Fallback only; current vanilla already has a real Ancient Chest
                // recipe, which ConfigureCraftingRecipe preserves before consulting
                // this table.
                { "Morkhalla_ChestAncient", Recipe(Ingredient("Wood", 10), Ingredient("Tar", 2), Ingredient("BlackMetal", 6)) },

                { "Morkhalla_coal_pile_memorial", Recipe(Ingredient("Coal", 10), Ingredient("Stone", 4)) },
                { "Morkhalla_Floor_2x2", Recipe(Ingredient("Stone", 4)) },
                { "Morkhalla_Floor_4x4", Recipe(Ingredient("Stone", 8)) },
                { "Morkhalla_Floor_4x4_broken01", Recipe(Ingredient("Stone", 4)) },
                { "Morkhalla_Floor_4x4_broken02", Recipe(Ingredient("Stone", 4)) },

                { "Morkhalla_GateDoor", Recipe(Ingredient("Frostwood", 12), Ingredient("Iron", 4)) },
                { "Morkhalla_GateDoor02", Recipe(Ingredient("Frostwood", 12), Ingredient("Iron", 4)) },
                { "Morkhalla_GateDoor03", Recipe(Ingredient("Stone", 12), Ingredient("Iron", 4)) },
                { "Morkhalla_jotun_gate", Recipe(Ingredient("Stone", 24), Ingredient("Gold", 4)) },
                { "Morkborg_gate", Recipe(Ingredient("Stone", 24), Ingredient("Gold", 4)) },

                { "Morkhalla_giant_railing", Recipe(Ingredient("Stone", 6), Ingredient("Iron", 2)) },
                { "Morkhalla_giant_railing_corner", Recipe(Ingredient("Stone", 6), Ingredient("Iron", 2)) },
                { "Morkhalla_giant_railing_deco", Recipe(Ingredient("Stone", 4), Ingredient("Iron", 1)) },
                { "Morkhalla_giant_railing_half", Recipe(Ingredient("Stone", 3), Ingredient("Iron", 1)) },
                { "Morkhalla_giant_railing_single", Recipe(Ingredient("Stone", 2), Ingredient("Iron", 1)) },
                { "Morkhalla_giant_railing_torch", Recipe(Ingredient("Stone", 3), Ingredient("Iron", 1), Ingredient("FrostCore", 1)) },
                { "Morkhalla_giant_railing_torch_unlit", Recipe(Ingredient("Stone", 3), Ingredient("Iron", 1)) },

                { "Morkhalla_rubble_trashpile", Recipe(Ingredient("Stone", 4), Ingredient("Frostwood", 4)) },

                { "Morkhalla_Rug_corner", Recipe(Ingredient("ElakingHairBundle", 3)) },
                { "Morkhalla_Rug_end1", Recipe(Ingredient("ElakingHairBundle", 2)) },
                { "Morkhalla_Rug_end2", Recipe(Ingredient("ElakingHairBundle", 2)) },
                { "Morkhalla_Rug_middle", Recipe(Ingredient("ElakingHairBundle", 4)) },
                { "Morkhalla_Rug_stair", Recipe(Ingredient("ElakingHairBundle", 4)) },

                { "Morkhalla_Stairs_giant_railing", Recipe(Ingredient("Stone", 8), Ingredient("Iron", 2)) },
                { "Morkhalla_Stairs_giant_short", Recipe(Ingredient("Stone", 8)) },
                { "Morkhalla_Stairs_giant_short_broken1", Recipe(Ingredient("Stone", 4)) },
                { "Morkhalla_Stairs_giant_short_broken2", Recipe(Ingredient("Stone", 4)) },

                { "Morkhalla_StatuePieceArmL", Recipe(Ingredient("Stone", 6)) },
                { "Morkhalla_StatuePieceArmR", Recipe(Ingredient("Stone", 6)) },
                { "Morkhalla_StatuePieceFace", Recipe(Ingredient("Stone", 6)) },
                { "Morkhalla_StatuePieceFeet", Recipe(Ingredient("Stone", 6)) },
                { "Morkhalla_StatuePieceHorn", Recipe(Ingredient("Stone", 6)) },
                { "Morkhalla_StatuePieceHorn2", Recipe(Ingredient("Stone", 6)) },
                { "Morkhalla_StatuePieceLegs", Recipe(Ingredient("Stone", 6)) },
                { "Morkhalla_StatuePieceSword", Recipe(Ingredient("Stone", 6)) },
                { "Morkhalla_StatuePieceTorso", Recipe(Ingredient("Stone", 6)) },

                { "Morkhalla_StatuePieceArmL_big", Recipe(Ingredient("Stone", 16)) },
                { "Morkhalla_StatuePieceArmR_big", Recipe(Ingredient("Stone", 16)) },
                { "Morkhalla_StatuePieceFace_big", Recipe(Ingredient("Stone", 16)) },
                { "Morkhalla_StatuePieceFeet_big", Recipe(Ingredient("Stone", 16)) },
                { "Morkhalla_StatuePieceHorn_big", Recipe(Ingredient("Stone", 16)) },
                { "Morkhalla_StatuePieceHorn2_big", Recipe(Ingredient("Stone", 16)) },
                { "Morkhalla_StatuePieceLegs_big", Recipe(Ingredient("Stone", 16)) },
                { "Morkhalla_StatuePieceSword_big", Recipe(Ingredient("Stone", 16)) },
                { "Morkhalla_StatuePieceTorso_big", Recipe(Ingredient("Stone", 16)) },

                { "Morkhalla_StatueSword", Recipe(Ingredient("Stone", 20)) },
                { "Morkhalla_StatueSword_hanging", Recipe(Ingredient("Stone", 12), Ingredient("Iron", 2)) },

                // Current vanilla already has a Black Marble Pile recipe. This is
                // a future-proof fallback only.
                { "Morkhalla_Stonepile", Recipe(Ingredient("BlackMarble", 50)) },

                { "Morkhalla_Stool", Recipe(Ingredient("Frostwood", 3), Ingredient("Iron", 1)) },
                { "Morkhalla_Table", Recipe(Ingredient("Frostwood", 6), Ingredient("Iron", 2)) },
                { "Morkhalla_WallChain1", Recipe(Ingredient("Stone", 8), Ingredient("Iron", 4)) },

                // Exhaustive 1.4 fallback coverage for the current safe hidden
                // candidate set. Real non-empty vanilla m_resources still win.
                { "ancient_skull", Recipe(Ingredient("BlackMarble", 100)) },
                { "ArmorStand_Female", Recipe(Ingredient("FineWood", 8), Ingredient("BronzeNails", 2), Ingredient("Tar", 4)) },
                { "ArmorStand_Male", Recipe(Ingredient("FineWood", 8), Ingredient("BronzeNails", 2), Ingredient("Tar", 4)) },
                { "ashland_pot1_green", Recipe(Ingredient("CeramicPlate", 2)) },
                { "ashland_pot1_red", Recipe(Ingredient("CeramicPlate", 2)) },
                { "ashland_pot2_green", Recipe(Ingredient("CeramicPlate", 2)) },
                { "ashland_pot2_red", Recipe(Ingredient("CeramicPlate", 2)) },
                { "ashland_pot3_green", Recipe(Ingredient("CeramicPlate", 2)) },
                { "ashland_pot3_red", Recipe(Ingredient("CeramicPlate", 2)) },
                { "Ashland_Stair", Recipe(Ingredient("Grausten", 32)) },
                { "Ashland_Steepstair", Recipe(Ingredient("Grausten", 32)) },
                { "Ashlands_Arch1", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Arch2", Recipe(Ingredient("Grausten", 10)) },
                { "Ashlands_Arch2_Broken1", Recipe(Ingredient("Grausten", 5)) },
                { "Ashlands_Arch2_Broken2", Recipe(Ingredient("Grausten", 5)) },
                { "Ashlands_ArchRoof", Recipe(Ingredient("Grausten", 12)) },
                { "Ashlands_ArchRoofDamaged", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_ArchRoofDamaged_half1", Recipe(Ingredient("Grausten", 4)) },
                { "Ashlands_ArchRoofDamaged_half2", Recipe(Ingredient("Grausten", 4)) },
                { "Ashlands_ArchRoofLong_Damaged", Recipe(Ingredient("Grausten", 12)) },
                { "Ashlands_Boss_Pillar", Recipe(Ingredient("Grausten", 20)) },
                { "Ashlands_Boss_Pillar_Twist_broken1", Recipe(Ingredient("Grausten", 7)) },
                { "Ashlands_Boss_Pillar_Twist_broken2", Recipe(Ingredient("Grausten", 7)) },
                { "Ashlands_Boss_Pillar_Twist_broken3", Recipe(Ingredient("Grausten", 7)) },
                { "Ashlands_Floor", Recipe(Ingredient("Grausten", 4)) },
                { "Ashlands_floor_large", Recipe(Ingredient("Grausten", 16)) },
                { "Ashlands_Fortress_Floor", Recipe(Ingredient("Grausten", 6)) },
                { "Ashlands_Fortress_Gate", Recipe(Ingredient("Grausten", 32)) },
                { "Ashlands_Fortress_Gate_Door", Recipe(Ingredient("Copper", 35)) },
                { "Ashlands_Fortress_Wall_Pillar", Recipe(Ingredient("Grausten", 12)) },
                { "Ashlands_Fortress_Wall_Pillar_base", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Fortress_Wall_PillarTop", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Fortress_Wall_PillarTopStone", Recipe(Ingredient("Grausten", 6)) },
                { "Ashlands_Fortress_Wall_Spikes", Recipe(Ingredient("Copper", 6)) },
                { "Ashlands_Pillar4", Recipe(Ingredient("Grausten", 10)) },
                { "Ashlands_Pillar4_tip", Recipe(Ingredient("Grausten", 10)) },
                { "Ashlands_Pillar4_tip_broken1", Recipe(Ingredient("Grausten", 5)) },
                { "Ashlands_Pillar4_tip_broken2", Recipe(Ingredient("Grausten", 5)) },
                { "Ashlands_Pillar4_tip2", Recipe(Ingredient("Grausten", 10)) },
                { "Ashlands_Pillar4_tip2_broken1", Recipe(Ingredient("Grausten", 5)) },
                { "Ashlands_Pillar4_tip2_broken2", Recipe(Ingredient("Grausten", 5)) },
                { "Ashlands_Pillar4_tip3", Recipe(Ingredient("Grausten", 10)) },
                { "Ashlands_Pillar4_tip3_broken1", Recipe(Ingredient("Grausten", 4)) },
                { "Ashlands_Pillar4_tip3_broken2", Recipe(Ingredient("Grausten", 4)) },
                { "Ashlands_Pillar4_tip3_broken3", Recipe(Ingredient("Grausten", 4)) },
                { "Ashlands_PillarBase3_double", Recipe(Ingredient("Grausten", 3)) },
                { "Ashlands_Ruins_Floor_1point5x1point5", Recipe(Ingredient("Grausten", 4)) },
                { "Ashlands_Ruins_Floor_1point5x1point5_broken", Recipe(Ingredient("Grausten", 2)) },
                { "Ashlands_Ruins_Floor_3x3", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Floor_3x3_broken1", Recipe(Ingredient("Grausten", 3)) },
                { "Ashlands_Ruins_Floor_3x3_broken2", Recipe(Ingredient("Grausten", 3)) },
                { "Ashlands_Ruins_Floor_3x3_broken3", Recipe(Ingredient("Grausten", 3)) },
                { "Ashlands_Ruins_Floor_6x6", Recipe(Ingredient("Grausten", 24)) },
                { "Ashlands_Ruins_Floor_6x6_broken1", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Floor_6x6_broken2", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_twist_ArchBig", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_twist_PillarBase", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_twist_PillarBaseSmall", Recipe(Ingredient("Grausten", 3)) },
                { "Ashlands_Ruins_Wall_4x6", Recipe(Ingredient("Grausten", 16)) },
                { "Ashlands_Ruins_Wall_Broken3_4x6", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Wall_Broken4_4x6", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Wall_Broken5_4x6", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Wall_Top_wHole", Recipe(Ingredient("Grausten", 16)) },
                { "Ashlands_Ruins_Wall_Window_4x6_broken2", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Wall_Window_4x6_broken3", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Wall_Window_4x6_broken4", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Wall_Window_4x6_broken5", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Wall_Window_4x6_broken6", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Ruins_Wall_Windows_Broken_4x6", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_StairsBroad", Recipe(Ingredient("Grausten", 24)) },
                { "Ashlands_Wall_2x2", Recipe(Ingredient("Grausten", 6)) },
                { "Ashlands_Wall_2x2_cornerL", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Wall_2x2_cornerL_top", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Wall_2x2_cornerR", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Wall_2x2_cornerR_top", Recipe(Ingredient("Grausten", 8)) },
                { "Ashlands_Wall_2x2_edge", Recipe(Ingredient("Grausten", 6)) },
                { "Ashlands_Wall_2x2_edge_top", Recipe(Ingredient("Grausten", 6)) },
                { "Ashlands_Wall_2x2_edge2", Recipe(Ingredient("Grausten", 6)) },
                { "Ashlands_Wall_2x2_edge2_top", Recipe(Ingredient("Grausten", 6)) },
                { "Ashlands_Wall_2x2_top", Recipe(Ingredient("Grausten", 6)) },
                { "Ashlands_WallBlock", Recipe(Ingredient("Grausten", 4)) },
                { "Ashlands_WallBlock_1x2x2", Recipe(Ingredient("Grausten", 2)) },
                { "Ashlands_WallBlock_base", Recipe(Ingredient("Grausten", 3)) },
                { "ashwood_arch_bottom", Recipe(Ingredient("Blackwood", 1)) },
                { "ashwood_arch_top", Recipe(Ingredient("Blackwood", 1)) },
                { "ashwood_wall_beam_26_alt", Recipe(Ingredient("Blackwood", 1)) },
                { "ashwood_wall_beam_45_alt", Recipe(Ingredient("Blackwood", 1)) },
                { "ashwood_wall_cross_26_alt", Recipe(Ingredient("Blackwood", 1)) },
                { "ashwood_wall_cross_45_alt", Recipe(Ingredient("Blackwood", 1)) },
                { "bar_ancientmetal_stack", Recipe(Ingredient("Flametal", 30)) },
                { "blackmarble_column_3", Recipe(Ingredient("BlackMarble", 16)) },
                { "blackmarble_creep_stair", Recipe(Ingredient("BlackMarble", 8)) },
                { "blackmarble_floor_large", Recipe(Ingredient("BlackMarble", 32)) },
                { "blackmarble_stair_corner", Recipe(Ingredient("BlackMarble", 8)) },
                { "blackmarble_stair_corner_left", Recipe(Ingredient("BlackMarble", 8)) },
                { "blackmarble_tile_floor_1x1", Recipe(Ingredient("BlackMarble", 2)) },
                { "blackmarble_tile_floor_2x2", Recipe(Ingredient("BlackMarble", 2)) },
                { "blackmarble_tile_wall_1x1", Recipe(Ingredient("BlackMarble", 1)) },
                { "blackmarble_tile_wall_2x2", Recipe(Ingredient("BlackMarble", 2)) },
                { "blackmarble_tile_wall_2x4", Recipe(Ingredient("BlackMarble", 4)) },
                { "BossStone_Bonemass", Recipe(Ingredient("Stone", 20), Ingredient("TrophyBonemass", 1)) },
                { "bucket", Recipe(Ingredient("Wood", 2)) },
                { "Candle_resin_bogwitch", Recipe(Ingredient("Resin", 1), Ingredient("CandleWick", 1)) },
                { "CastleKit_braided_box01", Recipe(Ingredient("Wood", 2)) },
                { "CastleKit_brazier", Recipe(Ingredient("Bronze", 5), Ingredient("Coal", 2), Ingredient("Chain", 1)) },
                { "CastleKit_groundtorch", Recipe(Ingredient("Iron", 2), Ingredient("Resin", 2), Ingredient("SurtlingCore", 1)) },
                { "CastleKit_groundtorch_blue", Recipe(Ingredient("Iron", 2), Ingredient("GreydwarfEye", 2), Ingredient("SurtlingCore", 1)) },
                { "CastleKit_groundtorch_green", Recipe(Ingredient("Iron", 2), Ingredient("Guck", 2), Ingredient("SurtlingCore", 1)) },
                { "CastleKit_groundtorch_unlit", Recipe(Ingredient("Iron", 2)) },
                { "CastleKit_metal_groundtorch_unlit", Recipe(Ingredient("Iron", 2)) },
                { "CastleKit_pot03", Recipe(Ingredient("Bronze", 2)) },
                { "caverock_ice_pillar_wall", Recipe(Ingredient("Crystal", 10)) },
                { "CharredBanner1", Recipe(Ingredient("Blackwood", 2), Ingredient("CharredBone", 4)) },
                { "CharredBanner2", Recipe(Ingredient("Blackwood", 2), Ingredient("CharredBone", 4)) },
                { "CharredBanner3", Recipe(Ingredient("Blackwood", 2), Ingredient("CharredBone", 4)) },
                { "Chest", Recipe(Ingredient("Wood", 10), Ingredient("Iron", 1)) },
                { "cliff_ashlands3_Arch_1", Recipe(Ingredient("Grausten", 20)) },
                { "cliff_ashlands7_HalfArch", Recipe(Ingredient("Grausten", 10)) },
                { "cloth_hanging_door", Recipe(Ingredient("JuteRed", 4), Ingredient("FineWood", 1)) },
                { "cloth_hanging_door_double", Recipe(Ingredient("JuteRed", 4)) },
                { "cloth_hanging_long", Recipe(Ingredient("JuteRed", 8), Ingredient("FineWood", 2)) },
                { "CreepProp_wall01", Recipe(Ingredient("YggdrasilWood", 2)) },
                { "crypt_skeleton_chest", Recipe(Ingredient("Wood", 10), Ingredient("BoneFragments", 10)) },
                { "deepnorth_lantern_standing", Recipe(Ingredient("Frostwood", 3), Ingredient("GlowWorm", 1), Ingredient("Iron", 1)) },
                { "dungeon_forestcrypt_door", Recipe(Ingredient("Stone", 8), Ingredient("Bronze", 2)) },
                { "dungeon_queen_door", Recipe(Ingredient("BlackMarble", 40), Ingredient("DvergrKeyFragment", 4), Ingredient("Iron", 12)) },
                { "dungeon_sunkencrypt_irongate", Recipe(Ingredient("Iron", 4)) },
                { "dungeon_sunkencrypt_irongate_rusty", Recipe(Ingredient("Iron", 4)) },
                { "dvergrtown_arch", Recipe(Ingredient("BlackMarble", 8)) },
                { "dvergrtown_creep_door", Recipe(Ingredient("YggdrasilWood", 4)) },
                { "dvergrtown_secretdoor", Recipe(Ingredient("BlackMarble", 12), Ingredient("Eitr", 2)) },
                { "dvergrtown_slidingdoor", Recipe(Ingredient("BlackMarble", 32), Ingredient("Copper", 8)) },
                { "dvergrtown_stair_corner_wood_left", Recipe(Ingredient("YggdrasilWood", 5), Ingredient("CopperScrap", 2)) },
                { "dvergrtown_wood_beam", Recipe(Ingredient("YggdrasilWood", 4)) },
                { "dvergrtown_wood_pole", Recipe(Ingredient("YggdrasilWood", 4), Ingredient("Copper", 2)) },
                { "dvergrtown_wood_stake", Recipe(Ingredient("YggdrasilWood", 1)) },
                { "dvergrtown_wood_stakewall", Recipe(Ingredient("YggdrasilWood", 4)) },
                { "dvergrtown_wood_wall01", Recipe(Ingredient("YggdrasilWood", 20)) },
                { "dvergrtown_wood_wall02", Recipe(Ingredient("YggdrasilWood", 12)) },
                { "dvergrtown_wood_wall03", Recipe(Ingredient("YggdrasilWood", 10), Ingredient("IronNails", 6)) },
                { "elaking_trashpile", Recipe(Ingredient("Frostwood", 4), Ingredient("ElakingHairBundle", 2)) },
                { "fenrirhide_hanging_door", Recipe(Ingredient("WolfHairBundle", 2)) },
                { "FirTree_Big_plantable_Stub", Recipe(Ingredient("Frostwood", 4)) },
                { "GhostSkull", Recipe(Ingredient("BoneFragments", 10), Ingredient("GreydwarfEye", 2)) },
                { "giant_skull", Recipe(Ingredient("BlackMarble", 32)) },
                { "goblin_banner", Recipe(Ingredient("Wood", 1), Ingredient("DeerHide", 1), Ingredient("Tar", 1)) },
                { "goblin_bed", Recipe(Ingredient("Wood", 8), Ingredient("Tar", 1)) },
                { "goblin_fence", Recipe(Ingredient("Wood", 2), Ingredient("Tar", 1)) },
                { "goblin_pole", Recipe(Ingredient("Wood", 1), Ingredient("Tar", 1)) },
                { "goblin_pole_small", Recipe(Ingredient("Wood", 1), Ingredient("Tar", 1)) },
                { "goblin_roof_45d", Recipe(Ingredient("Wood", 1), Ingredient("DeerHide", 1), Ingredient("Tar", 1)) },
                { "goblin_roof_45d_corner", Recipe(Ingredient("Wood", 1), Ingredient("DeerHide", 1), Ingredient("Tar", 1)) },
                { "goblin_roof_cap", Recipe(Ingredient("Wood", 4), Ingredient("DeerHide", 4), Ingredient("Tar", 1)) },
                { "goblin_stairs", Recipe(Ingredient("Wood", 2), Ingredient("Tar", 1)) },
                { "goblin_stepladder", Recipe(Ingredient("Wood", 2), Ingredient("Tar", 1)) },
                { "goblin_strawpile", Recipe(Ingredient("Wood", 4)) },
                { "goblin_trashpile", Recipe(Ingredient("Wood", 2), Ingredient("BoneFragments", 4), Ingredient("Tar", 1)) },
                { "goblin_woodwall_1m", Recipe(Ingredient("Wood", 1), Ingredient("Tar", 1)) },
                { "goblin_woodwall_2m", Recipe(Ingredient("Wood", 2), Ingredient("Tar", 1)) },
                { "goblin_woodwall_2m_ribs", Recipe(Ingredient("BoneFragments", 6), Ingredient("Tar", 1)) },
                { "GuckSack", Recipe(Ingredient("Guck", 12)) },
                { "GuckSack_small", Recipe(Ingredient("Guck", 6)) },
                { "HoleRock_rootFloor1", Recipe(Ingredient("ElderBark", 8)) },
                { "HoleRock_rootWall1", Recipe(Ingredient("ElderBark", 8)) },
                { "Ice_floor", Recipe(Ingredient("Crystal", 16)) },
                { "IceShelf_01", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_02", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_03", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_04", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_05", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_06", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_07", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_08", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_09", Recipe(Ingredient("Ice", 20)) },
                { "IceShelf_10", Recipe(Ingredient("Ice", 20)) },
                { "IceWall", Recipe(Ingredient("Ice", 8)) },
                { "iron_floor_1x1", Recipe(Ingredient("Iron", 1)) },
                { "iron_wall_1x1_rusty", Recipe(Ingredient("Iron", 1)) },
                { "LargeBone", Recipe(Ingredient("BoneFragments", 30)) },
                { "LargeBone_half01", Recipe(Ingredient("BoneFragments", 15)) },
                { "LargeBone_half02", Recipe(Ingredient("BoneFragments", 15)) },
                { "morgenhole_pile", Recipe(Ingredient("Stone", 4), Ingredient("MorgenSinew", 2)) },
                { "Morkhalla_Drawbridge", Recipe(Ingredient("Frostwood", 24), Ingredient("FlametalNew", 2), Ingredient("Gold", 2)) },
                { "MountainKit_brazier", Recipe(Ingredient("Bronze", 5), Ingredient("Coal", 2), Ingredient("BlackCore", 1), Ingredient("WolfClaw", 3)) },
                { "MountainKit_brazier_blue", Recipe(Ingredient("Bronze", 5), Ingredient("GreydwarfEye", 2), Ingredient("BlackCore", 1), Ingredient("WolfClaw", 3)) },
                { "MountainKit_brazier_purple", Recipe(Ingredient("Bronze", 5), Ingredient("CharcoalResin", 2), Ingredient("BlackCore", 1), Ingredient("WolfClaw", 3)) },
                { "mountainkit_chair", Recipe(Ingredient("FineWood", 4)) },
                { "mountainkit_table", Recipe(Ingredient("FineWood", 10), Ingredient("IronNails", 20)) },
                { "MountainKit_wood_gate", Recipe(Ingredient("Wood", 20), Ingredient("Iron", 4)) },
                { "mudpile", Recipe(Ingredient("IronScrap", 10)) },
                { "mudpile_beacon", Recipe(Ingredient("IronScrap", 10)) },
                { "mudpile2", Recipe(Ingredient("IronScrap", 10)) },
                { "obsidian_pile", Recipe(Ingredient("Obsidian", 10)) },
                { "piece_blackwood_bench", Recipe(Ingredient("Blackwood", 6)) },
                { "piece_dvergr_pole", Recipe(Ingredient("YggdrasilWood", 1)) },
                { "piece_dvergr_wood_door", Recipe(Ingredient("YggdrasilWood", 16), Ingredient("IronNails", 24)) },
                { "piece_dvergr_wood_wall", Recipe(Ingredient("YggdrasilWood", 5)) },
                { "piece_pot1_cracked", Recipe(Ingredient("CeramicPlate", 2)) },
                { "piece_pot1_red", Recipe(Ingredient("CeramicPlate", 4)) },
                { "piece_pot2_cracked", Recipe(Ingredient("CeramicPlate", 2)) },
                { "piece_pot2_red", Recipe(Ingredient("CeramicPlate", 4)) },
                { "piece_pot3_cracked", Recipe(Ingredient("CeramicPlate", 2)) },
                { "piece_pot3_red", Recipe(Ingredient("CeramicPlate", 4)) },
                { "prop_ashwood_bed", Recipe(Ingredient("Blackwood", 8), Ingredient("LoxPelt", 2), Ingredient("AskHide", 2)) },
                { "prop_bed02", Recipe(Ingredient("FineWood", 40), Ingredient("DeerHide", 7), Ingredient("WolfPelt", 4), Ingredient("Feathers", 10), Ingredient("IronNails", 15)) },
                { "prop_cauldron_ext3_butchertable", Recipe(Ingredient("ElderBark", 2), Ingredient("RoundLog", 4), Ingredient("FineWood", 4), Ingredient("Silver", 2)) },
                { "prop_chest_warderobe", Recipe(Ingredient("FineWood", 10), Ingredient("IronNails", 10)) },
                { "prop_piece_bench_runed", Recipe(Ingredient("Frostwood", 6), Ingredient("MooseHide", 1)) },
                { "prop_piece_brazierfloor01", Recipe(Ingredient("Bronze", 5), Ingredient("Coal", 2), Ingredient("WolfClaw", 3)) },
                { "prop_piece_chair03", Recipe(Ingredient("FineWood", 4), Ingredient("Tar", 1), Ingredient("IronNails", 5), Ingredient("DeerHide", 1)) },
                { "prop_piece_workbench_ext1", Recipe(Ingredient("Wood", 10), Ingredient("Flint", 10)) },
                { "prop_piece_workbench_ext2", Recipe(Ingredient("Wood", 10), Ingredient("Flint", 15), Ingredient("LeatherScraps", 20), Ingredient("DeerHide", 5)) },
                { "prop_piece_workbench_ext3", Recipe(Ingredient("FineWood", 10), Ingredient("Bronze", 3)) },
                { "prop_piece_workbench_ext4", Recipe(Ingredient("Iron", 4), Ingredient("FineWood", 10), Ingredient("Obsidian", 4)) },
                { "prop_preptable", Recipe(Ingredient("Iron", 5), Ingredient("FineWood", 20), Ingredient("LeatherScraps", 15)) },
                { "prop_wood_stack", Recipe(Ingredient("Wood", 50)) },
                { "rug_bogwitch_deer", Recipe(Ingredient("DeerHide", 4)) },
                { "rug_bogwitch_fur", Recipe(Ingredient("LoxPelt", 4)) },
                { "rug_bogwitch_wolf", Recipe(Ingredient("WolfPelt", 4)) },
                { "shipwreck_karve_chest", Recipe(Ingredient("Wood", 10), Ingredient("BronzeNails", 2)) },
                { "shipwreck_vikingship_chest", Recipe(Ingredient("Wood", 10), Ingredient("BronzeNails", 2)) },
                { "siege_wall_1x1", Recipe(Ingredient("Grausten", 2)) },
                { "sign_notext", Recipe(Ingredient("Wood", 1)) },
                { "Skull1", Recipe(Ingredient("TrophySkeleton", 1)) },
                { "Skull2", Recipe(Ingredient("TrophySkeleton", 4)) },
                { "StartPlatform", Recipe(Ingredient("Stone", 20), Ingredient("Wood", 10)) },
                { "stone_floor", Recipe(Ingredient("Stone", 16)) },
                { "stone_wall_1x1_ruin", Recipe(Ingredient("Stone", 4)) },
                { "stone_wall_2x1_ruin", Recipe(Ingredient("Stone", 8)) },
                { "stone_wall_ruin", Recipe(Ingredient("Stone", 8)) },
                { "stone_wall_ruin_2", Recipe(Ingredient("Stone", 8)) },
                { "stonechest", Recipe(Ingredient("Stone", 10), Ingredient("Iron", 2)) },
                { "stonewall_2", Recipe(Ingredient("Stone", 8)) },
                { "stonewall_3", Recipe(Ingredient("Stone", 8)) },
                { "sunken_crypt_gate", Recipe(Ingredient("Iron", 4)) },
                { "SunkenKit_int_towerwall_LOD", Recipe(Ingredient("Stone", 16)) },
                { "trader_wagon_destructable", Recipe(Ingredient("FineWood", 32)) },
                { "turf_roof", Recipe(Ingredient("Wood", 2)) },
                { "turf_roof_top", Recipe(Ingredient("Wood", 2)) },
                { "turf_roof_wall", Recipe(Ingredient("Wood", 2)) },
                { "UnstableLavaRock", Recipe(Ingredient("Grausten", 8), Ingredient("FlametalNew", 1)) },
                { "veg_skull_Ashlands", Recipe(Ingredient("CharredBone", 20)) },
                { "volture_strawpile", Recipe(Ingredient("Barley", 4), Ingredient("Flax", 4)) },
                { "WallSnow1", Recipe(Ingredient("Snowball", 20)) },
                { "WallSnow2", Recipe(Ingredient("Snowball", 20)) },
                { "WallSnow3", Recipe(Ingredient("Snowball", 20)) },
                { "WallSnow4", Recipe(Ingredient("Snowball", 20)) },
                { "wood_pole_log_4_worn", Recipe(Ingredient("RoundLog", 2)) },
                { "wood_wall_roof", Recipe(Ingredient("Wood", 2)) },
            };

        private readonly Stopwatch _pollTimer = Stopwatch.StartNew();
        private readonly Stopwatch _removalTimer = Stopwatch.StartNew();

        private readonly HashSet<string> _managedPrefabNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pieceAddedByUs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _managedPrefabObjects =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Type, object> _emptyDropTables =
            new Dictionary<Type, object>();
        private readonly HashSet<string> _unpricedPrefabNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private const int IconLayer = 31;
        private const int IconSize = 128;
        private const float IconFieldOfView = 0.5f;

        private sealed class IconRenderRequest
        {
            public readonly string PrefabName;
            public readonly object Prefab;
            public readonly object Piece;

            public IconRenderRequest(string prefabName, object prefab, object piece)
            {
                PrefabName = prefabName;
                Prefab = prefab;
                Piece = piece;
            }
        }

        private readonly Queue<IconRenderRequest> _iconRenderQueue =
            new Queue<IconRenderRequest>();
        private readonly HashSet<string> _iconQueued =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _generatedIcons =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<object, int> _customTagIds =
            new Dictionary<object, int>();

        private Harmony _harmony;
        private bool _categoryPatchesInstalled;
        private int _customCategoryId = -1;
        private Type _pieceCategoryType;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _automaticPropScan;
        private ConfigEntry<bool> _includeStructures;
        private ConfigEntry<bool> _useCraftingCosts;
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
        private Type _pieceTableType;
        private Type _byUsagePieceListType;
        private Type _dropOnDestroyedType;

        private Type _unityObjectType;
        private Type _gameObjectType;
        private Type _componentType;
        private Type _transformType;
        private Type _rendererType;
        private Type _meshRendererType;
        private Type _skinnedMeshRendererType;
        private Type _meshFilterType;
        private Type _cameraType;
        private Type _lightComponentType;
        private Type _renderTextureType;
        private Type _texture2DType;
        private Type _spriteType;
        private Type _rectType;
        private Type _vector2Type;
        private Type _vector3Type;
        private Type _quaternionType;
        private Type _colorType;
        private Type _textureFormatType;
        private Type _cameraClearFlagsType;
        private Type _lightKindType;
        private Type _applicationType;
        private Type _systemInfoType;

        private bool? _graphicsAvailable;

        private object _lastScene;
        private object _lastHammerPieceTable;
        private bool _playerRefreshed;

        private void Awake()
        {
            _instance = this;

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

            _useCraftingCosts = Config.Bind(
                "General",
                "UseCraftingCosts",
                true,
                "Use curated survival crafting costs for recipe-less hidden props while preserving developer-authored vanilla recipes. Disable for free building. The old AlwaysAvailable setting from pre-1.3 configs is intentionally ignored.");

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
            _useCraftingCosts.SettingChanged += OnConfigChanged;
            _allowDungeonPlacement.SettingChanged += OnConfigChanged;
            _allowOverlap.SettingChanged += OnConfigChanged;
            _extraPrefabNames.SettingChanged += OnConfigChanged;
            _blockedPrefabNames.SettingChanged += OnConfigChanged;

            ResolveTypes();
            EnsureCategoryPatches();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Waiting for Valheim's prefab database.");
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch
            {
                // BepInEx is unloading; nothing useful to recover here.
            }

            if (ReferenceEquals(_instance, this))
                _instance = null;
        }

        private void Update()
        {
            EnsureCategoryPatches();
            ProcessIconRenderQueue();

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
            EnsureCategoryPatches();

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
            _unpricedPrefabNames.Clear();
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

            if (_useCraftingCosts.Value && _unpricedPrefabNames.Count > 0)
            {
                Logger.LogWarning(
                    $"{_unpricedPrefabNames.Count} added prefab(s) had neither a developer recipe nor a curated 1.4 recipe. " +
                    $"Their original resource state was left untouched rather than inventing a generic cost. Missing: {FormatUnpricedPrefabNames()}");
            }
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

            ConfigurePiece(prefab, piece, templatePiece, name, addedComponent);

            // Some hidden vanilla prefabs already carry Piece but have an empty
            // m_name (goblin_bed is the current example). Never allow a blank
            // build-menu entry: preserve real names, synthesize a friendly name
            // only when vanilla left it empty, and skip the prefab if naming fails.
            if (!EnsurePieceDisplayName(piece, name))
            {
                Logger.LogWarning($"Skipped Hammer prefab with no usable display name: {name}");
                return false;
            }

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

        private void ConfigurePiece(object prefab, object piece, object templatePiece, string prefabName, bool newlyAddedPiece)
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
                // Keep a reliable vanilla icon until the prefab thumbnail is ready.
                CopyFieldIfTargetEmpty(templatePiece, piece, "m_icon");
                CopyFieldIfTargetZero(templatePiece, piece, "m_category");
                CopyFieldIfTargetZero(templatePiece, piece, "m_usage");
            }

            // Valheim 1.0's Hammer menu is usage-tag driven. Give every piece
            // added by this plugin one private category and no vanilla usage tags,
            // so it appears under Hammer Everything (plus Show All) instead of
            // being scattered through Building/Furniture/Decor/etc.
            if (_categoryPatchesInstalled)
                AssignCustomBuildCategory(piece);

            QueueUniqueIcon(prefabName, prefab, piece);

            if (_useCraftingCosts.Value)
            {
                ConfigureCraftingRecipe(prefabName, piece);
            }
            else
            {
                ClearArrayField(piece, "m_resources");
                SetFieldIfExists(piece, "m_craftingStation", null);
                SetFieldIfExists(piece, "m_requiredGlobalKey", "");
                ClearStringCollectionField(piece, "m_requiredGlobalKeys");
            }
        }




        private void ConfigureCraftingRecipe(string prefabName, object piece)
        {
            if (piece == null)
                return;

            if (HasNonEmptyArrayField(piece, "m_resources"))
            {
                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogInfo($"Preserved developer recipe: {prefabName}");
                return;
            }

            if (!CuratedRecipes.TryGetValue(prefabName, out RecipeIngredient[] recipe))
            {
                _unpricedPrefabNames.Add(prefabName);
                return;
            }

            if (!TrySetPieceRecipe(piece, recipe))
            {
                _unpricedPrefabNames.Add(prefabName);
                return;
            }

            if (_verboseLogging != null && _verboseLogging.Value)
                Logger.LogInfo($"Applied curated recipe: {prefabName} = {FormatRecipe(recipe)}");
        }

        private bool TrySetPieceRecipe(object piece, RecipeIngredient[] ingredients)
        {
            if (piece == null || ingredients == null || ingredients.Length == 0)
                return false;

            FieldInfo resourcesField = piece.GetType().GetField("m_resources", AnyInstance);
            if (resourcesField == null || !resourcesField.FieldType.IsArray)
                return false;

            Type requirementType = resourcesField.FieldType.GetElementType();
            if (requirementType == null)
                return false;

            object[] itemDrops = new object[ingredients.Length];

            for (int i = 0; i < ingredients.Length; i++)
            {
                itemDrops[i] = TryGetItemDrop(ingredients[i].ItemPrefab);
                if (itemDrops[i] == null)
                {
                    Logger.LogWarning(
                        $"Could not apply recipe because item prefab '{ingredients[i].ItemPrefab}' was not found.");
                    return false;
                }
            }

            Array requirements = Array.CreateInstance(requirementType, ingredients.Length);

            for (int i = 0; i < ingredients.Length; i++)
            {
                object requirement = Activator.CreateInstance(requirementType);
                if (requirement == null)
                    return false;

                SetFieldIfExists(requirement, "m_resItem", itemDrops[i]);
                SetFieldIfExists(requirement, "m_amount", ingredients[i].Amount);
                SetFieldIfExists(requirement, "m_recover", true);
                requirements.SetValue(requirement, i);
            }

            resourcesField.SetValue(piece, requirements);
            return true;
        }

        private object TryGetItemDrop(string itemPrefabName)
        {
            if (string.IsNullOrWhiteSpace(itemPrefabName) ||
                _objectDbType == null ||
                _itemDropType == null)
            {
                return null;
            }

            object objectDb = GetStaticSingleton(_objectDbType, "instance");
            if (objectDb == null)
                return null;

            MethodInfo getItemPrefab = _objectDbType.GetMethod(
                "GetItemPrefab",
                AnyInstance,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            object itemPrefab = getItemPrefab?.Invoke(objectDb, new object[] { itemPrefabName });
            return itemPrefab == null ? null : GetComponent(itemPrefab, _itemDropType);
        }

        private static bool HasNonEmptyArrayField(object instance, string fieldName)
        {
            if (instance == null)
                return false;

            FieldInfo field = instance.GetType().GetField(fieldName, AnyInstance);
            return field?.GetValue(instance) is Array values && values.Length > 0;
        }

        private static string FormatRecipe(RecipeIngredient[] recipe)
        {
            if (recipe == null || recipe.Length == 0)
                return "(none)";

            string[] parts = new string[recipe.Length];
            for (int i = 0; i < recipe.Length; i++)
                parts[i] = $"{recipe[i].Amount}x {recipe[i].ItemPrefab}";

            return string.Join(", ", parts);
        }

        private string FormatUnpricedPrefabNames()
        {
            if (_unpricedPrefabNames.Count == 0)
                return "(none)";

            List<string> names = new List<string>(_unpricedPrefabNames);
            names.Sort(StringComparer.OrdinalIgnoreCase);

            const int maxNames = 40;
            if (names.Count <= maxNames)
                return string.Join(", ", names);

            return string.Join(", ", names.GetRange(0, maxNames)) +
                $" ... and {names.Count - maxNames} more";
        }

        private void EnsureCategoryPatches()
        {
            if (_categoryPatchesInstalled)
                return;

            ResolveTypes();
            if (_pieceTableType == null || _byUsagePieceListType == null)
                return;

            try
            {
                _harmony = _harmony ?? new Harmony(PluginGuid + ".buildcategory");

                MethodInfo updateAvailable = FindInstanceMethod(_pieceTableType, "UpdateAvailable");
                MethodInfo updateAvailableTags = FindInstanceMethod(_byUsagePieceListType, "UpdateAvailableTags");
                MethodInfo getTagDisplayName = FindInstanceMethod(_byUsagePieceListType, "GetTagDisplayName");
                MethodInfo getAvailablePiecesWithTag = FindInstanceMethod(_byUsagePieceListType, "GetAvailablePiecesWithTag");

                if (updateAvailable == null ||
                    updateAvailableTags == null ||
                    getTagDisplayName == null ||
                    getAvailablePiecesWithTag == null)
                {
                    return;
                }

                _harmony.Patch(
                    updateAvailable,
                    prefix: new HarmonyMethod(typeof(HammerEverything).GetMethod(
                        nameof(PieceTableUpdateAvailablePrefix),
                        AnyStatic)),
                    postfix: new HarmonyMethod(typeof(HammerEverything).GetMethod(
                        nameof(PieceTableUpdateAvailablePostfix),
                        AnyStatic)));

                _harmony.Patch(
                    updateAvailableTags,
                    postfix: new HarmonyMethod(typeof(HammerEverything).GetMethod(
                        nameof(UsageListUpdateAvailableTagsPostfix),
                        AnyStatic)));

                _harmony.Patch(
                    getTagDisplayName,
                    prefix: new HarmonyMethod(typeof(HammerEverything).GetMethod(
                        nameof(UsageListGetTagDisplayNamePrefix),
                        AnyStatic)));

                _harmony.Patch(
                    getAvailablePiecesWithTag,
                    prefix: new HarmonyMethod(typeof(HammerEverything).GetMethod(
                        nameof(UsageListGetAvailablePiecesWithTagPrefix),
                        AnyStatic)));

                _categoryPatchesInstalled = true;
                Logger.LogInfo("Installed Valheim 1.0 build-menu category hooks.");
            }
            catch (Exception ex)
            {
                try
                {
                    _harmony?.UnpatchSelf();
                }
                catch
                {
                    // Ignore cleanup errors and retry on a later frame.
                }

                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogWarning($"Build-menu category hooks not ready: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void PieceTableUpdateAvailablePrefix(object __instance)
        {
            _instance?.PrepareCustomCategorySlot(__instance, clearCustomList: true);
        }

        private static void PieceTableUpdateAvailablePostfix(object __instance)
        {
            _instance?.PrepareCustomCategorySlot(__instance, clearCustomList: false);
            _instance?.PopulateCustomCategoryList(__instance);
            _instance?.EnsurePieceTableCategoryMetadata(__instance);
        }

        private static void UsageListUpdateAvailableTagsPostfix(object __instance, object[] __args)
        {
            object pieceTable = __args != null && __args.Length > 0 ? __args[0] : null;
            _instance?.RegisterCustomUsageTag(__instance, pieceTable);
        }

        private static bool UsageListGetTagDisplayNamePrefix(
            object __instance,
            int index,
            ref string __result)
        {
            HammerEverything plugin = _instance;
            if (plugin == null || !plugin.IsCustomTagAtIndex(__instance, index))
                return true;

            __result = CustomBuildCategoryName;
            return false;
        }

        private static bool UsageListGetAvailablePiecesWithTagPrefix(
            object __instance,
            object[] __args)
        {
            HammerEverything plugin = _instance;
            if (plugin == null)
                return true;

            return !plugin.TryFillCustomTagResults(__instance, __args);
        }

        private bool AssignCustomBuildCategory(object piece)
        {
            if (piece == null)
                return false;

            FieldInfo categoryField = piece.GetType().GetField("m_category", AnyInstance);
            if (categoryField == null || !categoryField.FieldType.IsEnum)
                return false;

            _pieceCategoryType = categoryField.FieldType;

            if (_customCategoryId < 0)
            {
                int allValue = GetEnumValueOrDefault(_pieceCategoryType, "All", 100);
                int maxBelowAll = -1;

                foreach (object raw in Enum.GetValues(_pieceCategoryType))
                {
                    int value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    if (value >= 0 && value < allValue && value > maxBelowAll)
                        maxBelowAll = value;
                }

                _customCategoryId = maxBelowAll + 1;
                if (_customCategoryId == allValue)
                    _customCategoryId = allValue + 1;

                Logger.LogInfo(
                    $"Registered private build category '{CustomBuildCategoryName}' as ID {_customCategoryId}.");
            }

            categoryField.SetValue(piece, Enum.ToObject(_pieceCategoryType, _customCategoryId));

            // Zero means no vanilla usage tags. Our ByUsagePieceList hook supplies
            // the dedicated Hammer Everything tag instead.
            SetEnumFieldToZero(piece, "m_usage");
            return true;
        }

        private void PrepareCustomCategorySlot(object pieceTable, bool clearCustomList)
        {
            if (pieceTable == null || _customCategoryId < 0)
                return;

            try
            {
                IList byCategory = GetFieldValue(pieceTable, "m_availablePiecesByCategory") as IList;
                if (byCategory != null)
                {
                    EnsureListOfListsSize(byCategory, _customCategoryId + 1);

                    if (clearCustomList &&
                        _customCategoryId >= 0 &&
                        _customCategoryId < byCategory.Count)
                    {
                        object list = byCategory[_customCategoryId];
                        ClearCollection(list);
                    }
                }

                ResizeArrayField(pieceTable, "m_selectedPiece", _customCategoryId + 1);
                ResizeArrayField(pieceTable, "m_lastSelectedPiece", _customCategoryId + 1);
            }
            catch (Exception ex)
            {
                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogDebug($"Custom category capacity refresh skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void PopulateCustomCategoryList(object pieceTable)
        {
            if (pieceTable == null || _customCategoryId < 0)
                return;

            IList byCategory = GetFieldValue(pieceTable, "m_availablePiecesByCategory") as IList;
            if (byCategory == null)
                return;

            EnsureListOfListsSize(byCategory, _customCategoryId + 1);
            if (_customCategoryId >= byCategory.Count)
                return;

            object customList = byCategory[_customCategoryId];
            if (customList == null)
                return;

            ClearCollection(customList);

            object rawAvailable = GetFieldValue(pieceTable, "m_availablePieces");
            if (!(rawAvailable is IEnumerable availablePieces))
                return;

            foreach (object piece in availablePieces)
            {
                if (GetEnumFieldInt(piece, "m_category") != _customCategoryId)
                    continue;

                AddCollectionItemIfMissing(customList, piece);
            }
        }

        private void EnsurePieceTableCategoryMetadata(object pieceTable)
        {
            if (pieceTable == null || _customCategoryId < 0 || _pieceCategoryType == null)
                return;

            IList categories = GetFieldValue(pieceTable, "m_categories") as IList;
            if (categories == null)
                return;

            object categoryValue = Enum.ToObject(_pieceCategoryType, _customCategoryId);
            int categoryIndex = IndexOfNumericValue(categories, _customCategoryId);

            if (categoryIndex < 0)
            {
                categories.Add(categoryValue);
                categoryIndex = categories.Count - 1;
            }

            IList labels = GetFieldValue(pieceTable, "m_categoryLabels") as IList;
            if (labels == null)
                return;

            while (labels.Count <= categoryIndex)
                labels.Add(string.Empty);

            labels[categoryIndex] = CustomBuildCategoryName;
        }

        private void RegisterCustomUsageTag(object usageList, object pieceTable)
        {
            if (usageList == null || pieceTable == null || _customCategoryId < 0)
                return;

            if (!PieceTableHasAvailableCustomPiece(pieceTable))
            {
                _customTagIds.Remove(usageList);
                return;
            }

            IList availableTags = GetFieldValue(usageList, "m_availableTags") as IList;
            Array usageTags = GetFieldValue(usageList, "m_usageTags") as Array;
            if (availableTags == null || usageTags == null)
                return;

            if (_customTagIds.TryGetValue(usageList, out int existingTagId) &&
                CollectionContainsNumericValue(availableTags, existingTagId))
            {
                return;
            }

            int tagId = usageTags.Length + availableTags.Count;
            while (CollectionContainsNumericValue(availableTags, tagId))
                tagId++;

            availableTags.Add(tagId);
            _customTagIds[usageList] = tagId;
        }

        private bool IsCustomTagAtIndex(object usageList, int index)
        {
            if (usageList == null ||
                !_customTagIds.TryGetValue(usageList, out int customTagId))
            {
                return false;
            }

            IList availableTags = GetFieldValue(usageList, "m_availableTags") as IList;
            if (availableTags == null || index < 0 || index >= availableTags.Count)
                return false;

            return Convert.ToInt32(availableTags[index], CultureInfo.InvariantCulture) == customTagId;
        }

        private bool TryFillCustomTagResults(object usageList, object[] args)
        {
            if (usageList == null ||
                args == null ||
                args.Length < 3 ||
                !_customTagIds.TryGetValue(usageList, out int customTagId))
            {
                return false;
            }

            int requestedTagId;
            try
            {
                requestedTagId = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }

            if (requestedTagId != customTagId)
                return false;

            object pieceTable = args[1];
            object resultOut = args[2];
            object rawAvailable = GetFieldValue(pieceTable, "m_availablePieces");

            if (!(rawAvailable is IEnumerable availablePieces) || resultOut == null)
                return true;

            foreach (object piece in availablePieces)
            {
                if (GetEnumFieldInt(piece, "m_category") == _customCategoryId)
                    AddCollectionItemIfMissing(resultOut, piece);
            }

            return true;
        }

        private bool PieceTableHasAvailableCustomPiece(object pieceTable)
        {
            object rawAvailable = GetFieldValue(pieceTable, "m_availablePieces");
            if (!(rawAvailable is IEnumerable availablePieces))
                return false;

            foreach (object piece in availablePieces)
            {
                if (GetEnumFieldInt(piece, "m_category") == _customCategoryId)
                    return true;
            }

            return false;
        }

        private static int GetEnumValueOrDefault(Type enumType, string name, int fallback)
        {
            if (enumType == null || !enumType.IsEnum)
                return fallback;

            try
            {
                object value = Enum.Parse(enumType, name, ignoreCase: false);
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static int GetEnumFieldInt(object instance, string fieldName)
        {
            object value = GetFieldValue(instance, fieldName);
            if (value == null)
                return int.MinValue;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return int.MinValue;
            }
        }

        private static void EnsureListOfListsSize(IList outer, int requiredCount)
        {
            if (outer == null || requiredCount <= outer.Count)
                return;

            Type elementType = null;
            Type outerType = outer.GetType();
            if (outerType.IsGenericType)
            {
                Type[] genericArgs = outerType.GetGenericArguments();
                if (genericArgs.Length == 1)
                    elementType = genericArgs[0];
            }

            if (elementType == null)
                return;

            while (outer.Count < requiredCount)
                outer.Add(Activator.CreateInstance(elementType));
        }

        private static void ResizeArrayField(object instance, string fieldName, int requiredLength)
        {
            if (instance == null)
                return;

            FieldInfo field = instance.GetType().GetField(fieldName, AnyInstance);
            if (field == null || !field.FieldType.IsArray)
                return;

            Array current = field.GetValue(instance) as Array;
            int currentLength = current?.Length ?? 0;
            if (currentLength >= requiredLength)
                return;

            Type elementType = field.FieldType.GetElementType();
            if (elementType == null)
                return;

            Array resized = Array.CreateInstance(elementType, requiredLength);
            if (current != null)
                Array.Copy(current, resized, currentLength);

            field.SetValue(instance, resized);
        }

        private static int IndexOfNumericValue(IList list, int value)
        {
            if (list == null)
                return -1;

            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    if (Convert.ToInt32(list[i], CultureInfo.InvariantCulture) == value)
                        return i;
                }
                catch
                {
                    // Ignore entries that are not enum/integer-like.
                }
            }

            return -1;
        }

        private static bool CollectionContainsNumericValue(IList list, int value)
        {
            return IndexOfNumericValue(list, value) >= 0;
        }

        private static void ClearCollection(object collection)
        {
            if (collection == null)
                return;

            if (collection is IList list)
            {
                list.Clear();
                return;
            }

            MethodInfo clear = collection.GetType().GetMethod(
                "Clear",
                AnyInstance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            clear?.Invoke(collection, null);
        }

        private static void AddCollectionItemIfMissing(object collection, object item)
        {
            if (collection == null || item == null)
                return;

            if (collection is IList list)
            {
                if (!list.Contains(item))
                    list.Add(item);
                return;
            }

            MethodInfo contains = FindCompatibleSingleArgumentMethod(collection.GetType(), "Contains", item);
            if (contains != null)
            {
                object present = contains.Invoke(collection, new[] { item });
                if (present is bool exists && exists)
                    return;
            }

            MethodInfo add = FindCompatibleSingleArgumentMethod(collection.GetType(), "Add", item);
            add?.Invoke(collection, new[] { item });
        }

        private static MethodInfo FindCompatibleSingleArgumentMethod(Type type, string name, object value)
        {
            if (type == null)
                return null;

            foreach (MethodInfo method in type.GetMethods(AnyInstance))
            {
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                    continue;

                if (value == null || parameters[0].ParameterType.IsInstanceOfType(value))
                    return method;
            }

            return null;
        }

        private static MethodInfo FindInstanceMethod(Type type, string name)
        {
            if (type == null)
                return null;

            foreach (MethodInfo method in type.GetMethods(AnyInstance))
            {
                if (string.Equals(method.Name, name, StringComparison.Ordinal))
                    return method;
            }

            return null;
        }

        private void QueueUniqueIcon(string prefabName, object prefab, object piece)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || prefab == null || piece == null)
                return;

            if (_generatedIcons.TryGetValue(prefabName, out object cached))
            {
                SetFieldIfExists(piece, "m_icon", cached);
                return;
            }

            if (_iconQueued.Contains(prefabName))
                return;

            ResolveTypes();
            if (!CanRenderIcons())
                return;

            _iconQueued.Add(prefabName);
            _iconRenderQueue.Enqueue(new IconRenderRequest(prefabName, prefab, piece));
        }

        private void ProcessIconRenderQueue()
        {
            if (_iconRenderQueue.Count == 0)
                return;

            if (!CanRenderIcons())
            {
                _iconRenderQueue.Clear();
                _iconQueued.Clear();
                return;
            }

            IconRenderRequest request = _iconRenderQueue.Dequeue();
            _iconQueued.Remove(request.PrefabName);

            try
            {
                object sprite = RenderPrefabIcon(request.Prefab);
                if (sprite == null)
                {
                    if (_verboseLogging != null && _verboseLogging.Value)
                        Logger.LogWarning($"Could not render unique icon for {request.PrefabName}; keeping the fallback icon.");
                    return;
                }

                _generatedIcons[request.PrefabName] = sprite;
                SetFieldIfExists(request.Piece, "m_icon", sprite);
                _playerRefreshed = false;

                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogInfo($"Rendered unique Hammer icon: {request.PrefabName}");
            }
            catch (Exception ex)
            {
                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogWarning($"Icon render failed for {request.PrefabName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool CanRenderIcons()
        {
            if (_graphicsAvailable.HasValue)
                return _graphicsAvailable.Value;

            ResolveTypes();

            if (_unityObjectType == null ||
                _gameObjectType == null ||
                _componentType == null ||
                _transformType == null ||
                _rendererType == null ||
                _meshFilterType == null ||
                _cameraType == null ||
                _lightComponentType == null ||
                _renderTextureType == null ||
                _texture2DType == null ||
                _spriteType == null ||
                _rectType == null ||
                _vector2Type == null ||
                _vector3Type == null ||
                _quaternionType == null ||
                _colorType == null ||
                _textureFormatType == null ||
                _cameraClearFlagsType == null ||
                _lightKindType == null)
            {
                return false;
            }

            object isBatchMode = GetStaticMemberValue(_applicationType, "isBatchMode");
            if (isBatchMode is bool batchMode && batchMode)
            {
                _graphicsAvailable = false;
                return false;
            }

            object graphicsDeviceType = GetStaticMemberValue(_systemInfoType, "graphicsDeviceType");
            if (graphicsDeviceType != null &&
                string.Equals(graphicsDeviceType.ToString(), "Null", StringComparison.OrdinalIgnoreCase))
            {
                _graphicsAvailable = false;
                return false;
            }

            _graphicsAvailable = true;
            return true;
        }

        private object RenderPrefabIcon(object prefab)
        {
            object parent = null;
            object spawn = null;
            object cameraObject = null;
            object lightObject = null;
            object camera = null;
            object renderTexture = null;
            object previousRenderTexture = null;

            try
            {
                parent = Activator.CreateInstance(_gameObjectType, new object[] { "HammerEverything Icon Parent" });
                SetGameObjectActive(parent, false);

                object parentTransform = GetPropertyValue(parent, "transform");
                spawn = InvokeStaticWithOptionalTail(_unityObjectType, "Instantiate", prefab, parentTransform);
                if (spawn == null)
                    return null;

                SetGameObjectActive(spawn, false);

                if (!StripCloneToVisuals(spawn))
                    return null;

                SetLayerRecursive(spawn, IconLayer);

                object spawnTransform = GetPropertyValue(spawn, "transform");
                if (spawnTransform == null)
                    return null;

                SetPropertyIfExists(spawnTransform, "parent", null);
                DestroyUnityObjectImmediate(parent);
                parent = null;

                SetPropertyIfExists(spawnTransform, "position", CreateVector3(0f, 0f, 0f));
                SetPropertyIfExists(
                    spawnTransform,
                    "rotation",
                    InvokeStaticWithOptionalTail(_quaternionType, "Euler", 23f, 51f, 25.8f));

                if (!TryGetVisualBounds(
                        spawn,
                        out float minX,
                        out float minY,
                        out float minZ,
                        out float maxX,
                        out float maxY,
                        out float maxZ))
                {
                    return null;
                }

                float centerX = (minX + maxX) * 0.5f;
                float centerY = (minY + maxY) * 0.5f;
                float centerZ = (minZ + maxZ) * 0.5f;

                SetPropertyIfExists(
                    spawnTransform,
                    "position",
                    CreateVector3(-centerX, -centerY, -centerZ));

                float sizeX = Math.Max(0.01f, maxX - minX);
                float sizeY = Math.Max(0.01f, maxY - minY);
                float maxMeshSize = Math.Max(sizeX, sizeY) + 0.1f;
                float radians = IconFieldOfView * (float)Math.PI / 180f;
                float distance = maxMeshSize / Math.Max(0.0001f, (float)Math.Tan(radians));

                cameraObject = Activator.CreateInstance(_gameObjectType, new object[] { "HammerEverything Icon Camera" });
                camera = AddComponent(cameraObject, _cameraType);
                if (camera == null)
                    return null;

                object cameraTransform = GetPropertyValue(cameraObject, "transform");
                SetPropertyIfExists(camera, "backgroundColor", Activator.CreateInstance(_colorType, new object[] { 0f, 0f, 0f, 0f }));
                SetPropertyIfExists(camera, "clearFlags", Enum.Parse(_cameraClearFlagsType, "SolidColor"));
                SetPropertyIfExists(camera, "fieldOfView", IconFieldOfView);
                SetPropertyIfExists(camera, "nearClipPlane", 0.01f);
                SetPropertyIfExists(camera, "farClipPlane", 100000f);
                SetPropertyIfExists(camera, "cullingMask", unchecked(1 << IconLayer));
                SetPropertyIfExists(cameraTransform, "position", CreateVector3(0f, 0f, distance));
                SetPropertyIfExists(
                    cameraTransform,
                    "rotation",
                    InvokeStaticWithOptionalTail(_quaternionType, "Euler", 0f, 180f, 0f));

                lightObject = Activator.CreateInstance(_gameObjectType, new object[] { "HammerEverything Icon Light" });
                object light = AddComponent(lightObject, _lightComponentType);
                if (light == null)
                    return null;

                object lightTransform = GetPropertyValue(lightObject, "transform");
                SetPropertyIfExists(light, "type", Enum.Parse(_lightKindType, "Directional"));
                SetPropertyIfExists(light, "cullingMask", unchecked(1 << IconLayer));
                SetPropertyIfExists(light, "intensity", 1.15f);
                SetPropertyIfExists(lightTransform, "position", CreateVector3(0f, 0f, 0f));
                SetPropertyIfExists(
                    lightTransform,
                    "rotation",
                    InvokeStaticWithOptionalTail(_quaternionType, "Euler", 5f, 180f, 5f));

                renderTexture = InvokeStaticWithOptionalTail(_renderTextureType, "GetTemporary", IconSize, IconSize);
                if (renderTexture == null)
                    return null;

                previousRenderTexture = GetStaticMemberValue(_renderTextureType, "active");
                SetPropertyIfExists(camera, "targetTexture", renderTexture);
                SetStaticPropertyIfExists(_renderTextureType, "active", renderTexture);

                SetGameObjectActive(spawn, true);
                InvokeInstanceWithOptionalTail(camera, "Render");
                SetGameObjectActive(spawn, false);

                object rgba32 = Enum.Parse(_textureFormatType, "RGBA32");
                object texture = Activator.CreateInstance(
                    _texture2DType,
                    new object[] { IconSize, IconSize, rgba32, false });

                object rect = Activator.CreateInstance(
                    _rectType,
                    new object[] { 0f, 0f, (float)IconSize, (float)IconSize });

                InvokeInstanceWithOptionalTail(texture, "ReadPixels", rect, 0, 0);
                InvokeInstanceWithOptionalTail(texture, "Apply");

                object pivot = Activator.CreateInstance(_vector2Type, new object[] { 0.5f, 0.5f });
                return InvokeStaticWithOptionalTail(_spriteType, "Create", texture, rect, pivot);
            }
            finally
            {
                try
                {
                    if (_renderTextureType != null)
                        SetStaticPropertyIfExists(_renderTextureType, "active", previousRenderTexture);
                }
                catch
                {
                    // Best-effort cleanup only.
                }

                try
                {
                    if (camera != null)
                        SetPropertyIfExists(camera, "targetTexture", null);
                }
                catch
                {
                    // Best-effort cleanup only.
                }

                try
                {
                    if (renderTexture != null)
                        InvokeStaticWithOptionalTail(_renderTextureType, "ReleaseTemporary", renderTexture);
                }
                catch
                {
                    // Best-effort cleanup only.
                }

                DestroyUnityObjectImmediate(spawn);
                DestroyUnityObjectImmediate(parent);
                DestroyUnityObjectImmediate(cameraObject);
                DestroyUnityObjectImmediate(lightObject);
            }
        }

        private bool StripCloneToVisuals(object root)
        {
            object rawComponents = InvokeInstanceWithOptionalTail(
                root,
                "GetComponentsInChildren",
                _componentType,
                true);

            if (!(rawComponents is IEnumerable components))
                return false;

            List<object> removable = new List<object>();

            foreach (object component in components)
            {
                if (component == null)
                    continue;

                Type type = component.GetType();
                bool keep =
                    (_transformType != null && _transformType.IsAssignableFrom(type)) ||
                    (_rendererType != null && _rendererType.IsAssignableFrom(type)) ||
                    (_meshFilterType != null && _meshFilterType.IsAssignableFrom(type));

                if (!keep)
                    removable.Add(component);
            }

            while (removable.Count > 0)
            {
                bool madeProgress = false;

                for (int i = removable.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        DestroyUnityObjectImmediate(removable[i]);
                        removable.RemoveAt(i);
                        madeProgress = true;
                    }
                    catch
                    {
                        // A RequireComponent dependency may need another component removed first.
                    }
                }

                if (!madeProgress)
                    return false;
            }

            return true;
        }

        private void SetLayerRecursive(object root, int layer)
        {
            object rawTransforms = InvokeInstanceWithOptionalTail(
                root,
                "GetComponentsInChildren",
                _transformType,
                true);

            if (!(rawTransforms is IEnumerable transforms))
                return;

            foreach (object transform in transforms)
            {
                object gameObject = GetPropertyValue(transform, "gameObject");
                SetPropertyIfExists(gameObject, "layer", layer);
            }
        }

        private bool TryGetVisualBounds(
            object root,
            out float minX,
            out float minY,
            out float minZ,
            out float maxX,
            out float maxY,
            out float maxZ)
        {
            minX = minY = minZ = float.PositiveInfinity;
            maxX = maxY = maxZ = float.NegativeInfinity;

            object rawRenderers = InvokeInstanceWithOptionalTail(
                root,
                "GetComponentsInChildren",
                _rendererType,
                true);

            if (!(rawRenderers is IEnumerable renderers))
                return false;

            bool found = false;

            foreach (object renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Type rendererType = renderer.GetType();
                if (_meshRendererType != null && _skinnedMeshRendererType != null &&
                    !_meshRendererType.IsAssignableFrom(rendererType) &&
                    !_skinnedMeshRendererType.IsAssignableFrom(rendererType))
                {
                    continue;
                }

                object bounds = GetPropertyValue(renderer, "bounds");
                object min = GetMemberValue(bounds, "min");
                object max = GetMemberValue(bounds, "max");
                if (min == null || max == null)
                    continue;

                float x0 = GetSingleMember(min, "x");
                float y0 = GetSingleMember(min, "y");
                float z0 = GetSingleMember(min, "z");
                float x1 = GetSingleMember(max, "x");
                float y1 = GetSingleMember(max, "y");
                float z1 = GetSingleMember(max, "z");

                if (!AreFinite(x0, y0, z0, x1, y1, z1))
                    continue;

                minX = Math.Min(minX, x0);
                minY = Math.Min(minY, y0);
                minZ = Math.Min(minZ, z0);
                maxX = Math.Max(maxX, x1);
                maxY = Math.Max(maxY, y1);
                maxZ = Math.Max(maxZ, z1);
                found = true;
            }

            return found && maxX > minX && maxY > minY;
        }

        private object CreateVector3(float x, float y, float z)
        {
            return Activator.CreateInstance(_vector3Type, new object[] { x, y, z });
        }

        private void SetGameObjectActive(object gameObject, bool active)
        {
            InvokeInstanceWithOptionalTail(gameObject, "SetActive", active);
        }

        private void DestroyUnityObjectImmediate(object unityObject)
        {
            if (unityObject == null || _unityObjectType == null)
                return;

            InvokeStaticWithOptionalTail(_unityObjectType, "DestroyImmediate", unityObject);
        }

        private static bool AreFinite(params float[] values)
        {
            foreach (float value in values)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                    return false;
            }

            return true;
        }

        private static float GetSingleMember(object instance, string memberName)
        {
            object value = GetMemberValue(instance, memberName);
            return value == null
                ? 0f
                : Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null)
                return null;

            FieldInfo field = instance.GetType().GetField(memberName, AnyInstance);
            if (field != null)
                return field.GetValue(instance);

            PropertyInfo property = instance.GetType().GetProperty(memberName, AnyInstance);
            return property?.GetValue(instance, null);
        }

        private static object GetStaticMemberValue(Type type, string memberName)
        {
            if (type == null)
                return null;

            FieldInfo field = type.GetField(memberName, AnyStatic);
            if (field != null)
                return field.GetValue(null);

            PropertyInfo property = type.GetProperty(memberName, AnyStatic);
            return property?.GetValue(null, null);
        }

        private static void SetStaticPropertyIfExists(Type type, string propertyName, object value)
        {
            if (type == null)
                return;

            PropertyInfo property = type.GetProperty(propertyName, AnyStatic);
            if (property != null && property.CanWrite)
                property.SetValue(null, value, null);
        }

        private static object InvokeInstanceWithOptionalTail(object instance, string methodName, params object[] supplied)
        {
            if (instance == null)
                return null;

            MethodInfo method = FindCallableMethod(instance.GetType(), methodName, AnyInstance, supplied);
            return method?.Invoke(instance, BuildInvocationArguments(method, supplied));
        }

        private static object InvokeStaticWithOptionalTail(Type type, string methodName, params object[] supplied)
        {
            if (type == null)
                return null;

            MethodInfo method = FindCallableMethod(type, methodName, AnyStatic, supplied);
            return method?.Invoke(null, BuildInvocationArguments(method, supplied));
        }

        private static MethodInfo FindCallableMethod(
            Type type,
            string methodName,
            BindingFlags flags,
            object[] supplied)
        {
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                    method.IsGenericMethodDefinition)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < supplied.Length)
                    continue;

                bool compatible = true;

                for (int i = 0; i < supplied.Length; i++)
                {
                    object value = supplied[i];
                    Type parameterType = parameters[i].ParameterType;

                    if (value == null)
                    {
                        if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                        {
                            compatible = false;
                            break;
                        }

                        continue;
                    }

                    if (!parameterType.IsInstanceOfType(value))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (!compatible)
                    continue;

                for (int i = supplied.Length; i < parameters.Length; i++)
                {
                    if (!parameters[i].IsOptional)
                    {
                        compatible = false;
                        break;
                    }
                }

                if (compatible)
                    return method;
            }

            return null;
        }

        private static object[] BuildInvocationArguments(MethodInfo method, object[] supplied)
        {
            if (method == null)
                return null;

            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];

            for (int i = 0; i < supplied.Length; i++)
                args[i] = supplied[i];

            for (int i = supplied.Length; i < parameters.Length; i++)
                args[i] = parameters[i].DefaultValue;

            return args;
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
            if (_managedPrefabNames.Count == 0 || _pieceType == null)
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

                    if (!_managedPrefabNames.Contains(name))
                        continue;

                    bool isPrefab =
                        _managedPrefabObjects.TryGetValue(name, out object prefab) &&
                        ReferenceEquals(prefab, gameObject);

                    if (isPrefab)
                    {
                        if (_pieceAddedByUs.Contains(name))
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
                    bool playerBuilt = creator != 0L;

                    if (_pieceAddedByUs.Contains(name))
                        SetFieldIfExists(piece, "m_canBeRemoved", playerBuilt);

                    // Location props frequently carry DropOnDestroyed loot (Soft
                    // Tissue, Extractors, Surtling Cores, etc.). Keep the component
                    // alive: WearNTear can retain its OnDestroyed callback, so
                    // destroying the component leaves a delegate targeting a dead
                    // Unity component and can make Hammer removal throw before the
                    // object is deleted. Neutralize only the player-built clone's
                    // drop table instead.
                    if (playerBuilt)
                        NeutralizeDropOnDestroyedLoot(gameObject);
                }
            }
            catch (Exception ex)
            {
                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogDebug($"Placed-prop removal scan skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }


        private void NeutralizeDropOnDestroyedLoot(object gameObject)
        {
            if (gameObject == null || _dropOnDestroyedType == null)
                return;

            try
            {
                object rawComponents = InvokeInstanceWithOptionalTail(
                    gameObject,
                    "GetComponentsInChildren",
                    _dropOnDestroyedType,
                    true);

                if (!(rawComponents is IEnumerable components))
                    return;

                foreach (object component in components)
                {
                    if (component == null)
                        continue;

                    FieldInfo dropTableField =
                        component.GetType().GetField("m_dropWhenDestroyed", AnyInstance);

                    if (dropTableField == null)
                        continue;

                    Type dropTableType = dropTableField.FieldType;
                    if (dropTableType == null)
                        continue;

                    if (!_emptyDropTables.TryGetValue(dropTableType, out object emptyDropTable))
                    {
                        try
                        {
                            emptyDropTable = Activator.CreateInstance(dropTableType);
                        }
                        catch
                        {
                            emptyDropTable = null;
                        }

                        if (emptyDropTable == null)
                            continue;

                        _emptyDropTables[dropTableType] = emptyDropTable;
                    }

                    if (!ReferenceEquals(dropTableField.GetValue(component), emptyDropTable))
                        dropTableField.SetValue(component, emptyDropTable);
                }
            }
            catch (Exception ex)
            {
                if (_verboseLogging != null && _verboseLogging.Value)
                    Logger.LogDebug($"Player-built loot neutralization skipped: {ex.GetType().Name}: {ex.Message}");
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
            if (_pieceTableType == null)
                _pieceTableType = FindLoadedType("PieceTable");
            if (_byUsagePieceListType == null)
                _byUsagePieceListType = FindLoadedType("ByUsagePieceList");
            if (_dropOnDestroyedType == null)
                _dropOnDestroyedType = FindLoadedType("DropOnDestroyed");

            if (_unityObjectType == null)
                _unityObjectType = FindLoadedType("UnityEngine.Object");
            if (_gameObjectType == null)
                _gameObjectType = FindLoadedType("UnityEngine.GameObject");
            if (_componentType == null)
                _componentType = FindLoadedType("UnityEngine.Component");
            if (_transformType == null)
                _transformType = FindLoadedType("UnityEngine.Transform");
            if (_rendererType == null)
                _rendererType = FindLoadedType("UnityEngine.Renderer");
            if (_meshRendererType == null)
                _meshRendererType = FindLoadedType("UnityEngine.MeshRenderer");
            if (_skinnedMeshRendererType == null)
                _skinnedMeshRendererType = FindLoadedType("UnityEngine.SkinnedMeshRenderer");
            if (_meshFilterType == null)
                _meshFilterType = FindLoadedType("UnityEngine.MeshFilter");
            if (_cameraType == null)
                _cameraType = FindLoadedType("UnityEngine.Camera");
            if (_lightComponentType == null)
                _lightComponentType = FindLoadedType("UnityEngine.Light");
            if (_renderTextureType == null)
                _renderTextureType = FindLoadedType("UnityEngine.RenderTexture");
            if (_texture2DType == null)
                _texture2DType = FindLoadedType("UnityEngine.Texture2D");
            if (_spriteType == null)
                _spriteType = FindLoadedType("UnityEngine.Sprite");
            if (_rectType == null)
                _rectType = FindLoadedType("UnityEngine.Rect");
            if (_vector2Type == null)
                _vector2Type = FindLoadedType("UnityEngine.Vector2");
            if (_vector3Type == null)
                _vector3Type = FindLoadedType("UnityEngine.Vector3");
            if (_quaternionType == null)
                _quaternionType = FindLoadedType("UnityEngine.Quaternion");
            if (_colorType == null)
                _colorType = FindLoadedType("UnityEngine.Color");
            if (_textureFormatType == null)
                _textureFormatType = FindLoadedType("UnityEngine.TextureFormat");
            if (_cameraClearFlagsType == null)
                _cameraClearFlagsType = FindLoadedType("UnityEngine.CameraClearFlags");
            if (_lightKindType == null)
                _lightKindType = FindLoadedType("UnityEngine.LightType");
            if (_applicationType == null)
                _applicationType = FindLoadedType("UnityEngine.Application");
            if (_systemInfoType == null)
                _systemInfoType = FindLoadedType("UnityEngine.SystemInfo");
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

        private bool EnsurePieceDisplayName(object piece, string prefabName)
        {
            if (piece == null)
                return false;

            FieldInfo field = piece.GetType().GetField("m_name", AnyInstance);
            if (field == null || field.FieldType != typeof(string))
                return false;

            string current = field.GetValue(piece) as string;
            if (!string.IsNullOrWhiteSpace(current))
                return true;

            string fallback = GetFriendlyName(prefabName);
            if (string.IsNullOrWhiteSpace(fallback))
                return false;

            field.SetValue(piece, fallback);

            if (_verboseLogging != null && _verboseLogging.Value)
                Logger.LogInfo($"Filled blank Piece name: {prefabName} -> {fallback}");

            return true;
        }

        private static string GetFriendlyName(string prefabName)
        {
            if (FriendlyNames.TryGetValue(prefabName, out string friendly))
                return friendly;

            string value = prefabName ?? "Vanilla Prop";
            value = Regex.Replace(value, "^dvergrprops_", "Dvergr ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "^dvergrtown_", "Dvergr ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "^goblinprops_", "Fuling ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, "^goblin_", "Fuling ", RegexOptions.IgnoreCase);
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
