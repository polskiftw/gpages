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

## Stair rarity adjustment — elevation-lattice-2

The v1 stair experiment was far too stair-heavy. Even its lowest setting
produced around **26% sloped passage connections** (the old default was near
30%). The generator now uses a **hard budget per neighboring floor pair**,
plus sparse deterministic gates for the *beginning* of a stair run. A flight
already climbing may continue up successive stories without a forced landing.

The stair frequency setting is **1–8**, default **2**. It is a maximum budget of
roughly `setting% × (width × height)` staircase edges per adjacent floor
pair, not a percentage of every outgoing move. The actual percentage is
shown on the page, along with the number of staircase edges. The graph also
keeps floor-to-floor connections even on unusually small seeds through a
bounded, clearance-tested recovery bridge.

On the reproducible 28×28×6 benchmark at seed `LOST-6549`, measured stairs
were **0.72% at setting 1, 1.41% at setting 2, and 3.15% at setting 8**
of all undirected navigable edges. This retains occasional multistory climbs
without making staircases the dominant means of travel. Extra alternative
route connections are deliberately planar rather than generating more ramps.

## Connected Detours 3D — integrated mathematical preview

**[Open the new unified browser viewer](./unified/)** ·
https://polskiftw.github.io/gpages/maze-simulator/unified/

The formerly separate corridor and stair experiments now have one tested,
seeded 3D navigation graph, **ConnectedDetours3D**. The older pages remain
available for comparisons rather than being deleted.

The integrator runs the approved **Connected Detours v4** on each *entire*
horizontal floor. It preserves every horizontal passage bit, every retained
wall, the deeper cul-de-sac distribution and the braiding algorithm exactly.
There are no chunk-sized submazes.

It then makes each floor reachable from the next via very sparse,
collision-checked, reciprocal **real X/Y/Z slope edges**. Additional stair
routes join existing floor corridors, which already contain the useful
long-range detours. Some stair runs start at a level corridor, pass through
previously solid *intermediate floor cells* without branching, and end at a
different floor's corridor: a genuine uninterrupted multi-story climb with
no compulsory landing or predefined L/U staircase template. Only the
randomly generated sequence of edges determines its shape.

- Setting 1-8 imposes an actual maximum per floor-pair; default 2.
- Randomized endpoints are in different floors and at least 85% of the
  maximum cross-floor *shortest-path graph distance* apart.
- Uninterrupted ramp interiors are protected from branches being attached
  later; ordinary floor junctions remain free to branch and turn.
- Physical XYZ positions are scaled as 7 m per horizontal step and 3.5 m
  per story, with 1.5 m corridor width, 2.1 m standing clearance and
  0.25 m slab allowance. Co-projected route edges cannot intersect at
  incompatible heights; full final mesh/collision rendering is NOT done yet.
- The viewer offers full-floor and cropped/pannable SVG, actual stair runs,
  seeded link sharing, and an exact navigable-edge JSON export.

Run with Node.js 22+:

    node --test tests/combined.test.mjs
    node unified/examples.mjs /tmp/combined-maze

CI independently checks 16 worlds across four seeds, small and large
dimensions, exact horizontal-bit preservation, all-level connectivity,
reciprocity, absence of non-Euclidean teleports, stair rarity, protected
uninterrupted multistory climbs, clearance and reproducible generation.

**Measured on LOST-6549, 28x28x6, frequency 2:** 35 stair edges out of
3,585 connected 3D edges (**0.98%**). Original Connected Detours median
dead-end depth by floor: 9/8/9/9/9/8 cells. At least one automatically
generated uninterrupted stair run climbs three levels. Those are real
topology statistics, not artistic mockups.

This is now the strongest base for a subsequent 3D renderer. We have **not**
replaced the existing original maze version or claimed that wall/ceiling
collision meshes have been completed.

## Landings versus uninterrupted staircases

**A straight staircase continuing uphill at the same slope, without an
alternate choice, is one uninterrupted flight.** Crossing a numeric floor
height does not automatically require a landing. A real landing occurs at
a change of heading, a usable branch, a transition to a level corridor,
or a change from ascending to descending.

The reusable elevation engine now classifies stair-bearing nodes as
continuous-flight or landing straight from the graph. SVG maps omit
the square platform at continuous-flight nodes, but mark genuine landings
with a hollow square. The exported 3D navigation JSON includes both sets
of exact coordinates so a future wall/floor/collision renderer can apply
the same decision without inventing floor-boundary platforms.

CI tests straight uninterrupted flights, turning stairs, real junctions,
transitions to level travel, and generated unified worlds. It also checks
for unintended straight stair -> one flat step -> same-direction stair
interruptions with no real alternative route.

These are 3D movement/geometry semantics, not yet full collision meshes.

## Enclosed passage geometry and hidden stair overpasses

Every direction from every navigable position can connect to at most **one**
of level, ascending or descending travel. Staircases ARE corridor segments:
there is no second walkable path around them within the same heading. The
topology is sealed by default; a doorway/corridor opening exists only where
a reciprocal graph edge explicitly permits it.

The mathematical passage envelope has 1.5m width, 2.1m headroom and 0.25m
floor thickness. The existing real-world XYZ graph uses 7m horizontal
spacing and 3.5m story spacing. When projected passage centerlines coincide
in X/Y, independent routes can lie over each other **only when the upper
passage's underside stays above the lower passage's standing clearance
across the entire segment**. A flat corridor below a stair is an ordinary
valid result, not a secret tunnel prefabricated into the generator. Neither
route has an opening to the other at their projected crossing.

The shared elevation engine now exports **actual overpass records**:
projected XY endpoints, lower/upper route type (flat or stair), level
coordinates and minimum available headroom. The full 3D JSON export
includes these records, but does not invent actual collision meshes.
The UI lists their count. An independent clearance check for perpendicular
ribbons around a shared lattice vertex uses the worst-case grade across
the corridor width: STORY - (STORY/PITCH)*WIDTH >= HEADROOM + SLAB.

On the original LOST-6549 integrated 28 x 28 x 6 example, the math detects
**50 clearance-safe stacked flat/stair shared XY segments** with no
interconnections. Across 12 reproducible regression worlds it detected
668 such stacked overpasses. New unit tests also prove a manually
constructed upper stair and lower corridor can share exactly the same
XY footprint without being connected or colliding, and that level and
ascending edges cannot coexist in one direction from a single position.

These are enforceable mathematical properties; the future renderer must
derive sealed wall/floor/ceiling meshes from the same graph and not
create unintended windows, bypasses or openings.

## First-person walkable renderer - v9

**[Enter the 3D labyrinth](./walk/)** or
https://polskiftw.github.io/gpages/maze-simulator/walk/

This is a functional WebGL browser prototype of the original Connected
Detours 3D maze, with working physical navigation - not concept art.
It consumes the *unchanged* ConnectedDetours3D math and has no external
runtime libraries, imported meshes, images or downloaded textures.

The mesh generator (walk/mesh.mjs) converts each original graph edge into a
sealed 3D corridor tube: two opaque brick sidewalls, a floor, a ceiling
and the underside of the ceiling/floor slab. The floor is ordinary stone
on a level corridor; on a staircase it takes the exact continuous grade
between mathematically connected nodes, with a repeating tread pattern.
Stairs are therefore mandatory sections of passages, never an object that
can be bypassed.

At each non-through-flight node it generates an enclosed square landing
with solid brick faces in every direction without a graph edge. Corridor
tube sidewalls STOP at the landing boundary rather than extending through
junctions and blocking a real turn. Consecutive slope edges classified as
uninterrupted stair flights do not receive unnecessary landing pads.
Independent stacked paths remain enclosed and visually isolated.

The collision solver (walk/navigation.mjs) samples these very same
corridor and landing envelopes. It checks the player's current height,
so overlapping X/Y corridors on different floors cannot become
accidental teleports. Wall directions cannot be passed; speed and frame
length are broken into substeps to avoid tunneling through solids.
Ascending physically changes the player's Y coordinate rather than
jumping immediately to the next floor.

Native WebGL1 (walk/webgl.mjs) renders the actual triangles with a
first-person perspective and fog. Four deliberately simple repeating
shader materials are used: brick, stone floor, stone ceiling and a
stair tread/slope surface. No prefab stair models, rooms or architecture
are inserted. A local render cache rebuilds when entering an 8x8-cell
region (with extra fog margin); only the *rendered mesh* is chunked,
never the maze topology or seed.

Desktop: click to lock the mouse, WASD to walk, Shift to run, mouse to look,
Escape to release. On a phone, drag the scene to look and hold touch
buttons to move. A seeded entrance and distant exit, restart, URL sharing
and optional debug coordinates are implemented.

The initial browser implementation supports dimensions 6-64 by 6-64,
2-12 floors. Walkable slopes are continuous ramps with visually repeating
tread marks (not physically articulated stair steps); collision does
not yet support a free-jumping character, shadows, decorative details,
or complex 3D game physics. This is the first playable exploration
milestone, with graph-faithful movement and solid enclosures.

Run the regression and browser smoke tests:

    node --test tests/walk.test.mjs

GitHub Actions also launches real Chromium with WebGL on a local server,
walks forward from the generated entrance, resets the run, checks JS
and GPU errors, and uploads a screenshot from inside the maze.

## More meaningful decisions in Connected Detours — v10

The playable renderer, textures, collision solver and enclosed-passage
geometry are unchanged. The new elevation/choices.mjs enhances the
*entire original Connected Detours floor* before staircases are attached.

The original floor graph is preserved as the starting point, byte for
byte. Extra connections link *already navigable*, degree-two corridors.
Each new reciprocal edge creates two three-way junctions. It does not
introduce new dangling dead-end spurs, room prefabs, staircases or chunk
boundaries. Every new detour must be at least 12-14 cells longer through
the old graph: small four-cell braids are deliberately forbidden. Links
near terminal dead ends are rejected to preserve their medium/long depth.

Branching in both the playable maze and the mathematical floor-plan view
has three settings:

- Original routes (0): the exact old v1 floor layout with all old stair
  positions, endpoints and passages. Old saved URLs without a choices
  parameter continue to use this mode.
- More choices (1): a modest increase in decisions and long detours.
- Many choices (2, default): more branches per walkable area, emphasizing
  real route decisions rather than long directionless corridors.

The graph is seeded and reproducible for each mode. A new selected mode
is a deliberately different maze topology, not a change in graphics.

On LOST-6549 at 28x28x6, stair frequency 2:

| Metric | Original | Many choices |
|---|---:|---:|
| Junctions | 238 | 386 |
| Additional long detours | 0 | 74 |
| Mean cell distance to the nearest junction | 4.84 | 3.20 |
| Navigable cells | 3,542 | 3,542 |
| Stair passages | 35 | 35 |
| Existing leaf/dead-end tips | unchanged | unchanged |

CI compares 4 seeds in all 3 modes, verifies exact preservation of old
corridors, full 3D connectivity and headroom, terminal branch protection,
determinism, and genuine improvement in choice proximity. It also exports
true math SVGs of the original, moderate and many-choice layouts, and
uses Chromium to walk through the high-choice version and switch back
to the original without browser errors.

## Solid-gap connectors — shorter walks between meaningful junctions (v11)

The v10 Many choices layout remained too linear because it only opened
links between *already adjacent walkable cells*. It could not join two
corridors separated by unused solid masonry.

**Bridge corridor gaps (3, new default)** finds two existing degree-2
corridors separated by 1-4 completely solid floor cells, then generates
a genuine straight enclosed connector between them. Both ends become
three-way junctions and the intermediary tunnel positions have exactly
two opposite directions. It is not a detached branch, a prebuilt room,
a staircase prefab, or a render-chunk seam.

Constraints enforced by the generator and CI:

- The old route between the two prospective exits must already require
  at least 14 graph steps; do not create a tiny trivial loop.
- The first five positions of actual terminal dead-end stems are
  protected, so extra connectors don't chop down long cul-de-sacs.
- Every original horizontal opening stays open and reciprocal.
- The other floor's corridor and stairs remain enclosed and can still
  overlap vertically only with the required physical clearance.
- Once a gap is bridged, additional Many choices braids may join existing
  passages but cannot turn the new connector interior into a new branch.
- World seeds and 3D movement are tested with the same physics used in game.

After bridging, the old seeded many-choice step adds remaining nearby
long alternatives. Both operations derive from the one global floor
graph, so no 16-cell checkerboard submazes can reappear.

The Original (0), More choices (1) and Many choices (2) modes still
generate their exact prior layouts for any old seed. Shared URLs
without a branching mode still select Original. New worlds use
choices=3 by default, with the selection included in their share URL.

On LOST-6549, 28x28x6, stair frequency 2:

| Metric | Old Many choices (2) | Bridge corridor gaps (3) |
|---|---:|---:|
| Junctions | 386 | 486 |
| Mean graph distance to next junction | 3.20 cells | 2.54 cells |
| New useful solid-gap connectors | 0 | 78 |
| Formerly solid cells now navigable | 0 | 134 |

The CI suite validates each connector against the prior planar
network, checks every newly carved cell's degree and the two new
junctions, verifies old terminal dead ends are preserved, and walks
through a sample connector using the real first-person collision solver.
New floor SVGs are exported for all four branching modes.
