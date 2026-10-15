#include "maze.hpp"

#include <chrono>
#include <cstdint>
#include <deque>
#include <iomanip>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

using labyrinth::Cell;
using labyrinth::Maze;
using labyrinth::Settings;
using namespace labyrinth;

namespace {
std::uint64_t number(const char* s) {
    std::string text(s);
    if (text.empty() || text[0] == '-' || text[0] == '+') throw std::invalid_argument("expected an unsigned integer");
    std::size_t index = 0;
    std::uint64_t value = std::stoull(text, &index, 0);
    if (index != text.size()) throw std::invalid_argument("invalid integer: " + text);
    return value;
}

Settings parse_settings(int argc, char** argv) {
    Settings s;
    if (argc > 2) s.width = number(argv[2]);
    if (argc > 3) s.height = number(argv[3]);
    if (argc > 4) s.floors = number(argv[4]);
    if (argc > 5) s.seed = number(argv[5]);
    if (argc > 6) {
        auto value = number(argv[6]);
        if (value > 10000) throw std::invalid_argument("loop_bps must be at most 10000");
        s.loop_bps = static_cast<std::uint16_t>(value);
    }
    if (argc > 7) {
        auto value = number(argv[7]);
        if (value > 10000) throw std::invalid_argument("stair_bps must be at most 10000");
        s.stair_bps = static_cast<std::uint16_t>(value);
    }
    return s;
}

void demo(Maze& maze, std::uint64_t level) {
    auto s = maze.settings();
    if (level >= s.floors) throw std::invalid_argument("floor out of range");
    if (s.width > 100 || s.height > 45) throw std::invalid_argument("demo limits display size to 100 x 45 cells");
    std::cout << "Floor " << level << " of " << s.floors << ", seed " << s.seed
              << "  (S=start, ^=up, v=down, B=both)\n";
    for (std::uint64_t x = 0; x < s.width; ++x) std::cout << "+---";
    std::cout << "+\n";
    for (std::uint64_t y = 0; y < s.height; ++y) {
        std::cout << "|";
        for (std::uint64_t x = 0; x < s.width; ++x) {
            std::uint8_t p = maze.passages({x, y, level});
            char marker = x == 0 && y == 0 && level == 0 ? 'S' :
                          (p & Up) && (p & Down) ? 'B' :
                          (p & Up) ? '^' : (p & Down) ? 'v' : ' ';
            std::cout << ' ' << marker << ' ' << ((p & East) ? ' ' : '|');
        }
        std::cout << '\n';
        for (std::uint64_t x = 0; x < s.width; ++x) {
            std::cout << '+' << ((maze.passages({x, y, level}) & South) ? "   " : "---");
        }
        std::cout << "+\n";
    }
}

void check(Maze& maze) {
    const auto s = maze.settings();
    constexpr std::uint64_t max_cells = 250000;
    if (s.width > max_cells || s.height > max_cells / s.width ||
        s.floors > max_cells / (s.width * s.height))
        throw std::invalid_argument("check is intentionally capped at 250,000 cells; use bench/probe for gigantic dimensions");
    const std::uint64_t total = s.width * s.height * s.floors;
    auto idx = [&](Cell c) { return static_cast<std::size_t>((c.z * s.height + c.y) * s.width + c.x); };
    std::vector<std::uint8_t> seen(static_cast<std::size_t>(total), 0);
    std::deque<Cell> queue;
    queue.push_back({0,0,0});
    seen[0] = 1;
    std::uint64_t explored = 0;
    while (!queue.empty()) {
        Cell c = queue.front(); queue.pop_front(); ++explored;
        std::uint8_t p = maze.passages(c);
        auto next = [&](std::uint8_t bit, bool inside, Cell neighbor) {
            if (!(p & bit)) return;
            if (!inside) throw std::runtime_error("opening through world boundary");
            std::size_t i = idx(neighbor);
            if (!seen[i]) { seen[i] = 1; queue.push_back(neighbor); }
        };
        next(North, c.y > 0, {c.x,c.y - (c.y > 0),c.z});
        next(East, c.x + 1 < s.width, {c.x + 1,c.y,c.z});
        next(South, c.y + 1 < s.height, {c.x,c.y + 1,c.z});
        next(West, c.x > 0, {c.x - (c.x > 0),c.y,c.z});
        next(Up, c.z + 1 < s.floors, {c.x,c.y,c.z + 1});
        next(Down, c.z > 0, {c.x,c.y,c.z - (c.z > 0)});
    }
    std::uint64_t edges = 0;
    for (std::uint64_t z = 0; z < s.floors; ++z)
        for (std::uint64_t y = 0; y < s.height; ++y)
            for (std::uint64_t x = 0; x < s.width; ++x) {
                auto p = maze.passages({x,y,z});
                if (p & East) ++edges;
                if (p & South) ++edges;
                if (p & Up) ++edges;
            }
    std::cout << "Cells: " << total << " | Reached: " << explored << " | Open edges: " << edges
              << " | Connected: " << (explored == total ? "YES" : "NO") << '\n';
    if (!s.loop_bps && !s.stair_bps)
        std::cout << "Perfect maze (tree): " << (edges == total - 1 ? "YES" : "NO") << '\n';
    if (explored != total || (!s.loop_bps && !s.stair_bps && edges != total - 1))
        throw std::runtime_error("maze check failed");
}

void bench(Maze& maze, std::uint64_t samples) {
    if (samples > 1000000) throw std::invalid_argument("maximum one million samples");
    auto s = maze.settings();
    std::uint64_t state = s.seed ^ 0xBEEFAA55;
    auto random = [&] {
        state += 0x9E3779B97F4A7C15ULL;
        std::uint64_t x = state;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9ULL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBULL;
        return x ^ (x >> 31);
    };
    std::uint64_t result = 0;
    auto start = std::chrono::steady_clock::now();
    for (std::uint64_t i = 0; i < samples; ++i)
        result += maze.passages({random() % s.width,random() % s.height,random() % s.floors});
    auto elapsed = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
    std::cout << samples << " random queries in " << std::fixed << std::setprecision(3) << elapsed
              << "s; cached chunks: " << maze.cached_chunks() << "; checksum: " << result << '\n';
}

void usage() {
    std::cout << "Usage: maze-cli <demo|check|probe|bench> [width height floors seed loops_bps stairs_bps] [mode arguments]\n"
              << "Default settings: 256 256 3 6549 600 50 (bps means 1/100 of 1%)\n"
              << "  demo: add [floor], maximum 100 x 45 display\n"
              << "  check: finite BFS test, maximum 250000 cells\n"
              << "  probe: add [x y z], returns bitmask for a single cell\n"
              << "  bench: add [samples], default 1000, works on enormous worlds\n";
}
}

int main(int argc, char** argv) {
    try {
        if (argc < 2) { usage(); return 0; }
        std::string command(argv[1]);
        Settings settings = parse_settings(argc, argv);
        Maze maze(settings);
        if (command == "demo") { demo(maze, argc > 8 ? number(argv[8]) : 0); return 0; }
        if (command == "check") { check(maze); return 0; }
        if (command == "bench") { bench(maze, argc > 8 ? number(argv[8]) : 1000); return 0; }
        if (command == "probe") {
            if (argc < 11) { usage(); return 2; }
            Cell c{number(argv[8]), number(argv[9]), number(argv[10])};
            std::cout << "Cell (" << c.x << "," << c.y << "," << c.z << ") passages=0x"
                      << std::hex << static_cast<unsigned>(maze.passages(c)) << std::dec << '\n';
            return 0;
        }
        usage(); return 2;
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << '\n';
        return 1;
    }
}
