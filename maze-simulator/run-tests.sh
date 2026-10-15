#!/bin/sh
set -eu
cd "$(dirname "$0")"
CXX="${CXX:-g++}"
"$CXX" -std=c++20 -O2 -Wall -Wextra -Wpedantic -Werror src/maze.cpp tests/test_maze.cpp -o /tmp/maze-simulator-tests
/tmp/maze-simulator-tests
"$CXX" -std=c++20 -O2 -Wall -Wextra -Wpedantic -Werror src/maze.cpp src/main.cpp -o /tmp/maze-simulator-cli
printf '\nSample floor map:\n'
/tmp/maze-simulator-cli demo 20 10 3 6549 600 300 0
