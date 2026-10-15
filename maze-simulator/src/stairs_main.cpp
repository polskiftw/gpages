#include "stairs.hpp"

#include <algorithm>
#include <cstdint>
#include <iostream>
#include <stdexcept>
#include <string>

using namespace labyrinth;

namespace {
std::uint64_t as_u64(const char* value) {
    const std::string input(value);
    if (input.empty() || input.front() == '-') throw std::invalid_argument("expected nonnegative integer");
    std::size_t n = 0;
    auto result = std::stoull(input,&n,10);
    if (n != input.size()) throw std::invalid_argument("invalid numeric input");
    return result;
}
std::uint16_t as_bps(const char* s) {
    auto x=as_u64(s);
    if (x>10000) throw std::invalid_argument("probabilities must be at most 10000 basis points");
    return static_cast<std::uint16_t>(x);
}
}

int main(int argc, char** argv) {
    try {
        if (argc != 1 && (argc < 5 || argc > 12))
            throw std::invalid_argument("usage: maze-stairs [width height floors seed [max_rise [min_rise [density_bps [landing_bps [enclosed_bps [solid_under_bps [section_floors]]]]]]]]");
        Settings world{128,128,96,6549,0,0};
        StairSettings settings;
        if (argc >= 5) {
            world.width=as_u64(argv[1]); world.height=as_u64(argv[2]);
            world.floors=as_u64(argv[3]); world.seed=as_u64(argv[4]);
        }
        if (argc >= 6) settings.max_rise=as_u64(argv[5]);
        if (argc >= 7) settings.min_rise=as_u64(argv[6]);
        if (argc >= 8) settings.density_bps=as_bps(argv[7]);
        if (argc >= 9) settings.landing_bps=as_bps(argv[8]);
        if (argc >= 10) settings.enclosed_bps=as_bps(argv[9]);
        if (argc >= 11) settings.solid_under_bps=as_bps(argv[10]);
        if (argc >= 12) settings.construction_section_floors=as_u64(argv[11]);
        Maze maze(world);
        StairNetwork network(maze,settings);
        std::cout << "Seed " << world.seed << " | finite maze " << world.width << 'x'
                  << world.height << 'x' << world.floors << " floors | stair rises "
                  << settings.min_rise << ".." << settings.max_rise << "\n";
        std::cout << "Inspecting the first four chunk columns and floor bands on each axis (not full-world enumeration):\n\n";
        const std::uint64_t x_chunks=(world.width-1)/Maze::ChunkSide+1;
        const std::uint64_t y_chunks=(world.height-1)/Maze::ChunkSide+1;
        const std::uint64_t bands=(world.floors-1)/network.band_size()+1;
        std::uint64_t found=0;
        for(std::uint64_t b=0;b<std::min<std::uint64_t>(bands,4);++b)
            for(std::uint64_t cy=0;cy<std::min<std::uint64_t>(y_chunks,4);++cy)
                for(std::uint64_t cx=0;cx<std::min<std::uint64_t>(x_chunks,4);++cx) {
                    const auto t=network.for_chunk(cx,cy,b);
                    if (!t) continue;
                    ++found;
                    std::cout << "Stair " << found << ": tile (" << t->x << ',' << t->y
                              << "), floors " << t->bottom+1 << " -> " << t->top+1
                              << " (rises " << t->top-t->bottom << "), "
                              << (t->enclosed ? "enclosed" : "intermediate landings enabled")
                              << ", preferred layout "
                              << (t->shape==StairShape::Spiral ? "spiral" : "switchback") << "\n"
                              << "   Entrances: " << t->bottom+1;
                    const std::uint64_t limit=std::min<std::uint64_t>(t->top-t->bottom-1,64);
                    for(std::uint64_t step=1;step<=limit;++step) {
                        const auto z=t->bottom+step;
                        if (network.has_landing(*t,z)) std::cout << ", " << z+1;
                    }
                    if (t->top-t->bottom-1 > limit) std::cout << ", ...";
                    std::cout << ", " << t->top+1 << "\n";
                    // Bounded, human-readable S/H display, NEVER iterating an
                    // entire million-story tower just to print its metadata.
                    const std::uint64_t flights=t->top-t->bottom;
                    const std::uint64_t shown=std::min<std::uint64_t>(flights,48);
                    std::cout << "   Underside per full flight (S=solid, H=hollow): ";
                    for (std::uint64_t step=0;step<shown;++step) {
                        if (step && step%settings.construction_section_floors==0)
                            std::cout << ' ';
                        std::cout << (network.solid_under(*t,t->bottom+step)?'S':'H');
                    }
                    if (shown<flights) std::cout << " ...";
                    std::cout << "\n";
                }
        if (!found) std::cout << "No long staircases in this sample. Increase density_bps or inspect another seed.\n";
        std::cout << "\nLong-stair metadata is generated, including per-flight underside style,\n"
                     "but the physical stair geometry is not yet installed into the full maze.\n";
    } catch (const std::exception& error) {
        std::cerr << "Error: " << error.what() << '\n';
        return 1;
    }
}
