# Expanded Worlds - Vanilla Continuity Audit

Authority: clean Terraria 1.4.5.8 retail decompile produced from the matching retail binary.

This file is the checklist for deciding whether Expanded Worlds is allowed to change a world-generation result. The default answer is **no**.

## Canonical physical size series

Terraria's own dimensions and network-section grid establish the sequence:

| Tier | Preset | Tiles | 200 x 150 sections |
| ---: | --- | ---: | ---: |
| 1 | Small | 4,200 x 1,200 | 21 x 8 |
| 2 | Medium | 6,400 x 1,800 | 32 x 12 |
| 3 | Large | 8,400 x 2,400 | 42 x 16 |
| 4 | THICC | 10,600 x 3,000 | 53 x 20 |
| 5 | THICC 2 | 12,600 x 3,600 | 63 x 24 |
| 6 | THICC 3 | 14,800 x 4,200 | 74 x 28 |
| 7 | THICC 4 | 16,800 x 4,800 | 84 x 32 |
| 8 | THICC 5 | 19,000 x 5,400 | 95 x 36 |
| 9 | THICC 6 | 21,000 x 6,000 | 105 x 40 |
| 10 | THICC 7 | 23,200 x 6,600 | 116 x 44 |
| 11 | THICC 8 | 25,200 x 7,200 | 126 x 48 |
| 12 | THICC 9 | 27,400 x 7,800 | 137 x 52 |
| 13 | THICC 10 | 29,400 x 8,400 | 147 x 56 |
| 14 | THICC 11 | 31,600 x 9,000 | 158 x 60 |

Horizontal section deltas continue `+11, +10`; vertical section deltas continue `+4`. This also keeps the same slight aspect-ratio wobble visible at vanilla Medium instead of creating a new aspect-ratio family.

THICC 11 is the final public tier because the next canonical width, 33,600, exceeds Terraria's signed Int16-positive coordinate range.

All expanded widths remain categorically Large to vanilla `WorldGen.GetWorldSize()`. The physical dimensions are carried separately only until Terraria begins generation.

## Rule 1: physical formulas remain untouched

No Expanded Worlds patch is allowed to reinterpret a source formula merely because a larger canvas produces a surprising number.

Examples that remain Terraria-owned include:

- `Main.maxTilesX` / `Main.maxTilesY` formulas;
- `WorldGenRange` `WorldWidth`, `WorldHeight`, and `WorldArea` scaling;
- floating-point width/height ratios such as `maxTilesX / 4200.0`;
- integer division such as `maxTilesX / 4200`;
- Jungle, Underground Desert, Hive, cave, ore, track, and other geometry already driven by the physical canvas;
- secret/special-seed branches and their RNG.

There is therefore no Desert/Jungle/Hive/feature-geometry/secret-seed aspect-ratio correction layer.

## Rule 2: explicit size tables continue when the sequence is unique

The following 1.4.5.8 Small/Medium/Large tables have one obvious arithmetic continuation and are extended mechanically:

| Source rule | Vanilla terms | Continuation rule |
| --- | --- | --- |
| Sky-lake base | 1, 2, 3 | `tier` |
| Statue multiplier | 2, 3, 4 | `tier + 1` |
| Glow Tulips | 2, 4, 6 | `2 * tier` |
| Boulder Pet base quota | 2, 4, 6 | `2 * tier` |
| Spike Cave base | 3, 5, 7 | `2 * tier + 1` |
| Chillet Eggs | 6, 9, 12 | `3 * tier + 3` |
| Dirtiest Block base | 3, 6, 9 | `3 * tier` |
| Dual Dungeon bookshelf minimum | 5, 10, 15 | `5 * tier` |
| Dual Dungeon water-candle minimum | 5, 10, 15 | `5 * tier` |
| Early Dual Dungeon altars | 20, 30, 40 | `10 * tier + 10` |
| Early desert drop traps | 8, 12, 16 | `4 * tier + 4` |
| Early snow drop traps | 6, 10, 14 | `4 * tier + 2` |
| Early cavern drop traps | 4, 6, 8 | `2 * tier + 2` |
| Early pit traps | 4, 8, 12 | `4 * tier` |
| Early biome clumps | 40, 60, 80 | `20 * tier + 20` |
| Flooded-pit quota | 2, 4, 6 | `2 * tier` |
| Shimmer specialized rooms | 2, 4, 6 | `2 * tier` |
| Living Tree rooms | 2, 6, 10 | `4 * tier - 2` |
| Living Mahogany rooms | 2, 6, 10 | `4 * tier - 2` |
| Beehive rooms | 5, 8, 11 | `3 * tier + 2` |
| Crystal rooms | 6, 10, 14 | `4 * tier + 2` |
| Specialized halls | 3, 4, 5 | `tier + 2` |
| Dual Dungeon trap base | 30, 50, 70 | `20 * tier + 10` |
| Dual Dungeon trap RNG exclusive max | 11, 16, 21 | `5 * tier + 6` |

Downstream vanilla modifiers stay downstream. No Traps, Celebration, Remix, Error World, Care Bears, and other seed behavior is duplicated inside these continuation functions.

## Rule 3: high-confidence contextual continuations may be promoted, but must stay visibly separate

A three-term table can be ambiguous in isolation while still having a strong source-backed continuation when compared with Terraria's other size math. Those promotions live in `InferredTierContinuity.cs`, not in the exact arithmetic table above.

### Shadow Orb / Crimson Heart quota

Vanilla source: `8, 14, 18`.

Vertical network sections are `8, 12, 16` for Small/Medium/Large. Medium and Large are exactly `verticalSections + 2`; Small is the compact-world exception. Expanded vertical sections continue `20, 24, ... 60`, so from Medium upward the promoted rule is:

`quota = verticalSections + 2 = 4 * tier + 6`

That gives THICC `22` through THICC 11 `62`. The source uses the same quota for Shadow Orb rooms and Crimson Heart rooms. Placement, room availability, and RNG remain Terraria-owned.

### Spider specialized rooms

Vanilla source: `2, 6, 8`.

From Medium upward this is exactly `verticalSections / 2`; Small is the compact-world exception. The promoted rule therefore gives THICC `10` through THICC 11 `30`.

This is a conversion quota over already-existing eligible rooms, not a guarantee that every requested Spider room can be placed.

### Lihzahrd painting cap: geometry-backed continuation

Vanilla uses the same painting cap in both relevant generation paths:

- Dual Dungeon `DungeonGlobalPaintings`: Small `1`, Medium `2`, Large `2 + genRand.Next(2)`.
- Legacy `WorldGen.templePart2()`: Small `1`, Medium `2`, Large `2 + genRand.Next(2)` by equivalent width gates.

There is independent physical-size evidence that this cap is intended to grow rather than remain permanently Large-sized:

- legacy `makeTemple()` chooses its ordinary room budget as `Next((int)(scale * 10), (int)(scale * 16))`, where `scale = Main.maxTilesX / 4200.0`;
- Dual Dungeon's Temple biome-room inner size is `(int)(50f * (Main.maxTilesX / 4200f))`;
- the three vanilla painting caps correspond closely to roughly one Lihzahrd painting per ten ordinary legacy Temple rooms: expected room counts are 12.5, 19, and 25.5 while painting counts are 1, 2, and 2/3.

Expanded Worlds therefore preserves vanilla Large's **existing single `Next(2)` roll** and continues only the deterministic base. The base is:

`floor(expected ordinary legacy Temple room count / 10)`

where the expected ordinary room count is derived from the exact unmodified width formula. Secret-seed room multipliers are intentionally excluded because vanilla's painting cap also ignores them.

| World | Lihzahrd painting cap |
| --- | ---: |
| Large | 2-3 (vanilla) |
| THICC | 3-4 |
| THICC 2 | 3-4 |
| THICC 3 | 4-5 |
| THICC 4 | 5-6 |
| THICC 5 | 5-6 |
| THICC 6 | 6-7 |
| THICC 7 | 7-8 |
| THICC 8 | 7-8 |
| THICC 9 | 8-9 |
| THICC 10 | 9-10 |
| THICC 11 | 9-10 |

The exact same helper is used for the Dual Dungeon and legacy `templePart2()` paths, preventing the two implementations from drifting apart. No additional RNG call is introduced.

## Rule 4: unresolved ambiguous tables stop at Large

If neither the local terms nor surrounding source math produce a strong enough continuation, Expanded Worlds retains vanilla Large behavior. A promoted inference must have its rationale documented here and deterministic tests locking the intended values.

## Rule 5: file/network-schema ceilings are not redesigned

`RandomizeTreeStyle` and `RandomizeCaveBackgrounds` visibly progress from two to three to four regions, but Terraria 1.4.5.8 stores exactly three X boundaries and four styles in its current world/runtime/network model. Expanded Worlds therefore retains the Large four-region representation rather than inventing an incompatible format.

## `GetWorldSize()` and direct-size audit

Every 1.4.5.8 worldgen/runtime `GetWorldSize()` use was classified, and direct 4,200/6,400/8,400-style world-size branches were separately checked so legacy copies are not missed:

- TerrainPass: Small-specific adjustment; expanded worlds naturally take the Large path.
- ExtraSpawnPointManager: Small-specific spacing; expanded worlds naturally take the Large path.
- DungeonGlobalBookshelves: exact `5/10/15`; continued.
- DungeonGlobalGroundFurniture (both implementations): exact `5/10/15`; continued.
- DungeonGlobalEarlyDualDungeonFeatures first switch: exact sequences continued; contextual Orb/Heart `8/14/18` promoted from the vertical-section rule.
- DungeonGlobalEarlyDualDungeonFeatures flooded-pit switch: exact `2/4/6`; continued.
- DungeonGlobalPaintings: Large's single `Next(2)` roll is preserved; deterministic base continued from width-derived Lihzahrd geometry.
- DungeonGlobalTraps: exact base/range progressions; continued.
- DualDungeonLayoutProvider specialized rooms: exact sequences continued; contextual Spider `2/6/8` promoted from the vertical-section rule.
- DualDungeonLayoutProvider specialized halls: exact `3/4/5`; continued.
- WorldGen Boulder Pet trap: exact `2/4/6`; continued before vanilla No Traps multiplier.
- WorldGen Dirtiest Block: exact `3/6/9`; continued before vanilla Celebration multiplier.
- WorldGen Spike Caves: exact base `3/5/7`; continued before vanilla `Next(2)`.
- WorldGen Chillet Eggs: exact `6/9/12`; continued.
- `WorldGen.templePart2()`: direct width-gated Lihzahrd painting cap is continued by the same helper as Dual Dungeon.
- UI world-size selection: remains vanilla Large categorically; no fake enum value.

Glow Tulips use equivalent explicit physical-width categorization rather than `GetWorldSize`; their `2/4/6` table is continued by the same rule.

The remaining direct size branches for tree-style and cave-background region schemas are intentionally retained at vanilla Large because their backing world/network representation has a fixed number of boundaries/styles.

## Capacity-only exceptions

Six fixed current-source bookkeeping/scratch stores or guards can be exceeded by otherwise valid canonical expanded generation:

- Floating Island metadata arrays: enlarged only as necessary for Terraria's own worst-case generated record count.
- `WorldGen.heartPos`: enlarged only as necessary for Terraria's own Crimson record count.
- Mountain Cave `mCaveX/mCaveY`: enlarged only as necessary for Terraria's own Remix-scaled attempt count.
- Surface Tunnel tracking: backing storage and its sentinel grow only enough for Terraria's own width formula.
- Surface Ore tracking: backing storage and its sentinel grow only enough for Terraria's own width formula.
- Minecart `TrackGenerator._history`: its constructor allocation grows only when the explicit WorldWidth-scaled `LongTrackLength` maximum plus Terraria's existing 100-entry tail reserve exceeds the retail 4,096 entries. THICC 11 therefore needs 7,623 entries for a 7,523 maximum requested long track plus the unchanged reserve.

No content count or RNG decision is made by the capacity code. Minecart history is generation-only scratch data, not part of the `.wld` format; existing worlds and existing tracks are unaffected.

## Non-worldgen support

The remaining patches are mechanical support for a larger legal canvas:

- apply physical dimensions before `clearWorld`;
- allocate tile/map/section storage using Terraria's exact formulas;
- extend the client MapRenderer's fixed render-target ceiling;
- present THICC through THICC 11 names and buttons;
- use vanilla Large's copied-seed category prefix because Terraria exposes only three size categories.

Client and server compile the same generation context, tier continuations, inferred promotions, and capacity guards so the same seed/preset does not have two Expanded Worlds rule sets.
