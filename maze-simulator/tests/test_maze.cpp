#include "../src/maze.hpp"

#include <cstdint>
#include <algorithm>
#include <deque>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

using namespace labyrinth;

namespace {
void require(bool condition, const std::string& message) {
    if (!condition) throw std::runtime_error(message);
}

void verify(Settings s) {
    Maze maze(s, 13);
    const std::size_t count = static_cast<std::size_t>(s.width * s.height * s.floors);
    std::vector<std::uint8_t> used(count, 0);
    auto index = [&](std::uint64_t x, std::uint64_t y, std::uint64_t z) {
        return static_cast<std::size_t>((z * s.height + y) * s.width + x);
    };
    std::deque<Cell> queue;
    queue.push_back({0,0,0}); used[0] = 1;
    std::size_t reached = 0, edges = 0;
    while (!queue.empty()) {
        Cell c = queue.front(); queue.pop_front(); ++reached;
        std::uint8_t p = maze.passages(c);
        auto edge = [&](std::uint8_t bit, std::uint8_t opposite, bool inside, Cell n) {
            if (!(p & bit)) return;
            require(inside, "passage escapes world bounds");
            require((maze.passages(n) & opposite) != 0, "asymmetric passage");
            auto id = index(n.x,n.y,n.z);
            if (!used[id]) { used[id] = 1; queue.push_back(n); }
        };
        edge(North,South,c.y>0,{c.x,c.y-(c.y>0),c.z});
        edge(East,West,c.x+1<s.width,{c.x+1,c.y,c.z});
        edge(South,North,c.y+1<s.height,{c.x,c.y+1,c.z});
        edge(West,East,c.x>0,{c.x-(c.x>0),c.y,c.z});
        edge(Up,Down,c.z+1<s.floors,{c.x,c.y,c.z+1});
        edge(Down,Up,c.z>0,{c.x,c.y,c.z-(c.z>0)});
        edges += static_cast<std::size_t>((p & East) != 0) + static_cast<std::size_t>((p & South) != 0) + static_cast<std::size_t>((p & Up) != 0);
    }
    require(reached == count, "disconnected maze");
    if (!s.loop_bps && !s.stair_bps) require(edges == count - 1, "perfect maze does not have V-1 edges");
    require(maze.cached_chunks() <= 13, "LRU cache exceeded bound");
    std::cout << "ok: " << s.width << 'x' << s.height << 'x' << s.floors
              << " seed=" << s.seed << " loops=" << s.loop_bps << " stairs=" << s.stair_bps
              << " cells=" << count << " edges=" << edges << '\n';
}

void test_order_independence() {
    Settings s{71, 77, 4, 899001, 2500, 750};
    Maze first(s, 1), second(s, 64);
    std::vector<ChunkCoord> order{{0,0,0},{2,2,3},{2,0,1},{1,1,2},{0,2,0},{1,0,3},{0,1,1}};
    std::vector<Chunk> snapshots;
    for (auto c : order) snapshots.push_back(first.generate_chunk(c));
    for (std::size_t i = order.size(); i-- > 0; ) {
        auto actual = second.generate_chunk(order[i]);
        require(actual.width == snapshots[i].width && actual.height == snapshots[i].height &&
                actual.passages == snapshots[i].passages, "generation changed with request order");
    }
    for (auto c : order) {
        for (std::uint64_t y = c.y * Maze::ChunkSide;
             y < std::min((c.y + 1) * Maze::ChunkSide, s.height); ++y)
            for (std::uint64_t x = c.x * Maze::ChunkSide;
                 x < std::min((c.x + 1) * Maze::ChunkSide, s.width); ++x) {
                require(first.passages({x,y,c.z}) == second.passages({x,y,c.z}), "cache affects output");
            }
    }
    require(first.cached_chunks() == 1, "cache limit 1 did not evict");
    Settings changed = s;
    changed.seed++;
    Maze different(changed);
    require(first.generate_chunk({1,1,2}).passages != different.generate_chunk({1,1,2}).passages,
            "changing seed did not change representative chunk");
    std::cout << "ok: seed reproducibility, order independence, cache eviction\n";
}

void test_huge_world() {
    Settings s{1000000000000ULL, 1000000000007ULL, 3000000ULL, 987654321ULL, 800, 100};
    Maze one(s, 1), two(s, 3);
    std::vector<Cell> cells{
        {0,0,0}, {1,0,0}, {31,31,0}, {32,32,1}, {999999999999ULL,1000000000006ULL,2999999},
        {999999999998ULL,1000000000006ULL,2999999}, {999999999999ULL,1000000000005ULL,2999999},
        {999999999999ULL,1000000000006ULL,2999998}, {999999999999ULL,7,1500000}
    };
    for (auto p : cells) require(one.passages(p) == two.passages(p), "huge-world nondeterminism");
    auto p = cells[4];
    auto current = one.passages(p);
    auto west = one.passages(cells[5]);
    auto north = one.passages(cells[6]);
    auto below = one.passages(cells[7]);
    require(((current & West)!=0) == ((west & East)!=0), "huge-world X border mismatch");
    require(((current & North)!=0) == ((north & South)!=0), "huge-world Y border mismatch");
    require(((current & Down)!=0) == ((below & Up)!=0), "huge-world floor border mismatch");
    require(one.cached_chunks() <= 1 && two.cached_chunks() <= 3, "huge-world cache memory overrun");
    std::cout << "ok: trillion-scale dimensions, millions of floors, constant-size cache\n";
}
}

int main() {
    try {
        for (auto s : std::vector<Settings>{
            {1,1,1,1,0,0}, {1,1,15,5,0,0}, {1,127,1,6,0,0},
            {127,1,1,6,0,0}, {3,3,8,7,0,0}, {33,31,3,8,0,0},
            {47,74,4,9,0,0}, {93,68,7,42,0,0}, {77,79,3,1024,2000,200},
            {60,60,4,11,10000,10000}, {1,2,2,12,10000,10000},
            {34,65,4,99999,100,0}, {31,33,4,1234,0,5000}
        }) verify(s);
        test_order_independence();
        test_huge_world();
        std::cout << "ALL TESTS PASSED\n";
    } catch (const std::exception& e) {
        std::cerr << "FAILED: " << e.what() << '\n';
        return 1;
    }
}
