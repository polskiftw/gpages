# Vertical pathing v2: variable-height staircases (no ladders)

The maze is finite, and the original connected 3D corridor graph remains intact.
This extension adds **actual staircase topology**: continuous, variable-length
multi-floor stairwells with only explicitly selected landings connecting them to
the rest of the maze. Stairs can span one floor or dozens/hundreds/millions of
floors (subject to the configured limit). No ladders, elevators, furniture, or
other traversal types are generated.

## The two-space model

An `(x,y,z)` now potentially identifies **two different navigation nodes**:

- `Corridor`: the pre-existing ordinary walkable cell on floor `z`.
- `StairInterior`: the inside of a long staircase at the same nominal location.

The stair interior continues vertically at every intervening floor. The
horizontal corridor connects to it **only where a landing is explicitly open**.
A staircase might run from Floor 1 to Floor 24 without any intermediate doors,
or it may have entrances on Floors 1, 9, 12, 18, and 24. Walking past Floor 11
inside the staircase does **not** let you step into Floor 11's corridor.

This separation is essential. The original 6-bit wall mask cannot simultaneously
represent (a) a vertical passage through a floor and (b) the absence of an
entrance on that floor. Adding a distinct `Space` type solves that topologically.

Navigation edges:

- Corridor <-> Corridor: original N/E/S/W and adjacent-floor Up/Down passages,
  unchanged. The existing Up/Down edges are interpreted as *single-floor*
  stair connections when physical geometry is introduced.
- StairInterior(z) <-> StairInterior(z+1): one continuous stairwell segment.
- StairInterior(z) <-> Corridor(z): **only** if that floor has a landing.

All edges are bidirectional. Optional long staircases add connections but never
remove the base routes. The original spanning-tree proof still guarantees
reachability of every floor corridor. Each additional tower has doors at both
endpoints, so every stair interior is reachable as well. Full-graph BFS tests
check this on small worlds (including intermediate floors without doors).

## Random-access generation of long staircases

`StairNetwork` uses the same world seed as `Maze`, and a separate group of hash
identifiers. Each XY **chunk column** (32x32 footprint, smaller at boundaries)
gets independent staircase candidates in starting-floor bands of
`max_rise + 1` floors. Each candidate may be disabled according to `density_bps`.

To generate one candidate, hash `(seed, chunk x, chunk y, band)` into its start
floor, rise (between `min_rise` and `max_rise`), staircase orientation preference,
and enclosure flag. The staircase may **cross the end of its starting band**;
it is not restricted to a synthetic vertical region boundary.

At most the candidate from the queried floor band **and its predecessor** can
cover a given height because each run rises less than the band length. Their
XY anchors are guaranteed to differ: even and odd bands use two distinct
locations within each XY chunk. A one-tile chunk cannot fit two distinct
stair interiors, so candidates in odd bands are intentionally suppressed there.
Candidates two bands apart cannot overlap vertically. This provides O(1)
random-access lookup without building or storing a world-wide catalogue.

A candidate has a unique identity `(chunk_x, chunk_y, starting_band)` and is
immutable for its seed and settings. Endpoints always have entrances. An
`enclosed` run has **no** intermediate entrances; a non-enclosed run chooses
each intermediate landing independently via `(seed, tower ID, floor)` and
`landing_bps`. The algorithm uses zero heap allocations for individual
navigation-neighbor queries (a fixed seven-entry buffer).

Configurable `StairSettings`:

| Field | Default | Purpose |
| --- | ---: | --- |
| `max_rise` | 63 | Maximum story difference between endpoints |
| `min_rise` | 1 | Minimum story difference; can equal max for exact-length runs |
| `density_bps` | 2000 | Chance a tower exists per XY chunk column / floor band (20%) |
| `landing_bps` | 3000 | Chance of an additional landing on an interior floor (30%) |
| `enclosed_bps` | 4000 | Chance a tower has only two entrances (40%) |

Percentages use basis points: 10000 = 100%; 0 = 0%.

**Example:** For exactly 23-story enclosed stairs, choose
`{max_rise=23,min_rise=23,density_bps=10000,landing_bps=10000,enclosed_bps=10000}`.
Intermediate floors are passed, not entered, regardless of `landing_bps`.
Alternatively choose `enclosed_bps=0,landing_bps=10000` to get an entrance on
every floor along the same uninterrupted vertical route.

## Build and inspect

From `maze-simulator/` on Gentoo/Linux:

```sh
cmake -S . -B build && cmake --build build && ctest --test-dir build --output-on-failure
./build/maze-stairs 64 64 50 6549 49 1 10000 3000 4000
```

The sample command inspects only the first four chunk columns and floor bands
per axis so it remains safe with huge dimension inputs. It prints generated
staircases with floor ranges and entrances (including some intermediate stops).

## Geometry still to be designed

**This implements route connectivity and object metadata, not climbable meshes.**
Each tower has a preferred `Spiral` or `Switchback` style. This is a deterministic
hint; dimensions, tread shape, slope, ceiling clearance, flight turns, and
collisions have not been modeled. `StairInterior` and `Corridor` currently share
nominal XY coordinates as *separate logical spaces*. Physically representing
both in a single square tile requires careful footprint reservations, vestibule
doors, or a larger stair-core geometry that does not intersect other corridors.
The base Up/Down routes also need reserved geometry for their single-story steps.

**A rendering engine must NOT naively place overlapping meshes for every logical
node** and claim the result is playable. The next significant task is a
geometry-aware stair-footprint allocator that reserves horizontal space,
re-routes affected corridor geometry without disconnecting it, and tests that
all required doors and steps are collision-accessible.

## Current limitations

- The multi-story stair network is *additional to* the mandatory one-story
  Up/Down routes. This is intentional: it guarantees connectivity regardless of
  the staircase density/door choices. A later generator revision may coordinate
  both systems into shared physical stairwells.
- Towers currently occupy a single abstract XY anchor each, not a realistic
  multi-tile footprint. A switchback or long straight staircase may need more
  horizontal space; shape metadata alone does not solve this.
- Starting-floor bands influence the statistical distribution of long runs and
  allow at most one optional stairwell per XY chunk column per band. They do not
  constrain exact floor reachability, because the existing maze remains
  connected. This is a scalable first-pass distribution, not a proof of uniform
  randomness over all possible staircase networks.
- Geometry-aware solution difficulty, minimum vertical transitions, and precise
  walking-distance measurements will follow after flight geometry is defined.
- Changing staircase settings changes the generated world. A save must include
  generator version, seed, original maze settings, and `StairSettings`.
