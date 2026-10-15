#!/bin/sh
set -eu
cd "$(dirname "$0")"
CXX="${CXX:-g++}"
"$CXX" -std=c++20 -O2 -Wall -Wextra -Wpedantic -Werror src/maze.cpp tests/test_maze.cpp -o /tmp/maze-simulator-tests
/tmp/maze-simulator-tests
"$CXX" -std=c++20 -O2 -Wall -Wextra -Wpedantic -Werror src/maze.cpp src/stairs.cpp tests/test_stairs.cpp -o /tmp/maze-simulator-stair-tests
/tmp/maze-simulator-stair-tests
"$CXX" -std=c++20 -O2 -Wall -Wextra -Wpedantic -Werror src/maze.cpp src/stairs.cpp src/stairs_main.cpp -o /tmp/maze-simulator-stairs-cli
"$CXX" -std=c++20 -O2 -Wall -Wextra -Wpedantic -Werror src/maze.cpp src/main.cpp -o /tmp/maze-simulator-cli
printf '\nSample floor map:\n'
/tmp/maze-simulator-cli demo 20 10 3 6549 600 300 0
printf '\nSample staircase listing:\n'
/tmp/maze-simulator-stairs-cli 64 64 50 6549 49 1 10000 3000 4000
