#include "../src/playable_mesh.hpp"
#include "../src/playable_room.hpp"

#include <cmath>
#include <iostream>
#include <stdexcept>
#include <string>

using namespace labyrinth::demo;

namespace {
void check(bool v,const char* message) {
    if(!v)throw std::runtime_error(message);
}
void close(double a,double b,double tolerance,const char* message) {
    check(std::abs(a-b)<=tolerance,message);
}
void test_climb_and_descend() {
    for(int style=0;style<4;++style) {
        Room room;
        room.configure(style>=2,(style%2)!=0);
        Pose p=room.spawn();
        check(room.standable(p),"spawn invalid");
        for(int i=0;i<440;++i)room.move(p,0.015,0.0);
        check(p.x>5.0,"never reached the upper-floor corridor");
        close(p.feet,1.0,1e-8,"failed to climb real step treads to the upper floor");
        check(room.standable(p),"arrived at invalid upper landing");
        for(int i=0;i<440;++i)room.move(p,-0.015,0.0);
        close(p.feet,0.0,1e-8,"failed to descend stairs to lower floor");
        close(p.x,room.spawn().x,0.04,"could not return to starting X");
        check(room.standable(p),"ending pose invalid");
    }
    std::cout<<"ok: walk up/down every stair tread with all four physical styles\n";
}
void test_real_underpasses() {
    for(int style=0;style<4;++style) {
        Room room;
        room.configure(style>=2,(style%2)!=0);
        Pose p{3.6,-1.6,0.0};
        check(room.standable(p),"underpass corridor starting point invalid");
        const bool moved=room.move(p,0,3.2);
        check(moved,"ground-level route could not begin");
        if(style==0) {
            check(p.y>1.45,"hollow un-walled stairs did not permit full underpass");
            close(p.feet,0.0,1e-9,"underpass magically lifted player onto stairway");
        } else {
            check(p.y<0.0,"solid infill or side walls improperly allowed an underpass");
        }
        check(room.standable(p),"crossing left player inside solid geometry");
    }
    Room hollow;
    Pose low{1.20,-1.5,0};
    hollow.move(low,0,3.0);
    check(low.y<0.0,"low-end crossing should be blocked by insufficient headroom");
    Pose high{3.6,-1.5,0};
    hollow.move(high,0,3.0);
    check(high.y>1.4,"valid high-end crossing wrongly rejected");
    close(high.feet,0.0,1e-9,"passing under staircase incorrectly mounted it");
    std::cout<<"ok: true high-end underpass, low-end headroom and solid/walled blocking\n";
}
void test_collisions() {
    Room room;
    Pose p=room.spawn();
    check(!room.standable({-3,0,0}),"outer brick wall not solid");
    check(!room.standable({3.6,0.5,0}),"feet cannot stand inside low-floor slab");
    check(room.standable({3.6,0.5,0.9375}),"stair step top should be standable");
    check(!room.standable({3.6,0.5,0.75}),"floating between underside and tread");
    check(!room.move(p,1000,0),"unbounded movement should be rejected");
    close(p.x,room.spawn().x,1e-9,"invalid input changed player position");
    for(int i=0;i<1000;++i)room.move(p,-0.03,0.0);
    check(p.x>Room::MinX,"player passed through outer wall");
    Room enclosed;
    enclosed.configure(false,true);
    check(!enclosed.standable({3.6,-0.13,0.0}),
          "side wall radius clearance should block touch of masonry");
    std::cout<<"ok: world perimeter, radius, impossible elevation and sliding protection\n";
}
void test_mesh_variants() {
    std::size_t vertex_count[4]{};
    for(int mode=0;mode<4;++mode) {
        Room room;
        room.configure(mode>=2,mode%2!=0);
        const auto mesh=make_test_room_mesh(room.flight());
        const auto& verts=mesh.vertices();
        check(verts.size()>800 && verts.size()%3==0,"missing 3D triangles");
        bool floor=false,wall=false,steps=false;
        for(const auto& v:verts) {
            check(std::isfinite(v.x)&&std::isfinite(v.y)&&std::isfinite(v.z),
                  "non-finite geometry vertex");
            if(v.material==static_cast<float>(Material::Floor)) floor=true;
            if(v.material==static_cast<float>(Material::Brick)) wall=true;
            if(v.material==static_cast<float>(Material::Tread)) steps=true;
        }
        check(floor && wall && steps,"essential room materials missing");
        vertex_count[mode]=verts.size();
    }
    check(vertex_count[1]>vertex_count[0],"walls not added to hollow flight mesh");
    check(vertex_count[3]>vertex_count[2],"walls not added to solid flight mesh");
    std::cout<<"ok: finite triangulated brick room, roof, landings, treads and all variants\n";
}
}
int main() {
    try {
        test_climb_and_descend();
        test_real_underpasses();
        test_collisions();
        test_mesh_variants();
        std::cout<<"ALL PLAYABLE ROOM PHYSICS TESTS PASSED\n";
    } catch(const std::exception& e) {
        std::cerr<<"FAILED: "<<e.what()<<'\n';
        return 1;
    }
}
