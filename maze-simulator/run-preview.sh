#!/bin/sh
# Build the authoritative C++ floor-plan preview and open it in Firefox.
# No Git, server, resident daemon, SDL3 or OpenGL required.
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cmake -S "$here" -B "$here/build" -DMAZE_BUILD_PLAYABLE=OFF
cmake --build "$here/build" --parallel "${MAZE_JOBS:-12}" --target maze-preview
"$here/build/maze-preview" gallery "$here/previews"
echo "Generated $here/previews/standalone.html"
if command -v firefox >/dev/null 2>&1; then
  exec firefox "file://$here/previews/standalone.html"
fi
