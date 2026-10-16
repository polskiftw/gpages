#include "../src/maze_preview.hpp"

#include <stdexcept>
#include <string>
#include <iostream>

using namespace labyrinth;
using namespace labyrinth::preview;

namespace {
void require(bool yes,const char* msg) {if(!yes)throw std::runtime_error(msg);}
std::uint64_t open_horizontal_edges(Maze& m,std::uint64_t z) {
    const auto s=m.settings();
    std::uint64_t edges=0;
    for(std::uint64_t y=0;y<s.height;++y)
        for(std::uint64_t x=0;x<s.width;++x) {
            auto p=m.passages({x,y,z});
            if(p&East)++edges;
            if(p&South)++edges;
        }
    return edges;
}
void test_one_tile() {
    Maze m({1,1,1,6549,0,0});
    auto image=render_floor(m,0);
    require(image.stats.tiles==1,"one-tile size wrong");
    require(image.stats.isolated==1,"isolated tile not counted");
    require(image.stats.wall_segments==4,"one-tile perimeter should have four walls");
    require(image.svg.find("data-wall-segments=\"4\"")!=std::string::npos,
            "wall metadata missing");
    require(image.svg.find(">S</text>")!=std::string::npos,
            "start reference missing");
    require(image.svg.find(">U</text>")==std::string::npos,
            "one-floor world cannot have a vertical up link");
}
void test_exact_edge_counts() {
    for(auto loops : {0,600,2500,10000}) {
        Maze m({32,32,1,6549,static_cast<std::uint16_t>(loops),0});
        const auto image=render_floor(m,0);
        const auto& a=image.stats;
        require(a.tiles==1024,"tile count wrong");
        require(a.dead_ends+a.turns+a.straights+a.tee_junctions+
                a.crossings+a.isolated==a.tiles,"not every tile counted exactly once");
        const auto edges=open_horizontal_edges(m,0);
        require(a.wall_segments==(2*32*32+32+32-edges),
                "wall line count doesn't match the actual generator passage edges");
        if(loops==0) {
            require(edges==1023,"perfect DFS chunk must have exactly N-1 openings");
            require(a.wall_segments==1089,"perfect maze wall count wrong");
        }
    }
}
void test_multi_floor_and_stability() {
    Settings s{48,32,3,4321,600,10000};
    Maze first(s,2),second(s,60);
    const auto a=render_floor(first,1,16),b=render_floor(second,1,16);
    require(a.svg==b.svg,"preview depends on chunk cache size/load order");
    require(a.stats.up_links==s.width*s.height,
            "100% extra shafts should create an Up link in every cell");
    require(a.stats.down_links==s.width*s.height,
            "100% extra shafts should create a Down link in every cell");
    require(a.svg.find(">B</text>")!=std::string::npos,
            "multi-floor bidirectional links missing from SVG");
    const auto floor0=render_floor(first,0,16);
    require(floor0.stats.down_links==0,"ground floor cannot have Down");
    require(floor0.stats.up_links==s.width*s.height,"ground floor missing vertical edge");
    require(floor0.svg!=a.svg,"different floors have identical SVG images");
    require(a.stats.wall_segments==2*s.width*s.height+s.width+s.height-
             open_horizontal_edges(first,1),"multi-chunk wall accounting invalid");
}
void test_preview_bounds() {
    Maze world({161,10,1,7,0,0});
    bool rejected=false;
    try { (void)render_floor(world,0); }
    catch(const std::invalid_argument&) {rejected=true;}
    require(rejected,"oversize preview should be rejected");
    Maze valid({20,20,2,7,0,0});
    rejected=false;
    try { (void)render_floor(valid,2); }
    catch(const std::out_of_range&) {rejected=true;}
    require(rejected,"invalid floor should be rejected");
    rejected=false;
    try { (void)render_floor(valid,0,1); }
    catch(const std::invalid_argument&) {rejected=true;}
    require(rejected,"tiny tile scale should be rejected");
}
}
int main() {
    try {
        test_one_tile();
        test_exact_edge_counts();
        test_multi_floor_and_stability();
        test_preview_bounds();
        std::cout<<"ALL GENERATOR-BASED TOP-DOWN PREVIEW TESTS PASSED\n";
    }catch(const std::exception& e) {
        std::cerr<<"FAILED: "<<e.what()<<'\n';
        return 1;
    }
}
