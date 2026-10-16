# Maze Simulator - procedural 3D maze engine (prototype)

**Status:** the mathematically tested on-demand **maze/staircase generator** now has an
independent **playable native 3D staircase laboratory** (SDL3 + OpenGL 3.3).
The laboratory is a small handcrafted room, **not yet the generated giant maze**.
The source is in this independent `gpages/maze-simulator/` folder; it does not
modify the GitHub Pages landing page, other utilities, or site navigation.

The goal is to make mazes far larger than RAM, with arbitrary finite dimensions,
multiple floors, connected corridors, reproducible worlds, and no requirement to
generate or save the entire maze up front.

## Math first: view exact top-down mazes (current priority)

**The procedural generator is the authoritative model.** The SDL3/OpenGL
test room is a secondary visualization prototype. Before integrating
3D into large worlds, we are comparing the actual mathematical layouts
and settling physical staircase footprints, stair/underpass layering,
rooms, corridor lengths and whole-world collision-safe connectivity.

The standalone `maze-preview` tool reads the original
`Maze::passages` bitmask and exports vector-accurate **SVG** floor maps.
It also generates a self-contained **offline HTML comparison gallery**
with multiple seeds and loop settings, plus switchable floors.

On Gentoo, from this directory:

    sh run-preview.sh

This builds the math-only C++ tools (no SDL3/OpenGL), creates
`previews/index.html`, and opens it in Firefox if installed. It never
starts a server, service, or daemon. Run only the generator if desired:

    cmake -S . -B build -DMAZE_BUILD_PLAYABLE=OFF
    cmake --build build --target maze-preview
    ./build/maze-preview gallery ./previews
    ./build/maze-preview svg ./custom.svg 48 32 3 6549 600 50 1

The custom example uses a 48 x 32 x 3 world and renders floor index 1
(zero-based). Gallery files can also be downloaded from GitHub CI and
opened directly in a browser. **These are real existing one-tile-wide
corridors.** There are no intentional rooms yet. U/D/B in previews
show only abstract vertical graph edges, not physically placed stairs.

Read [docs/MATH_FIRST.md](docs/MATH_FIRST.md) for the engine
architecture, unsolved geometry constraints and math-focused roadmap.

## Play the first 3D test room

The separate stair laboratory is a real Linux first-person executable with
brick walls and ceilings, 16 physical steps, a raised landing and a usable
ground-level underpass. Switch between all four combinations of hollow/solid
undercarriage and open/enclosed sides in real time. It tests the agreed geometry,
not procedural maze placement yet.

If SDL3 and OpenGL development support are already installed, run from this folder:

    sh run-playable.sh

WASD moves, mouse looks, Shift runs, R starts at the foot of the stairs,
U starts beside the underpass, 1/2/3/4 switch structural types, Esc frees
the mouse, and Q quits. The launcher compiles locally using your own
installed libraries; it neither installs software nor starts daemons.

Full details, including headless screenshot checks:
[docs/PLAYABLE.md](docs/PLAYABLE.md).

## Build on Gentoo / Linux

For the original topology CLI/tests, only a C++20 compiler is needed.
The separate native 3D laboratory additionally needs your existing SDL3/OpenGL development libraries.
Neither mode needs a server or daemon.

```sh
g++ -std=c++20 -O2 -Wall -Wextra src/maze.cpp src/main.cpp -o maze-cli
./maze-cli demo 20 10 3 6549 600 300 0
./maze-cli check 64 64 8 1234 0 0
./maze-cli bench 100000 100000 100 42 600 50 1000
./maze-cli probe 100000 100000 100 42 600 50 99999 99999 99
./run-tests.sh
./build/maze-stairs 64 64 50 6549 49 1 10000 3000 4000 # after CMake build
```

Or use CMake: `cmake -S . -B build && cmake --build build && ctest --test-dir build --output-on-failure`.
`run-tests.sh` creates temporary test binaries in `/tmp`; it does not install anything.

The original `maze-cli` commands take `width height floors seed loop_bps stair_bps` in that order,
followed by command-specific values. Defaults are `256 256 3 6549 600 50`.
Basis points are percentages with two decimal places: `600` = 6% additional
horizontal openings and `50` = 0.5% additional vertical shafts per cell/floor pair.

| Command | What it does |
| --- | --- |
| `demo ... [floor]` | ASCII floor plan for <=100 x 45 cells; shows vertical transitions |
| `check ...` | Whole-world reachability and perfect-tree check, capped at 250,000 cells |
| `probe ... x y z` | Get a cell's passage bits without generating the rest of the world |
| `bench ... [samples]` | Random-access timing across an immense finite world |

The standalone **`maze-stairs`** executable is a bounded inspector for the new
variable-height staircase network. Argument order: `width height floors seed
[max_rise [min_rise [density_bps [landing_bps [enclosed_bps]]]]]`. This can
show enclosed 23-story routes, staircases with selective floor entrances, and
long runs crossing procedural floor-band boundaries. See
[docs/STAIRCASES.md](docs/STAIRCASES.md).

The passage bitmask is **N=1, E=2, S=4, W=8, Up=16, Down=32**.
A set bit means an opening, not a wall. Cells and floors use zero-based coordinates.

## Physical stair geometry prototype

The geometry prototype produces sixteen real stair treads with either a
continuous hollow underside or a fully solid undercarriage, an upper landing,
and optional enclosing walls. It analytically checks whether a person can walk under the *high end*
and separately whether the same stairs are climbable. A 23-story switchback
test verifies successive flights meet at shared landings. Export OBJ test
rooms with:

    ./build/maze-geometry open ./open-stairs.obj
    ./build/maze-geometry walled ./walled-stairs.obj

This is **not yet** integrated with the giant-maze corridors. The geometry
needs deterministic multi-tile space reservations and a new reachability proof
before generated stairs can replace abstract Up/Down edges. See
[docs/GEOMETRY.md](docs/GEOMETRY.md) for limitations and exact test rules.

## Solid versus hollow staircase construction

Stair flights can have an entirely **solid** supporting mass beneath
them, blending with an ordinary brick maze wall, or a uniformly
**hollow** underside with one continuous sloping slab. A solid flight
blocks any underpass regardless of whether it has enclosing walls;
a hollow flight with no enclosing walls may allow an underpass where
the player fits. **Never** create per-tread or random partial underfills.

A long staircase can switch its construction at story landings.
The default stair network groups four adjacent floor-to-floor flights
with one underside style; section decisions are reproducible from the
world seed and stair ID. These are independent of whether an intermediate
floor has an entrance. Physical geometry and navigation are still
separate prototypes, not a collision-proof generated world.

Inspect all four combinations as standard OBJ meshes after the CMake
build:

    ./build/maze-geometry open hollow-open.obj
    ./build/maze-geometry walled hollow-walled.obj
    ./build/maze-geometry solid solid-open.obj
    ./build/maze-geometry solid-walled solid-walled.obj

See [docs/GEOMETRY.md](docs/GEOMETRY.md) and
[docs/STAIRCASES.md](docs/STAIRCASES.md).

## What we implemented

1. Divide the world into **32 x 32-cell chunks per floor**. Edge chunks are smaller.
2. Treat all chunks across all floors as vertices in an implicit **3D graph**.
3. Recursively divide that rectangular graph into two nonempty boxes. Each split
   selects **exactly one adjacent chunk pair** across the dividing plane as a
   mandatory connection. Keep subdividing until every box contains one chunk.
   All choices are derived from the seed and the current box, not global RNG state.
4. Locally generate each chunk's 2D maze with **iterative randomized depth-first
   carving**. Each tile is connected to every other tile in that chunk.
5. Open mandatory cross-chunk portals on the correct boundaries, plus mandatory
   **up/down portals** when the macro graph joins different floors. Neighboring
   chunks compute the same portal coordinates independently.
6. Optionally add independent horizontal loops and vertical shafts. Since these
   **only add connections**, they can never disconnect the guaranteed maze.
7. Cache only recently requested chunks with a configurable **LRU memory cap**.
   No complete world map, global visited set, giant DFS stack, or precomputed
   world graph is needed during generation.
8. Add **first-class long stairwells** using a separate navigation space.
   Stairs can rise by 1, 23, or many more floors, with entrances only on the
   endpoints or selectively on intervening floors. They do not change any of
   the original corridor connections. Stairwells are deterministic and queried
   on demand with O(1) metadata lookup. No ladders are part of the design.

**Connectivity is guaranteed by construction** for any valid finite world. With
`loop_bps=0` and `stair_bps=0`, the original **corridor graph** is a spanning tree: exactly
one route between any pair of cells, including cells on different floors.
Additional long stairwells create intentional alternative routes when enabled.
For a precise argument and important tradeoffs, see [docs/ALGORITHM.md](docs/ALGORITHM.md)
and [docs/STAIRCASES.md](docs/STAIRCASES.md).

## Scales and practical limits

The implementation uses `uint64_t` coordinates and doesn't multiply the complete
world volume to allocate memory. It can query cells at coordinates near
**one trillion x one trillion, across three million floors** (the large-world
regression test) with a bounded cache. That **doesn't mean** the entire world has
been generated, explored, drawn, or performance-tested as a real game.

Each complete 32 x 32 chunk's passage grid is only **1 KiB**, excluding cache
metadata and transient generation buffers. A 128-chunk cache retains about
128 KiB of passage grid data (plus overhead). The full procedural world will need nearby-chunk mesh streaming; that
world-scale renderer does not exist yet. The separate handcrafted 3D room
renderer is playable.

## Important limitations

- The **generated giant labyrinth** is still a topology graph, not a
  collision-tested 3D building. Up/Down bits are abstract links that will
  require reserved physical stair geometry. The separate playable test room
  *already* demonstrates proper treads, underside fills, walls and collision,
  but does not yet place them into generated labyrinth corridors. The
  continuous multistory stair network remains abstract until that integration.
- Hierarchical division introduces **large-scale chokepoints**. Extra loops
  create alternative routes. Later algorithms can offer different maze styles.
- Changing the **dimensions** changes the hierarchy and thus may change the
  maze even with the same seed. Treat seed + all generator parameters + algorithm
  version as the stable world identity. Do not silently alter a saved world's size.
- The maze is finite, albeit potentially enormously so. **Infinite/unbounded
  mode is a separate future design** requiring a consistent infinite chunk graph.
- The code does not yet persist explored-cell fog-of-war, modified walls, player
  position, or save files. Regenerating unchanged chunks is deterministic.
- `Maze` uses an LRU cache and is **not thread-safe**. Use one generator per
  worker thread or synchronize shared access later.
- The distribution is **not** a uniform random spanning tree over the entire
  3D world. It intentionally prioritizes chunk independence and scalability.

## What comes next

See [docs/NEXT.md](docs/NEXT.md) for the renderer integration plan and tests
needed before calling this a full maze game.

## References / inspiration

The core design here is original code combining established methods, **not a
copied implementation**. Useful reading:

- Jamis Buck, [Maze Generation: Recursive Division](https://weblog.jamisbuck.org/2011/1/12/maze-generation-recursive-division-algorithm)
- Jamis Buck, [Maze Generation: Recursive Backtracking](https://weblog.jamisbuck.org/2010/12/27/maze-generation-recursive-backtracking)
- Jamis Buck, [A Better Recursive Division Algorithm](https://weblog.jamisbuck.org/2015/1/15/better-recursive-division-algorithm.html) (on visible bottlenecks)

Licensing: this self-contained subproject inherits the repository's default
license unless a separate license is introduced later.
