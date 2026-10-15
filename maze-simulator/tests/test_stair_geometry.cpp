#include "../src/stair_geometry.hpp"

#include <algorithm>
#include <cmath>
#include <iostream>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>

using namespace labyrinth::geometry;

namespace {
void require(bool ok, const char* why) {
    if (!ok) throw std::runtime_error(why);
}
void near(double a, double b, const char* why) {
    require(std::abs(a-b) < 1e-9,why);
}
void test_underpass() {
    const Flight flight{};
    const Player player{};
    // Corridor passes in Y, perpendicular to the staircase running in X.
    const Point2 near_side_high{3.60,-0.60}, far_side_high{3.60,1.60};
    require(crosses_under(flight,near_side_high,far_side_high,player),
            "high side should have a usable underpass");
    require(crosses_under(flight,far_side_high,near_side_high,player),
            "underpass must be bidirectional");
    require(!crosses_under(flight,{1.2,-0.6},{1.2,1.6},player),
            "low side of stairs must NOT allow a player through");
    require(!crosses_under(flight,{3.0,-0.6},{3.0,1.6},Player{0.12,0.90,0.07}),
            "insufficient headroom must block taller player");
    require(!crosses_under(flight,{0.2,-0.6},{0.2,1.6},player),
            "walking through the first riser is impossible");
    require(!crosses_under(flight,{3.6,0.1},{3.6,1.6},player),
            "an endpoint already within the flight cannot be called a full underpass");
    require(!crosses_under(flight,{-2.0,-0.6},{-2.0,1.6},player),
            "off-footprint crossing is not under the flight");

    Flight enclosed=flight; enclosed.sides=Sides::Walled;
    require(!crosses_under(enclosed,near_side_high,far_side_high,player),
            "floor-reaching side walls must block the underpass");
    Flight thinner=flight; thinner.slab=0.25;
    require(!crosses_under(thinner,{3.6,-0.6},{3.6,1.6},player),
            "thick supporting slab should invalidate underpass clearance");
    Flight reversed=flight; reversed.reversed=true;
    require(crosses_under(reversed,{0.40,-0.6},{0.40,1.6},player),
            "reversed staircase must permit the underpass at its NEW high end");
    require(!crosses_under(reversed,{3.60,-0.6},{3.60,1.6},player),
            "reversed staircase low end should now be blocked");
    std::cout << "ok: open underside, enclosure walls, radius/headroom and reversed incline\n";
}

void test_step_geometry() {
    Flight f{};
    Player p{};
    require(climbable(f,p),"16 treads should be walkable without jumping");
    near(tread_height(f,0.0),0.0,"bottom landing height");
    near(tread_height(f,0.01),0.0625,"first tread height");
    near(tread_height(f,1.01),0.3125,"fifth tread height");
    near(tread_height(f,4.0),1.0,"top landing height");
    near(underside_height(f,3.6),0.84,"continuous sloped underside");
    double previous=tread_height(f,0.0);
    for (int i=1;i<=160;++i) {
        const double next=tread_height(f,4.0*i/160.0);
        require(next>=previous,"steps must never descend in climb direction");
        require(next-previous<=p.max_step_up+1e-9,"riser exceeds player step limit");
        previous=next;
    }
    f.steps=8;
    require(!climbable(f,p),"eight taller risers require higher step-up allowance");
    p.max_step_up=0.15;
    require(climbable(f,p),"larger step-up must admit eight risers");
    p.radius=0.6;
    require(!climbable(f,p),"walker too broad to fit between stairway sides");
    std::cout << "ok: sixteen real treads, one-story ascent and player proportions\n";
}

void test_long_switchback() {
    Flight template_f{};
    for (std::uint32_t i=0;i<23;++i) {
        Flight f=switchback_story(template_f,i);
        require(f.reversed==(i%2==1),"switchback must alternate direction");
        near(f.base_z,static_cast<double>(i),"story vertical origin");
        require(climbable(f,Player{}),"each story must be climbable");
        const double entry_x=f.reversed?f.origin.x+f.run:f.origin.x;
        const double exit_x=f.reversed?f.origin.x:f.origin.x+f.run;
        near(tread_height(f,entry_x),static_cast<double>(i),"story entry elevation");
        near(tread_height(f,exit_x),static_cast<double>(i+1),"story exit elevation");
        if (i<22) {
            Flight next=switchback_story(template_f,i+1);
            const double next_entry=next.reversed?next.origin.x+next.run:next.origin.x;
            near(exit_x,next_entry,"adjacent floors must share actual landing XY");
            near(tread_height(next,next_entry),tread_height(f,exit_x),
                 "adjacent flights must join at the same physical height");
        }
    }
    std::cout << "ok: 23 contiguous flights with aligned turn landings and no teleporting\n";
}

void test_obj() {
    Flight f{};
    std::ostringstream out;
    write_demo_room_obj(out,f);
    const std::string mesh=out.str();
    const auto count = [&](const std::string& prefix) {
        std::size_t c=0,start=0;
        while (start<mesh.size()) {
            const auto end=mesh.find('\n',start);
            if (mesh.compare(start,prefix.size(),prefix)==0) ++c;
            if (end==std::string::npos) break;
            start=end+1;
        }
        return c;
    };
    require(count("v ") > 8*16, "mesh has too few 3D vertices");
    require(count("f ") >= 12*16, "mesh missing solid tread triangles");
    require(count("g ") == 3, "open model should have three named groups");
    f.sides=Sides::Walled;
    std::ostringstream walled;
    write_demo_room_obj(walled,f);
    require(walled.str().find("floor_reaching_enclosure_walls")!=std::string::npos,
            "enclosed model missing wall objects");
    require(walled.str().size()>mesh.size(),"walled model should contain additional mesh");
    std::cout << "ok: valid-looking OBJ vertex/face counts and optional walls\n";
}

void test_invalid() {
    auto bad=[](auto action){try{action();}catch(const std::invalid_argument&){return true;}return false;};
    require(bad([]{Flight f;f.steps=0;validate(f);}),"zero step count accepted");
    require(bad([]{Flight f;f.rise=0;validate(f);}),"zero rise accepted");
    require(bad([]{Flight f;f.run=std::numeric_limits<double>::infinity();validate(f);}),"infinite run accepted");
    require(bad([]{Player p;p.radius=0;validate(p);}),"zero player radius accepted");
    std::cout << "ok: rejects degenerate and non-finite geometry\n";
}
}

int main() {
    try {
        test_underpass();
        test_step_geometry();
        test_long_switchback();
        test_obj();
        test_invalid();
        std::cout << "ALL STAIR GEOMETRY TESTS PASSED\n";
    } catch(const std::exception& e) {
        std::cerr << "FAILED: " << e.what() << '\n';
        return 1;
    }
}
