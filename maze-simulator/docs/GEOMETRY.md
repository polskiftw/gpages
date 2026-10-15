# Physical stair prototype: climbing and passing underneath

This phase adds real dimensionless 3D stair-flight geometry to answer the
example from our design discussion. The player may climb the flight or
cross underneath its high end. Side walls optionally block the underpass.
**This is a mathematically modeled test room, not yet a playable game or an
integrated multi-billion-cell geometry generator.**

## No meters: tiles and levels only

A horizontal tile has width 1 and consecutive floors differ by Z=1.
A default flight spans X=0..4, Y=0..1, rises from Z=0 to Z=1, and has
16 flat treads. Default player radius is 0.12, standing height 0.65,
step-up 0.07, supporting slab thickness 0.06. All are configurable
dimensionless ratios; they are not real-world architectural measurements.

## Two separate routes through the same space

- **Climb:** from the low end at X=0, walk along X to the upper landing at
  X=4 and Z=1. Each tread is one sixteenth of a story high.
- **Cross underneath:** walk across Y from -0.6 to 1.6 at ground-floor X=3.6.
  The thin sloping stair underside leaves enough headroom. The underpass
  does not connect to the upper stair surface.
- **Blocked crossing:** the same route at X=1.2 is too low for the player.
- **Walled crossing:** if stairs have walls extending to the floor, the
  formerly valid underpass is obstructed by those walls.

For a positive-X ascending flight, the continuous underside is:

    underside(x) = base_z + rise * clamp((x - start_x)/run, 0, 1) - slab

A player may cross at ground level only if the lowest underside along the
whole swept body footprint, including player radius, is above their head.
The test calculates that minimum analytically for a straight crossing.
It is NOT yet a fully general capsule collision solver.

The physical OBJ exporter writes flat stair treads, a continuous hollow slab
or a single solid underside infill, the lower floor, an upper landing, and
optional floor-reaching enclosure walls. The caller chooses the full-flight
undercarriage style; it never mixes solid and hollow under one flight.

## Undercarriage and enclosing walls are independent

A staircase flight has TWO independent architectural choices:

| Undercarriage | Side walls | Appearance and ground-level crossing |
| --- | --- | --- |
| Hollow | None | One uninterrupted sloped underside; can pass under the high end if there is headroom and a corridor |
| Hollow | Enclosed | Underside is continuous and hollow, but floor-reaching side walls block a transverse ground-level corridor |
| Solid | None | Entire space below the inclined stair slab is one continuous masonry wedge; no underpass, even without side walls |
| Solid | Enclosed | Same solid infill plus the separate side walls |

A solid undercarriage is **all-or-nothing for a complete floor-to-floor
flight**. It must not contain arbitrarily placed hollow tiles or gaps.
The OBJ model creates a **single triangular infill prism** under the entire
flight, with two uninterrupted side surfaces that can visually match
ordinary corridor walls. The open/hollow model instead has ONE continuous
smooth inclined underside from bottom to top. No patchy supports or
random per-step undercarriage decisions.

One enormous staircase may switch its construction **between stories**:
floors 1-5 solid, floors 5-9 hollow, floors 9-17 solid, etc. The story
landing is a natural architectural boundary where the type can change.
This must not create a gap or discontinuity in the walking surface above.

The long-stair generator uses a seeded stable style hash keyed to the
stairwell ID and the index of a contiguous section of whole story flights.
Defaults: 50% chance of solid infill per section; sections span four full
flights. Set construction_section_floors=1 for a new decision on every
story, or a larger value to create longer consistent runs. Consecutive
sections may independently pick the same style and visually join into
one longer uninterrupted section.

Undercarriage solidity and outer side walls are **not** the same as
Stairwell.enclosed, which controls only which intermediate floors
have corridor entrances. Geometry does not automatically cut openings
in an otherwise closed maze wall.

## Many floors without teleporting

A switchback flips direction every story: Floor 0->1 climbs left to right,
Floor 1->2 climbs right to left, then left to right again. The exit XY and
Z of every flight agree with the entry of the next. Tests include 23
consecutive interconnected flights.

The existing staircase network decides how tall a run is and which floors
permit access. The geometry prototype models the individual repeatable
flights; it does not yet carve doors into the surrounding maze.

**Physical side walls are NOT the same as endpoint-only entrances.**
The previous Stairwell.enclosed metadata controls which floors have
a corridor connection. Geometry::Sides::Walled adds actual enclosing walls.
Even a wall-less flight may be inaccessible from a particular intermediate
floor because the ordinary maze walls do not open into it.

## Build and inspect

Inside maze-simulator:

    cmake -S . -B build
    cmake --build build
    ctest --test-dir build --output-on-failure
    ./build/maze-geometry open ./open-stairs.obj
    ./build/maze-geometry walled ./walled-stairs.obj
    ./build/maze-geometry solid ./solid-stairs.obj
    ./build/maze-geometry solid-walled ./solid-walled-stairs.obj

Both OBJ files are standard triangle meshes that can be viewed in a 3D
mesh application. The program also reports whether the two example
ground-level crossings are passable.

## The important next problem: integrate spatial occupancy

The current base maze assumes every cell is a corridor node. The existing
multi-floor staircase network anchors a staircase at a single XY cell;
the physical flight spans FOUR horizontal cells. Overlaying it directly
on a finished maze could intersect corridors or break necessary passages.

Before generated labyrinths become playable, we must:

1. Deterministically reserve enough real 3D volume for each stair, without
   conflicts with other stair structures or future corridor geometry.
2. Carve ordinary maze corridors around reservations without destroying
   the proven global connectivity.
3. Allow deliberately planned underpasses only when the player fits and
   both ends connect to reachable corridors. Shared XY must NOT silently
   connect the ground-level underpass to the staircase above it.
4. Connect stair landings only on floors granted entrances; ensure closed
   intermediate floors have no accidental entry.
5. Represent every mandatory single-level Up/Down edge with walkable
   physical stairs and collision-clear endpoints, then test the new
   geometry-aware navigation graph for reachability.

The original topology proof remains valid. Actual whole-world
physical reachability is **not** proven by this prototype.
