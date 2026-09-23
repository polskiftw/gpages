# Expanded Worlds

Expanded Worlds extends Terraria 1.4.5.8's normal world-size cadence through **THICC 11** without replacing Terraria's generator.

> **Expanded Worlds supplies the bigger vanilla-shaped canvas. Terraria supplies the world.**

## Canonical size ladder

Terraria network sections are `200 x 150` tiles. Vanilla uses `21x8`, `32x12`, and `42x16` sections for Small, Medium, and Large. Expanded Worlds continues the same cadence: horizontal section jumps repeat `+11, +10`; vertical sections always add `+4`.

| Overall tier | Name | Tiles | Network sections |
| ---: | --- | ---: | ---: |
| 1 | Small | `4,200 x 1,200` | `21 x 8` |
| 2 | Medium | `6,400 x 1,800` | `32 x 12` |
| 3 | Large | `8,400 x 2,400` | `42 x 16` |
| 4 | **THICC** | **`10,600 x 3,000`** | **`53 x 20`** |
| 5 | **THICC 2** | **`12,600 x 3,600`** | **`63 x 24`** |
| 6 | **THICC 3** | **`14,800 x 4,200`** | **`74 x 28`** |
| 7 | **THICC 4** | **`16,800 x 4,800`** | **`84 x 32`** |
| 8 | **THICC 5** | **`19,000 x 5,400`** | **`95 x 36`** |
| 9 | **THICC 6** | **`21,000 x 6,000`** | **`105 x 40`** |
| 10 | **THICC 7** | **`23,200 x 6,600`** | **`116 x 44`** |
| 11 | **THICC 8** | **`25,200 x 7,200`** | **`126 x 48`** |
| 12 | **THICC 9** | **`27,400 x 7,800`** | **`137 x 52`** |
| 13 | **THICC 10** | **`29,400 x 8,400`** | **`147 x 56`** |
| 14 | **THICC 11** | **`31,600 x 9,000`** | **`158 x 60`** |

There is deliberately **no THICC 12**. The next canonical tier would be `33,600 x 9,600`, and width `33,600` crosses Terraria's signed 16-bit positive coordinate ceiling of `32,767`. THICC 11 is the hard stop for this design.

## Naming compatibility

The public names `XL` and `Huge` are retired. Existing world files need no migration because compatibility is based only on the dimensions stored in the normal `.wld` header:

- former `XL` `10,600 x 3,000` -> **THICC**;
- former `Huge` `12,600 x 3,600` -> **THICC 2**;
- former `THICC` `14,800 x 4,200` -> **THICC 3**.

No custom world-size identifier and no custom `.wld` format are introduced.

## Vanilla-continuity policy

The source authority is the clean matching **Terraria 1.4.5.8 retail decompile**.

Expanded Worlds follows these rules:

1. **Physical formulas remain Terraria's formulas.** Width, height, area, `maxTilesX / 4200.0`, integer divisions, and other physical expressions see the real selected canvas.
2. **Exact Small/Medium/Large sequences may continue mechanically.** The same tier math now runs through overall tier 14.
3. **High-confidence contextual continuations stay isolated.** They live in `InferredTierContinuity.cs`, with source-shape guards and tests.
4. **Fixed storage may grow without changing generation policy.** Capacity patches only remove a bookkeeping ceiling that Terraria's own formula can legitimately hit.
5. **Ambiguous or schema-limited categories remain vanilla Large.** No fake extrapolation and no new file/network schema.

Expanded worlds also remain **categorically Large** inside Terraria: `WorldGen.GetWorldSize()` must still return `2`. The mod carries the physical THICC preset separately and applies the actual dimensions only at the generation/allocation boundary.

## Exact discrete continuations

The arithmetic continuations are functions of the overall tier, so they naturally extend through THICC 11. Examples:

| Rule | Source Small/Medium/Large | Continuation |
| --- | --- | --- |
| Sky-lake base | `1, 2, 3` | `tier` |
| Statue multiplier | `2, 3, 4` | `tier + 1` |
| Glow Tulips | `2, 4, 6` | `2 * tier` |
| Boulder Pet base quota | `2, 4, 6` | `2 * tier` |
| Spike Cave base | `3, 5, 7` | `2 * tier + 1`, then vanilla `Next(2)` |
| Chillet Eggs | `6, 9, 12` | `3 * tier + 3` |
| Dirtiest Block base | `3, 6, 9` | `3 * tier` |

The 1.4.5 Dual Dungeon arithmetic tables follow the same rule. Secret-seed multipliers and random rolls remain in Terraria's code after these base values.

### Promoted contextual rules

Two Dual Dungeon values have enough surrounding evidence to continue:

- Shadow Orb / Crimson Heart quota: Small is a compact exception; Medium+ follows `verticalSections + 2`.
- Spider specialized-room quota: Small is a compact exception; Medium+ follows `verticalSections / 2`.

Lihzahrd paintings use a separate geometry-backed continuation. Vanilla Large's existing randomized `2 or 3` result and its single `Next(2)` call are preserved; Expanded Worlds changes only the deterministic base, derived from Terraria's own width-scaled Temple room geometry. The resulting caps progress from `3-4` on THICC to `9-10` on THICC 11, with no additional RNG call. The exact per-tier values and derivation are documented in `VANILLA-CONTINUITY-AUDIT.md`.

## Fixed-capacity audit through THICC 11

The 1.4.5.8 source audit found six bookkeeping/scratch stores that can be exceeded at the hard-stop dimensions. Those are expanded without changing placement counts or RNG:

| Store | Retail capacity/guard | THICC 11 source-derived requirement |
| --- | ---: | ---: |
| Floating Island metadata arrays | `300` | `890` worst-case Error World + full extra-island multiplier |
| `WorldGen.heartPos` | `100` | `232` worst-case Remix Crimson record bound |
| Mountain Cave `mCaveX/mCaveY` | `30` | `46` worst-case Remix attempts |
| Surface Tunnel tracking | effective `49` records from sentinel `50` | `70` records, sentinel `71` |
| Surface Ore tracking | effective `49` records from sentinel `50` | `74` records, sentinel `75` |
| Minecart `TrackGenerator` path history | `4,096`, with final `100` entries reserved | `7,623` = `7,523` scaled long-track maximum + the same `100`-entry reserve |

Minecart history is generation-only scratch storage and is not written to `.wld` files. THICC 4 is the first tier where Terraria's own WorldWidth-scaled maximum plus its existing 100-entry reserve exceeds `4,096`; existing worlds are therefore untouched, while newly generated THICC 4+ worlds can realize the longer tracks Terraria requested instead of clipping against retail scratch headroom.

Audited fixed stores that still remain below retail capacity at THICC 11 are left untouched:

- lakes: at most `44` tracked records vs capacity `50`;
- mushroom biomes: at most `46` vs `50`;
- oases: at most `16` vs `20`;
- jungle shrine positions: at most `83` vs `100`;
- bee larvae: worst audited secret-seed bound `82` vs `100`.

`Main.chest` remains the retail `8,000`-slot contract. The source audit does not justify changing the entire chest serialization/network/runtime contract merely because worlds are larger. The Windows stress matrix records the realized chest count for every THICC tier so a real approach to that limit is visible instead of being guessed at.

## Backing storage

World-sized backing storage follows the selected dimensions:

- `Main.tile` is allocated at the exact physical width and height;
- tile-area arithmetic used by the mod is checked 64-bit (`THICC 11 = 284,400,000` logical tiles);
- client `WorldMap` is recreated at the exact physical dimensions when needed;
- section tables use retail `maxTilesX / 200 + 1` by `maxTilesY / 150 + 1` sizing;
- startup/static section tables are provisioned against the maximum supported `31,600 x 9,000` canvas;
- already-created `RemoteClient` section tables are resized at the world boundary.

## Client map renderer

Retail `MapRenderer` has a fixed `5 x 2` target grid. Normal targets cover `2,000 x 1,800` tiles, while its final allocated column and row are special `400 x 600` targets.

THICC 11 needs a **16 x 5 logical target grid**. Its physical final column is `1,600` tiles wide and its final row is a full `1,800`, so both physical edges must stay on normal-size targets. One unused guard column and one unused guard row preserve retail `checkMap` behavior, giving a **17 x 6 backing grid**. `DrawMap` is extended only through the final real X target, index `15`.

This is a rendering/storage accommodation, not a worldgen rule.

## 64-bit requirement

The THICC ladder is an **x64 runtime feature**. THICC 11 alone contains `284,400,000` logical tiles; a 32-bit Terraria process is not a viable runtime for these canvases.

Supported gloader path:

- `gloader.exe` on .NET 10;
- private x64 Terraria runtime under `gdeps/x64-runtime/TerrariaRelease.dll`;
- dedicated server through `gloader.exe --server`.

There is no Linux runtime target for this ladder.

## Dedicated server selector

Set `GLOADER_EXPANDED_WORLD` to any of:

```text
THICC
THICC2
THICC3
THICC4
THICC5
THICC6
THICC7
THICC8
THICC9
THICC10
THICC11
```

Spaces are also accepted in display-style names such as `THICC 11`. Retired `XL` and `HUGE` selectors are rejected.

## CI and stress validation

Fast continuity CI verifies:

- the exact 11-entry size table and dimension lookup;
- section cadence through overall tier 14;
- the tier math and promoted inferences through tier 14;
- capacity upper bounds and safe retail capacities, including minecart path-history headroom;
- map logical/backing target math, including THICC 11 `16x5` / `17x6`;
- the signed-coordinate hard stop;
- server selector parsing and retired-name rejection;
- syntax parsing of every Expanded Worlds source file in both client and server modes;
- exact retail source shapes when the private Terraria binary input is configured.

A separate manual Windows x64 stress workflow generates all **11** THICC tiers independently with seed `1337420`, saves, reloads, verifies exact dimensions, and records generation time, peak memory, `.wld` size, and chest count. The matrix uses `fail-fast: false`; one giant tier failing does not hide the others.

The canonical acceptance run on **2026-09-04** completed successfully: **11 / 11 THICC tiers passed generation, save, reload, and exact-dimension verification**. THICC 11 itself generated in `3072.459 s` (about `51.21 min`), peaked at `11.82 GiB` working set / `12.04 GiB` private memory, produced a `156.1 MiB` `.wld`, used `3,855 / 8,000` chest slots, and reloaded at the exact `31,600 x 9,000` dimensions.

See [`THICC-STRESS-RESULTS.md`](THICC-STRESS-RESULTS.md) for the complete per-tier acceptance table and run identity.

## What Expanded Worlds does not do

It does **not** replace/reorder worldgen passes, reseed Terraria's RNG, force biome sides, post-process completed worlds, invent a custom terrain generator, invent a new `.wld` format, or make fake Terraria size enums.

Different physical sizes can therefore produce visibly different geography from the same seed. That is normal Terraria behavior: each size is a fresh run of the original generator on a different canvas.
