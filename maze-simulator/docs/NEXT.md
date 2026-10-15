# Integration roadmap

1. **Freeze generator v1:** keep seed and world settings alongside a generator
   version in saves; do not change hashing or chunk construction invisibly.
2. **3D geometry:** make procedural meshes for visible chunk walls/floor/ceiling,
   merge repeated wall faces, and render only chunks near the player. Use SDL3 +
   OpenGL and keep the renderer separate from the topology library.
3. **Geometry-aware stairs:** `StairNetwork` now generates continuous,
   variable-rise multi-floor stairwells with selective entrances. **Next:**
   allocate collision-safe physical footprints/vestibules, switchback or spiral
   meshes, ceiling cutouts, and suitable single-floor steps at original Up/Down
   edges. Test that the staircase and corridor geometry never overlap and that
   intermediate floors without doors cannot be entered. Stairs only: no ladders.
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
- Validate **stair footprints, ceilings, landings, step collision, and doorway
  accessibility** when actual 3D geometry exists.

Current `tests/test_maze.cpp` covers the first four regression areas and tests
single-cell, linear, partial chunks, multiple floors, deterministic seeds,
order-independence, and optional-loop configurations. AddressSanitizer and
UndefinedBehaviorSanitizer runs also passed in the initial development session.
`tests/test_stairs.cpp` now tests 23-story enclosed staircases, open landings,
band-crossing runs, multi-space navigation reciprocity/connectedness, and
trillion-coordinate queries.

## Phase 3: physical stair proof (prototype)

The separate dimensionless stair geometry module now makes flat treads with
thin supporting undersides (so real underpasses exist), tests the full player
width/height against clearance, distinguishes open and walled stairs, and
verifies switchback landings across 23 story segments. It exports simple OBJ
rooms; see [GEOMETRY.md](GEOMETRY.md). All of this is independent of the
large-scale topology generator. NEXT PRIORITY: reserve the full 3D volume
needed by each stair *before* corridor carving; integrate the guaranteed
macro graph while routing around reserved space and allowing valid underpasses.
Do not mark physical maze connectivity as solved until traversability is
checked end-to-end with actual collision geometry.
