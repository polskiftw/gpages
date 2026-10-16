# Maze Lab v4 — deeper branches, fewer short dead ends

**Live browser viewer:** https://polskiftw.github.io/gpages/maze-simulator/

A seeded, finite multi-floor *global* labyrinth. A plain global grid of
navigation cells is the only source of truth. It has no chunk-local mazes
or generation boundaries; the viewer draws a local window of the completed map.

## The problem v4 addresses

v3's mixed mazes produced many one- to four-cell dead-end spurs: little comb
teeth that were not interesting to explore. v4 measures **dead-end depth** along
the passage graph from each tip up to the next junction and deliberately
changes how passages are built.

Three candidates, each using the same global cell graph:

- **Deep branches** (default): a seeded *stroke grower* extends routes in
  batches of 9–24 cells with some lookahead. It prunes short terminal spurs
  (up to five cells), then selectively reconnects certain remaining stubs
  where an alternative detour is genuinely long.
- **Connected detours**: a second stroke-growth setting (7–17 cells) with
  somewhat less solid masonry, additional sparse long detours and moderate
  pruning of spurs of four cells or fewer.
- **Wandering branches**: a mixed growing-tree traversal with short spurs
  up to six cells removed. Its corridors turn frequently, avoiding the
  exceptionally long *straight* hallways of the old winding preset.

Unlike simply braiding every dead end into a tiny square, the main new
operation is **pruning**: when a short terminal chain meets a real junction,
remove the cells in that chain from navigable space, convert them to solid
wall mass (bit 64), and close the junction connection. Each removed cell is
a leaf at removal, so this preserves connectivity of every remaining cell.
The graph retains longer dead-end branches. Gray on SVG maps means impassable
masonry — a real topology decision, not a decorative background.

Some extra edges reconnect a surviving leaf to another path only when
the existing route around the proposed loop is more than nine steps.
Sparse other connections require a loop longer than eight steps. No chunk
boundaries influence either operation.

## Guaranteed / tested

- Every remaining navigable cell on every floor is reachable.
- Every opening is reciprocal and no boundary has an external opening.
- Every adjacent level pair is connected; vertical graph edges are preserved
  during pruning.
- The same seed, dimensions, style and engine version regenerate byte-for-byte
  identical passages, masonry, endpoints and statistics.
- A random deterministic start, and a seeded distant exit in another floor
  when the world has multiple floors.
- One byte per cell stores its directional opening bits or SOLID (64).
- Nothing depends on the size of the viewport or how rendering is chunked.

The browser currently builds up to **4,194,304 cells in RAM**, with additional
temporary working arrays; rendering windows are cheap but initial generation
requires a complete global pass. It is not a billion-cell on-demand generator.

**Stair markers are still abstract reciprocal 3D graph connections**, not yet
physically validated stair flights, headroom, or underpass geometry.

## Build-free inspection

Pick a seed, dimensions, floors and one of the three styles. Navigate the
true top-down geometry, change floors, inspect dead-end statistics, export
whole-floor SVG (where small enough), or copy a reproducible URL. The small
style cards are independent miniature mazes using the same production math.

Node.js 22+ can run the regression suite and export original math diagrams:

    node --test tests/maze.test.mjs
    node examples.mjs /tmp/maze-samples

CI verifies reachability, reciprocal connections, deterministic reproduction,
solid-block safety, short-dead-end reduction and independence from 7/16/24/32/64
render windows, then publishes five SVG floor plans plus metrics.json.
The previous v3 version remains available through Git history.

## Elevation-aware staircase math experiment

**[Open Natural Staircases](./elevation/)** or
https://polskiftw.github.io/gpages/maze-simulator/elevation/

This is a *separate*, working mathematical prototype that does not alter the
approved **Connected Detours** 2D generation. It explores the graph/geometry
needed to give that labyrinth ordinary physical, multistory stair passages.

Every edge moves one X or Y grid step. It may also change Z by -1, 0 or +1;
there are no same-X/Y vertical teleports. Consecutive edges can climb three,
four, or more floors with **no automatically inserted landing**. A physical
landing emerges from the graph wherever passages end, turn, or branch.
Any apparent L- or U-turn is an incidental sequence of independently grown
edges, **not a prefab staircase structure**.

A 7m horizontal step and 3.5m vertical story have a 26.6-degree grade.
Mathematical reservations keep each 1.5m-wide passage ribbon clear of
other ribbons across the same projected segment, with 2.1m headroom and
a 0.25m slab allowance. Additional conservative grid-pitch clearance
allows orthogonal ribbons stacked at different elevations to pass safely.
These are assumptions of this particular prototype, not user-mandated scale.

The separate gallery displays each true floor's edge topology, highlights
continuous no-landing stair runs, computes an actual side elevation profile,
and exports the seeded **3D graph geometry JSON**. A single connected graph
and symmetric reciprocal movements are verified in CI, along with the
presence of RNG-generated climbs crossing at least three story boundaries.

Physical 3D meshes, wall/ceiling collision geometry, and final integration
with Connected Detours remain future steps; this preview does **not**
misrepresent those as finished.
