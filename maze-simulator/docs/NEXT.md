# Integration roadmap

1. **Freeze generator v1:** keep seed and world settings alongside a generator
   version in saves; do not change hashing or chunk construction invisibly.
2. **3D geometry:** make procedural meshes for visible chunk walls/floor/ceiling,
   merge repeated wall faces, and render only chunks near the player. Use SDL3 +
   OpenGL and keep the renderer separate from the topology library.
3. **Physical multi-floor transitions:** initially use a clearly signposted
   vertical shaft at each Up/Down cell; handle both stair directions and
   ceiling cutouts. Later add true stair meshes with a deterministic footprint.
4. **Player controller:** mouse look, WASD, collision, speed settings, coordinate
   HUD, optional 90-degree turning, and fast one-click launch.
5. **Navigation / objectives:** spawn selection, seeded exit locations, optional
   maps, coordinates, breadcrumbs, manual markers, waypoints, and shortest-path
   guidance *when computationally feasible*. Never assume that a full-world
   flood fill fits RAM at giant scales.
6. **Room/complexity styles:** optional rooms, local Wilson/Prim variants,
   short-vs-long corridor tuning, no-loop/perfect vs loop-heavy presets, vertical
   connection density. Keep a known-solvable base graph.
7. **Saves / streaming:** small world metadata, player position, objective state,
   chunk-indexed compressed fog-of-war, sparse modified walls, LRU capacity,
   and profiling of load/unload behavior.
8. **Native packaging:** compile for Gentoo with no always-on background services
   and no browser requirement. Keep this `gpages` folder as the upstream source.

## CI / regression tasks

- Test different cache limits and chunk request orders.
- Run full-grid BFS plus 6-way reciprocal edge checks on manageable worlds.
- Assert that with zero optional loops/shafts, edge count = total cells - 1.
- Probe huge coordinates to ensure no accidental total-world allocations.
- Profile tens of thousands of random chunk loads and check LRU bounds.
- Add randomized fuzz tests of dimension edge cases and chunk boundaries.
- Add save/reload reproducibility test after serialization exists.
- Add floor/shaft collision validation when the 3D geometry layer exists.

Current `tests/test_maze.cpp` covers the first four regression areas and tests
single-cell, linear, partial chunks, multiple floors, deterministic seeds,
order-independence, and optional-loop configurations. AddressSanitizer and
UndefinedBehaviorSanitizer runs also passed in the initial development session.