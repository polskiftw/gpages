# Maze Simulator - procedural 3D maze engine (prototype)

**Status:** a real, tested, on-demand **maze topology generator** for the planned
Windows 95 3D Maze-inspired first-person game. It is **not a playable 3D game yet**.
The source is in this independent `gpages/maze-simulator/` folder; it does not
modify the GitHub Pages landing page, other utilities, or site navigation.

The goal is to make mazes far larger than RAM, with arbitrary finite dimensions,
multiple floors, connected corridors, reproducible worlds, and no requirement to
generate or save the entire maze up front.

## Build on Gentoo / Linux

Only a C++20 compiler is needed. No SDL, OpenGL, third-party code, servers, or daemons.

```sh
g++ -std=c++20 -O2 -Wall -Wextra src/maze.cpp src/main.cpp -o maze-cli
./maze-cli demo 20 10 3 6549 600 300 0
./maze-cli check 64 64 8 1234 0 0
./maze-cli bench 100000 100000 100 42 600 50 1000
./maze-cli probe 100000 100000 100 42 600 50 99999 99999 99
./run-tests.sh
```

Or use CMake: `cmake -S . -B build && cmake --build build && ctest --test-dir build --output-on-failure`.
`run-tests.sh` creates temporary test binaries in `/tmp`; it does not install anything.

All CLI commands take `width height floors seed loop_bps stair_bps` in that order,
followed by command-specific values. Defaults are `256 256 3 6549 600 50`.
Basis points are percentages with two decimal places: `600` = 6% additional
horizontal openings and `50` = 0.5% additional vertical shafts per cell/floor pair.

| Command | What it does |
| --- | --- |
| `demo ... [floor]` | ASCII floor plan for <=100 x 45 cells; shows vertical transitions |
| `check ...` | Whole-world reachability and perfect-tree check, capped at 250,000 cells |
| `probe ... x y z` | Get a cell's passage bits without generating the rest of the world |
| `bench ... [samples]` | Random-access timing across an immense finite world |

The passage bitmask is **N=1, E=2, S=4, W=8, Up=16, Down=32**.
A set bit means an opening, not a wall. Cells and floors use zero-based coordinates.

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

**Connectivity is guaranteed by construction** for any valid finite world. With
`loop_bps=0` and `stair_bps=0`, the entire cell graph is a spanning tree: exactly
one route between any pair of cells, including cells on different floors.
For a precise argument and important tradeoffs, see [docs/ALGORITHM.md](docs/ALGORITHM.md).

## Scales and practical limits

The implementation uses `uint64_t` coordinates and doesn't multiply the complete
world volume to allocate memory. It can query cells at coordinates near
**one trillion x one trillion, across three million floors** (the large-world
regression test) with a bounded cache. That **doesn't mean** the entire world has
been generated, explored, drawn, or performance-tested as a real game.

Each complete 32 x 32 chunk's passage grid is only **1 KiB**, excluding cache
metadata and transient generation buffers. A 128-chunk cache retains about
128 KiB of passage grid data (plus overhead). The rendering geometry should
also be streamed by proximity, but that renderer does not exist yet.

## Important limitations

- This generates **graph topology**, not 3D geometry. An `Up`/`Down` bit denotes
  a vertical connection at a matching XY coordinate on adjacent floors. The
  renderer/gameplay layer must eventually decide whether it is a staircase,
  ladder, elevator, or explicit floor-transfer interaction and ensure clearance.
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
