#!/bin/sh
# Build and launch the small native Linux 3D staircase laboratory.
# Does not install packages, start a daemon, or use Git.
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cmake -S "$here" -B "$here/build" -DMAZE_BUILD_PLAYABLE=ON -DMAZE_REQUIRE_PLAYABLE=ON
cmake --build "$here/build" --parallel "${MAZE_JOBS:-12}" --target maze-playable
exec "$here/build/maze-playable" "$@"
