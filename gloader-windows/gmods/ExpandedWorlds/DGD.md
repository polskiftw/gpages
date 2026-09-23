# Expanded Worlds - DGD

## The buttons

```text
Small       4200 x 1200
Medium      6400 x 1800
Large       8400 x 2400
THICC      10600 x 3000
THICC 2    12600 x 3600
THICC 3    14800 x 4200
THICC 4    16800 x 4800
THICC 5    19000 x 5400
THICC 6    21000 x 6000
THICC 7    23200 x 6600
THICC 8    25200 x 7200
THICC 9    27400 x 7800
THICC 10   29400 x 8400
THICC 11   31600 x 9000
```

`XL` and `Huge` are dead names. Old worlds are not rewritten: their stored dimensions simply map to the new names.

```text
old XL     10600 x 3000 -> THICC
old Huge   12600 x 3600 -> THICC 2
old THICC  14800 x 4200 -> THICC 3
```

## Why those stupid-looking numbers

Terraria splits the world into `200 x 150` tile network sections.

```text
Small    21 x  8
Medium   32 x 12
Large    42 x 16
THICC    53 x 20
THICC 2  63 x 24
THICC 3  74 x 28
THICC 4  84 x 32
THICC 5  95 x 36
THICC 6 105 x 40
THICC 7 116 x 44
THICC 8 126 x 48
THICC 9 137 x 52
THICC10 147 x 56
THICC11 158 x 60
```

Horizontal jumps repeat `+11, +10`. Vertical always adds `+4`. We keep Terraria's own slightly wobbly width/height cadence instead of inventing prettier dimensions and then writing a pile of fake geometry compensation.

## Why THICC 11 is the end

The next correct tier is:

```text
168 x 64 sections = 33600 x 9600 tiles
```

Terraria still has signed 16-bit coordinate paths whose positive ceiling is `32767`. `33600` crosses it. So **THICC 11 is the hard stop**. No THICC 12 unless the underlying coordinate contract is redesigned first.

## The main rule

**If Terraria already has a physical formula, leave it the hell alone.**

Width formula? It sees the real width.

Height formula? Real height.

Area formula? Real area.

Floating point? Keep it floating point.

Integer division? Keep the integer division.

The mod is allowed to continue a Small/Medium/Large lookup only when the sequence is source-backed and defensible. Weird/ambiguous stuff stays vanilla Large.

## Terraria still thinks every THICC world is Large

This is intentional.

`WorldGen.GetWorldSize()` must still return `2` for every THICC tier. We do **not** add fake Terraria size enums. The mod remembers the selected physical tier separately, applies the real dimensions at generation/allocation time, and lets Terraria keep using its normal Large category everywhere that does not have a defensible continuation.

## Exact sequences

These extend by overall tier all the way through tier 14:

```text
Sky lakes:       tier
Statue mult:     tier + 1
Glow Tulips:     2 * tier
Boulder Pet:     2 * tier
Spike base:      2 * tier + 1, then Terraria still does Next(2)
Chillet Eggs:    3 * tier + 3
Dirtiest Block:  3 * tier
```

The obvious 1.4.5 Dual Dungeon sequences do the same thing. The formulas are in code/tests; README has the policy.

## High-confidence weirdos

Two Dual Dungeon tables are weird at Small but become obvious from Medium onward:

- Orb/Heart quota = `vertical sections + 2` from Medium onward;
- Spider specialized rooms = `vertical sections / 2` from Medium onward.

Lihzahrd paintings **do** grow on expanded worlds, but only from source-backed Temple geometry. Vanilla Large still rolls `2 or 3` with one `Next(2)`. Expanded Worlds keeps that exact roll and changes only the deterministic base using Terraria's own width-scaled Temple room budget. The caps are:

```text
Large     2-3
THICC     3-4
THICC 2   3-4
THICC 3   4-5
THICC 4   5-6
THICC 5   5-6
THICC 6   6-7
THICC 7   7-8
THICC 8   7-8
THICC 9   8-9
THICC 10  9-10
THICC 11  9-10
```

No extra RNG call. Full derivation is in `VANILLA-CONTINUITY-AUDIT.md`.

## Fixed arrays that actually break

At THICC 11 the source audit says these retail bookkeeping/scratch limits are genuinely too small:

```text
Floating Island metadata: 300 -> need up to 890
Crimson heartPos:          100 -> need up to 232
Mountain Cave records:      30 -> need up to 46
Surface Tunnel tracking:    49 usable -> need 70 (sentinel 71)
Surface Ore tracking:       49 usable -> need 74 (sentinel 75)
Minecart track history:    4096 -> need 7623 (7523 max track + same 100-slot reserve)
```

We only enlarge the bookkeeping/scratch space. Terraria still decides how many things to generate, where they go, how long tracks are allowed to be, and which RNG calls happen.

Minecart history is the especially dumb one: Terraria explicitly scales long-track length with world width, but its private path scratch array was sized with huge unused headroom for vanilla Large instead of dynamically. THICC 4 is the first tier where `max track + 100 reserve` crosses 4096. The fix only grows that temporary generation array. It does not touch existing `.wld` files or existing tracks.

These are still safe at THICC 11 and therefore stay vanilla:

```text
Lakes:          44 max / 50 slots
Mushrooms:      46 max / 50
Oases:          16 max / 20
Jungle shrines: 83 max / 100
Bee larvae:     82 max / 100
```

The global chest array stays at Terraria's `8000`. Do **not** casually resize it: that number is part of more than one chest runtime/serialization/network contract. The stress workflow records actual chest counts so we get evidence before ever touching it.

## Big memory stuff

THICC 11 is:

```text
31600 * 9000 = 284,400,000 logical tiles
```

That is why this is an x64-only feature. The supported path is the .NET 10 `gloader.exe` with the private x64 Terraria runtime in `gdeps/x64-runtime/TerrariaRelease.dll`. Server is `gloader.exe --server`.

No 32-bit heroics. No Linux runtime target.

## Map renderer nonsense

Terraria's map renderer is separately hard-coded for a `5 x 2` target grid. Normal chunks are `2000 x 1800`, except the **final allocated** chunk is special `400 x 600`.

THICC 11 really needs:

```text
logical map targets: 16 x 5
```

Its real right edge is `1600` tiles wide and its bottom row is a full `1800`, so neither edge may accidentally become the retail `400 x 600` special final target. The clean trick is to leave one unused guard column and row:

```text
backing map targets: 17 x 6
real final X target: index 15
```

That preserves the stupid retail behavior instead of rewriting MapRenderer from scratch.

## Server names

`GLOADER_EXPANDED_WORLD` accepts:

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

Display-style spaces such as `THICC 11` are okay too. `XL`, `HUGE`, and `THICC12` are supposed to fail.

## OCD check

Fast CI locks the exact 11-tier table, dimensions-to-name lookup, section cadence, tier math through 14, capacity bounds including minecart track history, map grid, signed-coordinate hard stop, selector parsing, and client/server syntax. When the private retail input is available it also checks the exact TrackGenerator/WorldGenRange source shape behind the minecart fix.

The separate manual Windows stress matrix runs **all 11 THICC tiers independently** with seed `1337420`, `fail-fast: false`, and records:

```text
generation time
peak memory
.wld size
chest count
save/reload dimension verification
```

The canonical **2026-09-04 run #4 went 11 / 11 green**. Every THICC tier generated, saved, reloaded, and came back with the exact expected dimensions.

The stupidest one, THICC 11, did this:

```text
31600 x 9000
3072.459 seconds generation (~51.21 min)
11.82 GiB peak working set
12.04 GiB peak private memory
156.1 MiB .wld
3855 / 8000 chests
reload verified: YES
```

So THICC 11 is no longer theoretical. The supported x64 path has actually eaten the full 284.4-million-tile canvas and survived. The next tier is still forbidden because `33600` crosses Terraria's signed-coordinate wall, not because we chickened out on the stress test.

Full receipts live in `THICC-STRESS-RESULTS.md`.
