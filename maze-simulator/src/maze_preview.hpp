#pragma once

#include "maze.hpp"

#include <cstdint>
#include <string>

namespace labyrinth::preview {

// These statistics describe HORIZONTAL corridors on one floor. Abstract
// UP/DOWN graph edges are counted separately and NEVER represented as a
// physically approved staircase or room footprint.
struct Stats {
    std::uint64_t tiles=0, dead_ends=0, turns=0, straights=0;
    std::uint64_t tee_junctions=0, crossings=0, isolated=0;
    std::uint64_t up_links=0, down_links=0, wall_segments=0;
};

struct FloorPreview {
    std::string svg;
    Stats stats;
};

// A pure mathematical *floor-plan* view of the exact existing Maze graph.
// Does not use or reproduce the maze algorithm: Maze::passages is authoritative.
// Limits only the preview area, not what the real generator can create.
FloorPreview render_floor(Maze& maze,std::uint64_t floor,std::uint32_t tile_px=20);

} // namespace labyrinth::preview
