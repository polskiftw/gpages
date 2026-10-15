#include "stairs.hpp"

#include <algorithm>
#include <limits>
#include <stdexcept>

namespace labyrinth {
namespace {
constexpr std::uint64_t TagEnabled = 0x5354414952531001ULL;
constexpr std::uint64_t TagAnchorX = 0x5354414952531002ULL;
constexpr std::uint64_t TagAnchorY = 0x5354414952531003ULL;
constexpr std::uint64_t TagRise = 0x5354414952531004ULL;
constexpr std::uint64_t TagBegin = 0x5354414952531005ULL;
constexpr std::uint64_t TagEnclosed = 0x5354414952531006ULL;
constexpr std::uint64_t TagShape = 0x5354414952531007ULL;
constexpr std::uint64_t TagLanding = 0x5354414952531008ULL;
constexpr std::uint64_t TagSolidUnder = 0x5354414952531009ULL;
}

StairNetwork::StairNetwork(Maze& maze, StairSettings options) : maze_(maze), options_(options) {
    if (!options.min_rise || options.min_rise > options.max_rise ||
        options.max_rise == std::numeric_limits<std::uint64_t>::max())
        throw std::invalid_argument("stair min_rise must be 1..max_rise and max_rise must be below UINT64_MAX");
    if (options.density_bps > 10000 || options.landing_bps > 10000 ||
        options.enclosed_bps > 10000 || options.solid_under_bps > 10000)
        throw std::invalid_argument("stair probabilities must be 0..10000 basis points");
    if (!options.construction_section_floors)
        throw std::invalid_argument("underside construction sections must span at least one whole floor");
}

std::uint64_t StairNetwork::mix(std::uint64_t x) {
    x += 0x9E3779B97F4A7C15ULL;
    x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9ULL;
    x = (x ^ (x >> 27)) * 0x94D049BB133111EBULL;
    return x ^ (x >> 31);
}

std::uint64_t StairNetwork::hash(std::uint64_t tag, std::initializer_list<std::uint64_t> values) const {
    std::uint64_t h = mix(maze_.settings().seed ^ tag);
    for (std::uint64_t v : values) h = mix(h ^ mix(v));
    return h;
}

std::optional<Stairwell> StairNetwork::for_chunk(std::uint64_t cx, std::uint64_t cy, std::uint64_t band) const {
    const auto& world = maze_.settings();
    const std::uint64_t count_x = (world.width - 1) / Maze::ChunkSide + 1;
    const std::uint64_t count_y = (world.height - 1) / Maze::ChunkSide + 1;
    const std::uint64_t count_bands = (world.floors - 1) / band_size() + 1;
    if (cx >= count_x || cy >= count_y || band >= count_bands) return std::nullopt;
    const std::uint64_t floor_begin = band * band_size(); // safe: band < ceil(floors / band_size)
    const std::uint64_t remaining = world.floors - floor_begin;
    if (remaining <= options_.min_rise || !options_.density_bps ||
        hash(TagEnabled, {cx,cy,band}) % 10000 >= options_.density_bps) return std::nullopt;
    const std::uint64_t ox = cx * Maze::ChunkSide, oy = cy * Maze::ChunkSide;
    const std::uint64_t local_width = std::min<std::uint64_t>(Maze::ChunkSide, world.width - ox);
    const std::uint64_t local_height = std::min<std::uint64_t>(Maze::ChunkSide, world.height - oy);
    const std::uint64_t slots = local_width * local_height;
    // Two disjoint XY anchor slots, alternating by band parity, prevent
    // overlap between adjacent tall staircases crossing band boundaries.
    // In a one-tile chunk there is only one slot: omit odd-band candidates.
    if (slots == 1 && (band & 1)) return std::nullopt;
    const std::uint64_t first_slot = hash(TagAnchorX, {cx,cy}) % slots;
    const std::uint64_t other_slot = slots == 1 ? first_slot :
        (first_slot + 1 + hash(TagAnchorY, {cx,cy}) % (slots - 1)) % slots;
    const std::uint64_t slot = (band & 1) ? other_slot : first_slot;
    const std::uint64_t valid_starts = std::min(band_size(), remaining - options_.min_rise);
    const std::uint64_t offset = hash(TagBegin, {cx,cy,band}) % valid_starts;
    const std::uint64_t bottom = floor_begin + offset;
    const std::uint64_t max_rise = std::min(options_.max_rise, world.floors - bottom - 1);
    const std::uint64_t rise = options_.min_rise +
        hash(TagRise, {cx,cy,band}) % (max_rise - options_.min_rise + 1);
    const bool enclosed = hash(TagEnclosed, {cx,cy,band}) % 10000 < options_.enclosed_bps;
    const auto shape = hash(TagShape, {cx,cy,band}) % 2 ? StairShape::Spiral : StairShape::Switchback;
    return Stairwell{cx,cy,band, ox + slot % local_width, oy + slot / local_width,
        bottom, bottom + rise, shape, enclosed};
}

std::optional<Stairwell> StairNetwork::at(Cell c) const {
    const auto& world = maze_.settings();
    if (c.x >= world.width || c.y >= world.height || c.z >= world.floors) return std::nullopt;
    const std::uint64_t cx = c.x / Maze::ChunkSide, cy = c.y / Maze::ChunkSide;
    const std::uint64_t band = c.z / band_size();
    auto same = for_chunk(cx,cy,band);
    if (same && same->x == c.x && same->y == c.y &&
        c.z >= same->bottom && c.z <= same->top) return same;
    // A staircase may extend beyond its starting band by up to max_rise
    // floors, so only the immediately previous band can also contain it.
    if (band) {
        auto previous = for_chunk(cx,cy,band-1);
        if (previous && previous->x == c.x && previous->y == c.y &&
            c.z >= previous->bottom && c.z <= previous->top) return previous;
    }
    return std::nullopt;
}

bool StairNetwork::has_landing(const Stairwell& t, std::uint64_t floor) const {
    if (floor < t.bottom || floor > t.top) return false;
    if (floor == t.bottom || floor == t.top) return true;
    return !t.enclosed && options_.landing_bps &&
        hash(TagLanding, {t.chunk_x,t.chunk_y,t.band,floor}) % 10000 < options_.landing_bps;
}

// Physical underside style is stable throughout each group of complete
// floor-to-floor flights. Switching is possible ONLY at a shared landing,
// not partway up any individual flight or randomly within its mesh chunks.
// This is an O(1) lookup even in towers spanning millions of floors.
bool StairNetwork::solid_under(const Stairwell& t, std::uint64_t floor) const {
    if (floor < t.bottom || floor >= t.top)
        throw std::out_of_range("no flight originates at the given staircase floor");
    const std::uint64_t section = (floor - t.bottom) / options_.construction_section_floors;
    return options_.solid_under_bps != 0 &&
           hash(TagSolidUnder, {t.chunk_x,t.chunk_y,t.band,section}) % 10000 < options_.solid_under_bps;
}

NavNeighbors StairNetwork::neighbors(NavNode n) {
    const auto& s = maze_.settings();
    const Cell c = n.cell;
    if (c.x >= s.width || c.y >= s.height || c.z >= s.floors)
        throw std::out_of_range("navigation node outside maze");
    NavNeighbors out;
    if (n.space == Space::Corridor) {
        const auto p = maze_.passages(c);
        if (p & North) out.push_back({{c.x,c.y-1,c.z}, Space::Corridor});
        if (p & East) out.push_back({{c.x+1,c.y,c.z}, Space::Corridor});
        if (p & South) out.push_back({{c.x,c.y+1,c.z}, Space::Corridor});
        if (p & West) out.push_back({{c.x-1,c.y,c.z}, Space::Corridor});
        // The original adjacent-level links are retained as one-floor stairs.
        // Their physical flight meshes/footprints still require a renderer design.
        if (p & Up) out.push_back({{c.x,c.y,c.z+1}, Space::Corridor});
        if (p & Down) out.push_back({{c.x,c.y,c.z-1}, Space::Corridor});
        auto tower = at(c);
        if (tower && has_landing(*tower,c.z)) out.push_back({c, Space::StairInterior});
    } else if (n.space == Space::StairInterior) {
        auto tower = at(c);
        if (!tower) throw std::invalid_argument("stair interior node is not in a staircase");
        if (c.z > tower->bottom) out.push_back({{c.x,c.y,c.z-1}, Space::StairInterior});
        if (c.z < tower->top) out.push_back({{c.x,c.y,c.z+1}, Space::StairInterior});
        if (has_landing(*tower,c.z)) out.push_back({c, Space::Corridor});
    } else {
        throw std::invalid_argument("unknown navigation space");
    }
    return out;
}

} // namespace labyrinth
