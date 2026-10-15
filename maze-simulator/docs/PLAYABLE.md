# Native walkable 3D staircase laboratory

This is a real C++20 / SDL3 / OpenGL 3.3 Core first-person window. It is
a HANDCRAFTED TEST ROOM, not yet the streamed procedural maze.

## Build and run on Gentoo

Requires the installed SDL3 development package (3.2+), an OpenGL 3.3
driver and development headers/libraries, a C++20 compiler, and CMake.
It uses only your installed SDL3 and native OpenGL. No daemon, account,
Proton, game launcher, or network connection at runtime.

From the maze-simulator directory:

    sh run-playable.sh

Or build and run directly:

    cmake -S . -B build -DMAZE_BUILD_PLAYABLE=ON -DMAZE_REQUIRE_PLAYABLE=ON
    cmake --build build --parallel 12 --target maze-playable
    ./build/maze-playable

SDL3 automatically selects Wayland or X11; XWayland can be requested for
diagnosis with SDL_VIDEODRIVER=x11 before the launcher command. CMake
does not install packages. If SDL3 or OpenGL is missing, the wrapper
fails clearly, while topology-only targets still remain buildable.

## Controls

| Key | Action |
| --- | --- |
| W A S D | Walk; W from spawn follows actual stair treads |
| Mouse | Look around |
| Shift | Faster walk |
| R | Reset at foot of stairs, facing upward |
| U | Start at ground-level underpass, facing across raised stairs |
| 1 | HOLLOW stairs, OPEN sides |
| 2 | HOLLOW stairs, ENCLOSING walls |
| 3 | SOLID masonry underside, OPEN sides |
| 4 | SOLID underside, ENCLOSING walls |
| Escape | Toggle mouse capture |
| Left click | Capture mouse again |
| Q | Quit |

Try walking up with W. Press U and W to pass beneath the high end of the
hollow staircase, without climbing it. Press 3 then U then W: solid brick
infill blocks the same path. Press 2 then U then W: hollow stairs exist,
but the walls block the crossing. Press 1 to return to a passable
underpass. Changing modes resets your position so masonry doesn't appear
around the player's body.

All measurements are dimensionless: tiles of width 1, story rise 1,
16 flat treads per floor, player radius 0.12 and height 0.65.

## Tested and untested

The separate pure C++ movement/mesh modules run headless tests for:
climbing and descending in every structural mode, lower-vs-upper stair
headroom, full player radius clearance, walls, outer bounds, and valid
triangle geometry. CI also compiles the actual SDL3/OpenGL executable
and renders a frame into a PPM image with a software OpenGL driver.

Manual screenshot smoke test after building:

    ./build/maze-playable --smoke first-person.ppm
    ./build/maze-playable --smoke under-hollow.ppm 1 under
    ./build/maze-playable --smoke under-solid.ppm 3 under

This test room intentionally does NOT yet use the giant-world Maze or
StairNetwork to place physical stairs in random corridors. It is a
visual and physical prototype for that integration. The current
staircase network reserves a nominal one-tile XY anchor, whereas
the prototype flight takes four horizontal tiles. Actual collision-safe
spatial reservations and whole-world physical connectivity are still
next, before a full first-person procedural labyrinth is possible.

No saving, start/exit game loop, music, map, monsters, or furniture yet.
