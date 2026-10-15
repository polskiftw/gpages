# First validation run (2026-10-15)

Commands used on the initial development container:

- `./run-tests.sh` - **ALL TESTS PASSED**.
- `g++ -std=c++20 -g -O1 -fsanitize=address,undefined -fno-omit-frame-pointer ...` and its test binary - **ALL TESTS PASSED**.
- `maze-cli check 64 64 8 1234 0 0` - **32,768 cells reached, 32,767 edges, connected, perfect tree**.
- `maze-cli bench 100000 100000 100 42 600 50 1000` - **1000 dispersed cell queries in 0.078 s, cache 128** on this container (not a promise of frame rate on other hardware).

The enormous-coordinate regression uses a *trillion-by-trillion* cell plane,
three million floors, and evaluates a handful of sampled cells plus 6-way seam
reciprocity. This demonstrates random-access feasibility; it **does not** prove
traversal performance or renderability of that astronomical world.

## Staircase topology v2

Stair route creation is deterministic and uses constant-work queries for one
XY chunk column / floor band, without a global tower index. The separate
`StairInterior` navigation space allows intermediate closed floors to be
passed vertically but not entered from the staircase.

`test_stairs.cpp` verifies exactly 23-floor enclosed stairs, unrestricted
intermediate landings, long runs crossing floor-band boundaries, alternative
heights, seeded reproducibility and gigantic coordinates. Full BFS checks the
augmented corridor+stair network for connectivity and reciprocal edges.
No renderer, geometry, physics or stair-footprint validation exists yet.
