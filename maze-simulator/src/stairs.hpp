#pragma once

#include "maze.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <initializer_list>
#include <optional>

namespace labyrinth {

// Separate stairwell interior from the horizontal floor cell at identical XYZ.
// A player may traverse an enclosed stairwell *past* a floor without having an
// entrance to that floor. This cannot be represented with the 6-bit Cell mask.
enum class Space : std::uint8_t { Corridor, StairInterior };
struct NavNode { Cell cell; Space space = Space::Corridor; bool operator==(const NavNode& n) const { return cell.x == n.cell.x && cell.y == n.cell.y && cell.z == n.cell.z && space == n.space; } };
// A corridor has at most six traditional edges plus one staircase entrance.
// Fixed-capacity storage avoids heap allocation on every navigation query.
struct NavNeighbors {
    std::array<NavNode,7> entries{};
    std::size_t count = 0;
    void push_back(NavNode node) { entries[count++] = node; }
    std::size_t size() const { return count; }
    auto begin() const { return entries.begin(); }
    auto end() const { return entries.begin() + static_cast<std::ptrdiff_t>(count); }
};
enum class StairShape : std::uint8_t { Spiral, Switchback };

struct StairSettings {
    // Candidate starts are partitioned into max_rise+1-floor bands, but
    // staircase runs may cross into the following band. Choose
    // max_rise >= 23 to allow a 23-floor continuous staircase.
    std::uint64_t max_rise = 63, min_rise = 1;
    // Per XY chunk column + floor band: whether a tower exists (basis points).
    std::uint16_t density_bps = 2000;
    // Chance of a door at each interior floor for non-enclosed towers.
    std::uint16_t landing_bps = 3000;
    // Chance a tower has ONLY endpoint access, ignoring landing_bps.
    std::uint16_t enclosed_bps = 4000;
    // Per-whole-flight underside construction. Every flight within a
    // contiguous group of N stories gets the SAME solid or hollow style.
    // Deterministic per staircase ID; NEVER randomized per tread or tile.
    std::uint16_t solid_under_bps = 5000; // 50% of groups have a solid fill.
    std::uint64_t construction_section_floors = 4; // Whole-story grouping.
};

struct Stairwell {
    // The triple (chunk_x, chunk_y, starting band) is the unique ID.
    std::uint64_t chunk_x, chunk_y, band;
    std::uint64_t x, y, bottom, top; // top > bottom; inclusive floor levels.
    StairShape shape;
    bool enclosed;
};

// Additional multi-floor staircase routes, layered on top of the always-usable
// base maze. Creation and lookup are O(1), independent of world dimensions and
// staircase height; no global tower catalogue, allocations, or visited map.
// This is TOPOLOGY, not meshes/collision or a physical footprint reservation.
// Unlike corridor nodes, stair interiors are not accessible at closed floors.
class StairNetwork {
public:
    StairNetwork(Maze& maze, StairSettings options = {});
    const StairSettings& options() const { return options_; }
    std::uint64_t band_size() const { return options_.max_rise + 1; }
    std::optional<Stairwell> for_chunk(std::uint64_t chunk_x, std::uint64_t chunk_y, std::uint64_t band) const;
    std::optional<Stairwell> at(Cell cell) const;
    bool has_landing(const Stairwell& tower, std::uint64_t floor) const;
    // For the flight from floor to floor+1, not the landing itself.
    // Can change at story boundaries, optionally grouped into longer runs.
    bool solid_under(const Stairwell& tower, std::uint64_t floor) const;
    NavNeighbors neighbors(NavNode node); // all reachable movement edges; reciprocal; no allocation.

private:
    Maze& maze_;
    StairSettings options_;
    static std::uint64_t mix(std::uint64_t x);
    std::uint64_t hash(std::uint64_t tag, std::initializer_list<std::uint64_t> values) const;
};

} // namespace labyrinth
