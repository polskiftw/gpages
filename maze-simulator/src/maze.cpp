#include "maze.hpp"

#include <algorithm>
#include <array>
#include <initializer_list>
#include <stdexcept>
#include <tuple>
#include <utility>

namespace labyrinth {

namespace {
constexpr std::uint64_t TagChunk = 0xC001;
constexpr std::uint64_t TagAxis = 0xA110;
constexpr std::uint64_t TagSplit = 0x5A17;
constexpr std::uint64_t TagPortalA = 0x9001;
constexpr std::uint64_t TagPortalB = 0x9002;
constexpr std::uint64_t TagLoop = 0x1009;
constexpr std::uint64_t TagHorizontalEdge = 0xE002;
constexpr std::uint64_t TagHorizontalOffset = 0xE003;
constexpr std::uint64_t TagVerticalX = 0xE005;
constexpr std::uint64_t TagVerticalY = 0xE006;
constexpr std::uint64_t TagStair = 0x571A;

constexpr std::uint8_t Dir[4] = { North, East, South, West };
constexpr std::uint8_t Reverse[4] = { South, West, North, East };

std::uint64_t rng_next(std::uint64_t& state) {
    state += 0x9E3779B97F4A7C15ULL;
    std::uint64_t x = state;
    x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9ULL;
    x = (x ^ (x >> 27)) * 0x94D049BB133111EBULL;
    return x ^ (x >> 31);
}
}

std::uint64_t Maze::mix(std::uint64_t x) {
    x += 0x9E3779B97F4A7C15ULL;
    x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9ULL;
    x = (x ^ (x >> 27)) * 0x94D049BB133111EBULL;
    return x ^ (x >> 31);
}

std::size_t Maze::CoordHash::operator()(ChunkCoord c) const noexcept {
    return static_cast<std::size_t>(Maze::mix(c.x ^ Maze::mix(c.y ^ Maze::mix(c.z))));
}

Maze::Maze(Settings s, std::size_t cache_limit) : settings_(s), cache_limit_(cache_limit) {
    if (!s.width || !s.height || !s.floors) throw std::invalid_argument("maze dimensions must be positive");
    if (s.loop_bps > 10000 || s.stair_bps > 10000) throw std::invalid_argument("probabilities must be 0..10000 bps");
    if (!cache_limit) throw std::invalid_argument("cache limit must be positive");
    chunks_[0] = (s.width - 1) / ChunkSide + 1;
    chunks_[1] = (s.height - 1) / ChunkSide + 1;
    chunks_[2] = s.floors;
}

std::uint64_t Maze::hash(std::uint64_t tag, std::initializer_list<std::uint64_t> values) const {
    std::uint64_t h = mix(settings_.seed ^ tag);
    for (std::uint64_t v : values) h = mix(h ^ mix(v));
    return h;
}

std::uint64_t Maze::box_hash(const Box& b, std::uint64_t tag) const {
    return hash(tag, { b.lo[0], b.hi[0], b.lo[1], b.hi[1], b.lo[2], b.hi[2] });
}

std::uint64_t Maze::edge_hash(ChunkCoord a, ChunkCoord b, std::uint64_t tag) const {
    if (std::tie(a.z, a.y, a.x) > std::tie(b.z, b.y, b.x)) std::swap(a, b);
    return hash(tag, { a.x, a.y, a.z, b.x, b.y, b.z });
}

Maze::Split Maze::split_box(const Box& b) const {
    std::uint64_t best = 0;
    std::array<int, 3> choices{};
    int count = 0;
    for (int i = 0; i < 3; ++i) {
        std::uint64_t length = b.hi[i] - b.lo[i];
        if (length <= 1) continue;
        if (length > best) { best = length; count = 0; }
        if (length == best) choices[count++] = i;
    }
    if (!count) throw std::logic_error("attempted to split a single chunk");
    int axis = choices[box_hash(b, TagAxis) % static_cast<std::uint64_t>(count)];
    std::uint64_t size = b.hi[axis] - b.lo[axis];
    std::uint64_t min_left = std::max<std::uint64_t>(1, size / 3);
    std::uint64_t max_left = size - min_left;
    std::uint64_t left = min_left + box_hash(b, TagSplit) % (max_left - min_left + 1);
    return { axis, b.lo[axis] + left };
}

// The macro graph is a tree. Recursively bisect its bounding prism, with exactly
// one randomly positioned neighbor-to-neighbor portal joining the two children.
// Each child has its own tree, so induction proves all macro chunks connected.
// Evaluate ONLY the boxes containing this pair; never materialize the graph.
bool Maze::base_chunk_edge(ChunkCoord a, ChunkCoord b) const {
    const std::uint64_t aa[3] = { a.x, a.y, a.z };
    const std::uint64_t bb[3] = { b.x, b.y, b.z };
    int moving = -1;
    for (int i = 0; i < 3; ++i) {
        if (aa[i] == bb[i]) continue;
        if (moving != -1) return false;
        if ((aa[i] < bb[i] ? bb[i] - aa[i] : aa[i] - bb[i]) != 1) return false;
        moving = i;
    }
    if (moving == -1) return false;
    Box box{ {0, 0, 0}, {chunks_[0], chunks_[1], chunks_[2]} };
    while (true) {
        Split s = split_box(box);
        bool left_a = aa[s.axis] < s.cut;
        bool left_b = bb[s.axis] < s.cut;
        if (left_a != left_b) {
            if (s.axis != moving) return false;
            // Coordinate on the LOWER side of the dividing plane.
            const std::uint64_t* lower = left_a ? aa : bb;
            if (lower[moving] != s.cut - 1) return false;
            int k = 0;
            for (int i = 0; i < 3; ++i) {
                if (i == moving) continue;
                std::uint64_t tag = (k++ == 0) ? TagPortalA : TagPortalB;
                std::uint64_t location = box.lo[i] + box_hash(box, tag) % (box.hi[i] - box.lo[i]);
                if (lower[i] != location) return false;
            }
            return true;
        }
        if (left_a) box.hi[s.axis] = s.cut;
        else box.lo[s.axis] = s.cut;
    }
}

bool Maze::horizontal_chunk_edge(ChunkCoord a, ChunkCoord b) const {
    return base_chunk_edge(a, b) ||
        (settings_.loop_bps && edge_hash(a, b, TagHorizontalEdge) % 10000 < settings_.loop_bps);
}

bool Maze::extra_stair(std::uint64_t x, std::uint64_t y, std::uint64_t lower_floor) const {
    return settings_.stair_bps && hash(TagStair, { x, y, lower_floor }) % 10000 < settings_.stair_bps;
}

Chunk Maze::generate_chunk(ChunkCoord c) const {
    if (c.x >= chunks_[0] || c.y >= chunks_[1] || c.z >= chunks_[2])
        throw std::out_of_range("chunk outside the maze");
    const std::uint64_t ox = c.x * ChunkSide, oy = c.y * ChunkSide;
    const std::uint32_t w = static_cast<std::uint32_t>(std::min<std::uint64_t>(ChunkSide, settings_.width - ox));
    const std::uint32_t h = static_cast<std::uint32_t>(std::min<std::uint64_t>(ChunkSide, settings_.height - oy));
    Chunk out{ w, h, std::vector<std::uint8_t>(w * h, 0) };
    std::vector<std::uint8_t> visited(w * h, 0);
    std::vector<std::uint32_t> stack;
    stack.reserve(w * h);
    stack.push_back(0);
    visited[0] = 1;
    std::uint64_t rng = hash(TagChunk, { c.x, c.y, c.z });

    // Iterative randomized DFS, avoiding recursion/stack overflows.
    // Every tile is connected, including tiles containing a macro portal.
    while (!stack.empty()) {
        std::uint32_t i = stack.back(), x = i % w, y = i / w;
        std::array<std::uint32_t, 4> choices{};
        std::array<int, 4> directions{};
        int n = 0;
        if (y > 0 && !visited[i - w]) { choices[n] = i - w; directions[n++] = 0; }
        if (x + 1 < w && !visited[i + 1]) { choices[n] = i + 1; directions[n++] = 1; }
        if (y + 1 < h && !visited[i + w]) { choices[n] = i + w; directions[n++] = 2; }
        if (x > 0 && !visited[i - 1]) { choices[n] = i - 1; directions[n++] = 3; }
        if (!n) { stack.pop_back(); continue; }
        int j = static_cast<int>(rng_next(rng) % static_cast<std::uint64_t>(n));
        std::uint32_t next = choices[j];
        int direction = directions[j];
        out.passages[i] |= Dir[direction];
        out.passages[next] |= Reverse[direction];
        visited[next] = 1;
        stack.push_back(next);
    }

    // Optional deterministic extra passages. These never remove connectivity.
    for (std::uint32_t y = 0; y < h; ++y) for (std::uint32_t x = 0; x < w; ++x) {
        std::uint32_t i = y * w + x;
        if (settings_.loop_bps) {
            if (x + 1 < w && !(out.passages[i] & East) &&
                hash(TagLoop, {ox + x, oy + y, c.z, 0}) % 10000 < settings_.loop_bps) {
                out.passages[i] |= East; out.passages[i + 1] |= West;
            }
            if (y + 1 < h && !(out.passages[i] & South) &&
                hash(TagLoop, {ox + x, oy + y, c.z, 1}) % 10000 < settings_.loop_bps) {
                out.passages[i] |= South; out.passages[i + w] |= North;
            }
        }
        // Both levels derive this decision from exactly the same lower-floor key.
        if (c.z + 1 < chunks_[2] && extra_stair(ox + x, oy + y, c.z)) out.passages[i] |= Up;
        if (c.z > 0 && extra_stair(ox + x, oy + y, c.z - 1)) out.passages[i] |= Down;
    }

    // Neighboring chunks independently agree on their shared boundary doorway.
    if (c.x + 1 < chunks_[0]) {
        ChunkCoord n{c.x + 1, c.y, c.z};
        if (horizontal_chunk_edge(c, n)) {
            std::uint32_t y = static_cast<std::uint32_t>(edge_hash(c, n, TagHorizontalOffset) % h);
            out.passages[y * w + (w - 1)] |= East;
        }
    }
    if (c.x > 0) {
        ChunkCoord n{c.x - 1, c.y, c.z};
        if (horizontal_chunk_edge(c, n)) {
            std::uint32_t y = static_cast<std::uint32_t>(edge_hash(c, n, TagHorizontalOffset) % h);
            out.passages[y * w] |= West;
        }
    }
    if (c.y + 1 < chunks_[1]) {
        ChunkCoord n{c.x, c.y + 1, c.z};
        if (horizontal_chunk_edge(c, n)) {
            std::uint32_t x = static_cast<std::uint32_t>(edge_hash(c, n, TagHorizontalOffset) % w);
            out.passages[(h - 1) * w + x] |= South;
        }
    }
    if (c.y > 0) {
        ChunkCoord n{c.x, c.y - 1, c.z};
        if (horizontal_chunk_edge(c, n)) {
            std::uint32_t x = static_cast<std::uint32_t>(edge_hash(c, n, TagHorizontalOffset) % w);
            out.passages[x] |= North;
        }
    }

    // Mandatory vertical portals make every floor reachable even with stair_bps=0.
    if (c.z + 1 < chunks_[2]) {
        ChunkCoord n{c.x, c.y, c.z + 1};
        if (base_chunk_edge(c, n)) {
            std::uint32_t x = static_cast<std::uint32_t>(edge_hash(c, n, TagVerticalX) % w);
            std::uint32_t y = static_cast<std::uint32_t>(edge_hash(c, n, TagVerticalY) % h);
            out.passages[y * w + x] |= Up;
        }
    }
    if (c.z > 0) {
        ChunkCoord n{c.x, c.y, c.z - 1};
        if (base_chunk_edge(c, n)) {
            std::uint32_t x = static_cast<std::uint32_t>(edge_hash(c, n, TagVerticalX) % w);
            std::uint32_t y = static_cast<std::uint32_t>(edge_hash(c, n, TagVerticalY) % h);
            out.passages[y * w + x] |= Down;
        }
    }
    return out;
}

const Chunk& Maze::cached_chunk(ChunkCoord c) {
    auto it = cache_.find(c);
    if (it != cache_.end()) {
        lru_.splice(lru_.begin(), lru_, it->second.usage);
        return it->second.chunk;
    }
    Chunk next = generate_chunk(c);
    if (cache_.size() >= cache_limit_) { cache_.erase(lru_.back()); lru_.pop_back(); }
    lru_.push_front(c);
    auto [inserted, ignored] = cache_.emplace(c, CacheEntry{std::move(next), lru_.begin()});
    (void)ignored;
    return inserted->second.chunk;
}

std::uint8_t Maze::passages(Cell c) {
    if (c.x >= settings_.width || c.y >= settings_.height || c.z >= settings_.floors)
        throw std::out_of_range("cell outside the maze");
    const Chunk& chunk = cached_chunk({ c.x / ChunkSide, c.y / ChunkSide, c.z });
    return chunk.at(static_cast<std::uint32_t>(c.x % ChunkSide), static_cast<std::uint32_t>(c.y % ChunkSide));
}

} // namespace labyrinth
