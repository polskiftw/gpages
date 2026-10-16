# Maze Lab v2 - topology before graphics

**Live viewer:** https://polskiftw.github.io/gpages/maze-simulator/

This is a phone-friendly, **math-only** maze workshop. Every visible wall is
drawn directly from the maze's real cell passage bitmask. There is no 3D engine,
web service, dependency, build step, or downloaded asset.

## Mathematical guarantees

- A finite width x height x levels world with one start and one exit.
- Every cell on every level is connected to every other cell.
- The **macro chunk graph** recursively divides a 3D box, selecting exactly
  one shared neighboring-chunk portal across each split: a spanning tree.
  Macro edges can be queried directly without storing the full world.
- Each 16 x 16 chunk contains its own seeded spanning tree. Combining local
  trees and macro portals guarantees connectivity of the full cell graph.
- Neighboring chunks calculate identical reciprocal wall openings and
  vertical transitions, regardless of visitation or cache eviction order.
- Optional braid openings, extra cross-chunk links, and extra vertical edges
  only add routes. They cannot disconnect the maze.
- Local chunk generation is deterministic from the seed, dimensions, chunk
  coordinates and passage style, with a bounded 96-chunk default LRU.
- Styles: straight-biased depth-first, mixed growing-tree, or random Prim.

**Important physical limitation:** Vertical arrows are valid *abstract*
floor-connection edges. Full-size staircase footprints, collision, headroom
and underpasses have NOT yet been proven or placed. This tool validates
topology, not an architectural 3D building.

## Viewer

Enter dimensions, floor count, passage character and seed. Pan on a phone,
change floors, jump to the start or exit, save SVG and copy a reproducible URL.
The three small samples use the same generation engine as the main map,
rather than fabricated concept images.

Large worlds need not be fully generated to inspect a window. The generator
version is part of reproduction: changing the algorithm in a future release
would require a new version to preserve exact old seed results.

## Tests

Node.js 22+, no packages:

    node --test tests/maze.test.mjs

The suite verifies whole-world reachability in several multi-floor worlds,
every reciprocal cell opening, outer boundaries, unique endpoints, macro-tree
size and connectivity, chunk-order independence, and SVG determinism.
GitHub Actions runs the same test suite.

The old native C++/OpenGL prototype, heavy CI and geometry studies have been
removed from the active project. They remain recoverable in Git history.
