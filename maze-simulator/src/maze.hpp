#pragma once

#include <cstddef>
#include <cstdint>
#include <list>
#include <initializer_list>
#include <unordered_map>
#include <vector>

namespace labyrinth {

// Each cell stores the OPEN passages. A missing bit is a wall or solid floor.
enum Passage : std::uint8_t { North = 1, East = 2, South = 4, West = 8, Up = 16, Down = 32 };

struct Cell { std::uint64_t x, y, z; };
struct ChunkCoord {
    std::uint64_t x, y, z;
    bool operator==(const ChunkCoord&) const = default;
};

struct Settings {
    std::uint64_t width = 256, height = 256, floors = 3, seed = 0x1995;
    // Basis points, 0 to 10000. 10000 means 100%.
    std::uint16_t loop_bps = 600;  // Extra horizontal intra/inter-chunk openings.
    std::uint16_t stair_bps = 50;  // Additional shafts at individual XY cells per floor pair.
};

struct Chunk {
    std::uint32_t width, height;
    // Row-major passage bits. Stored as one byte per cell (max 1024 bytes).
    std::vector<std::uint8_t> passages;
    std::uint8_t at(std::uint32_t x, std::uint32_t y) const { return passages[y * width + x]; }
};

// Purely procedural, finite, always connected 3D maze. Generation costs O(chunk area
// + log(number of chunks)) per uncached chunk, NOT O(total world volume).
// The object is not thread-safe; use one instance per worker or synchronize access.
class Maze {
public:
    static constexpr std::uint32_t ChunkSide = 32;

    explicit Maze(Settings settings, std::size_t cache_limit = 128);
    const Settings& settings() const { return settings_; }
    std::uint8_t passages(Cell cell);
    Chunk generate_chunk(ChunkCoord chunk) const; // Order-independent, no cache required.
    std::size_t cached_chunks() const { return cache_.size(); }
    std::size_t cache_limit() const { return cache_limit_; }

private:
    struct CoordHash { std::size_t operator()(ChunkCoord c) const noexcept; };
    struct CacheEntry { Chunk chunk; std::list<ChunkCoord>::iterator usage; };
    struct Box { std::uint64_t lo[3], hi[3]; };
    struct Split { int axis; std::uint64_t cut; };

    Settings settings_;
    std::uint64_t chunks_[3];
    std::size_t cache_limit_;
    std::list<ChunkCoord> lru_;
    std::unordered_map<ChunkCoord, CacheEntry, CoordHash> cache_;

    static std::uint64_t mix(std::uint64_t value);
    std::uint64_t hash(std::uint64_t tag, std::initializer_list<std::uint64_t> values) const;
    std::uint64_t box_hash(const Box& box, std::uint64_t tag) const;
    std::uint64_t edge_hash(ChunkCoord a, ChunkCoord b, std::uint64_t tag) const;
    Split split_box(const Box& box) const;
    bool base_chunk_edge(ChunkCoord a, ChunkCoord b) const;
    bool horizontal_chunk_edge(ChunkCoord a, ChunkCoord b) const;
    bool extra_stair(std::uint64_t x, std::uint64_t y, std::uint64_t lower_floor) const;
    const Chunk& cached_chunk(ChunkCoord chunk);
};

} // namespace labyrinth
