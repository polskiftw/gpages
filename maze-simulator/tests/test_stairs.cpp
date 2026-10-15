#include "../src/stairs.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

using namespace labyrinth;

namespace {
void require(bool ok, const std::string& message) {
    if (!ok) throw std::runtime_error(message);
}

void test_enclosed_23_floor_run() {
    Maze m({32,32,24,6549,0,0});
    StairNetwork net(m,{23,23,10000,10000,10000});
    auto t = net.for_chunk(0,0,0);
    require(t && t->bottom == 0 && t->top == 23 && t->enclosed, "missing 23-floor enclosed staircase");
    for (std::uint64_t floor = 0; floor < 24; ++floor) {
        Cell c{t->x,t->y,floor};
        auto found = net.at(c);
        require(found && found->bottom == 0 && found->top == 23, "tower continuity broken");
        bool entry = (floor == 0 || floor == 23);
        require(net.has_landing(*t, floor) == entry, "enclosed staircase has invalid intermediate entrance");
        auto inside = net.neighbors({c,Space::StairInterior});
        require(inside.size() == 2, "wrong internal staircase degree");
        require(std::count_if(inside.begin(),inside.end(),[](auto n){ return n.space == Space::Corridor; })
                == (entry ? 1 : 0), "closed level incorrectly joins floor corridors");
        auto corridor = net.neighbors({c,Space::Corridor});
        require(std::count_if(corridor.begin(),corridor.end(),[](auto n){ return n.space == Space::StairInterior; })
                == (entry ? 1 : 0), "enclosed staircase can be entered from closed floor");
    }
    std::cout << "ok: 23-floor continuous enclosed staircase with endpoint-only landings\n";
}

void test_all_floor_landings() {
    Maze m({1,1,45,42,0,0});
    StairNetwork net(m,{44,44,10000,10000,0});
    auto t = net.for_chunk(0,0,0);
    require(t && t->bottom == 0 && t->top == 44 && !t->enclosed, "missing fully accessible long stairwell");
    for (std::uint64_t z = 0; z < 45; ++z) {
        require(net.has_landing(*t,z), "missing intermediate floor landing");
        auto from_tower = net.neighbors({{0,0,z}, Space::StairInterior});
        require(std::find(from_tower.begin(),from_tower.end(),NavNode{{0,0,z},Space::Corridor}) != from_tower.end(),
                "open landing has no corridor access");
    }
    std::cout << "ok: tall stairwell with an exit on every floor\n";
}

void test_optional_disabled() {
    Maze m({35,35,30,42,0,0});
    StairNetwork net(m,{29,1,0,10000,0});
    require(!net.for_chunk(0,0,0), "staircase exists despite density=0");
    for (std::uint64_t z = 0; z < 30; ++z)
        require(!net.at({0,0,z}), "disabled staircase found by cell lookup");
    std::cout << "ok: disabled long staircases preserve base maze\n";
}

void test_augmented_connectivity(Settings world, StairSettings options) {
    Maze maze(world,8);
    StairNetwork stairs(maze,options);
    const std::size_t floor_count = static_cast<std::size_t>(world.width * world.height * world.floors);
    std::vector<std::uint8_t> seen_floor(floor_count,0);
    std::vector<std::uint8_t> seen_tower(floor_count,0);
    auto index = [&](Cell c) { return static_cast<std::size_t>((c.z*world.height+c.y)*world.width+c.x); };
    auto seen = [&](NavNode n) -> std::uint8_t& {
        return (n.space == Space::Corridor ? seen_floor : seen_tower)[index(n.cell)];
    };
    std::deque<NavNode> queue;
    queue.push_back({{0,0,0},Space::Corridor});
    seen(queue.front()) = 1;
    std::uint64_t reached_floor = 0, reached_stairs = 0;
    while (!queue.empty()) {
        NavNode node = queue.front(); queue.pop_front();
        if (node.space == Space::Corridor) ++reached_floor;
        else ++reached_stairs;
        for (const NavNode& neighbor : stairs.neighbors(node)) {
            auto reverse = stairs.neighbors(neighbor);
            require(std::find(reverse.begin(),reverse.end(),node) != reverse.end(), "asymmetric navigation link");
            if (!seen(neighbor)) { seen(neighbor) = 1; queue.push_back(neighbor); }
        }
    }
    std::uint64_t expected_stairs = 0, total_towers = 0;
    const std::uint64_t cxcount = (world.width-1)/32+1, cycount = (world.height-1)/32+1;
    const std::uint64_t bands = (world.floors-1)/stairs.band_size()+1;
    for (std::uint64_t cx=0;cx<cxcount;++cx) for(std::uint64_t cy=0;cy<cycount;++cy)
        for(std::uint64_t band=0;band<bands;++band) {
            const auto t=stairs.for_chunk(cx,cy,band);
            if (!t) continue;
            ++total_towers;
            expected_stairs += t->top - t->bottom + 1;
            for(std::uint64_t z=t->bottom;z<=t->top;++z) {
                auto node = NavNode{{t->x,t->y,z},Space::StairInterior};
                require(stairs.at(node.cell).has_value(),"tower missing internal node");
                require(seen(node),"tower interior unreachable from floor maze");
            }
        }
    require(reached_floor == floor_count, "floor maze disconnected by stair logic");
    require(reached_stairs == expected_stairs, "stair interior component unreachable");
    std::cout << "ok: augmented graph " << world.width << 'x' << world.height << 'x' << world.floors
              << " floor cells=" << reached_floor << " stair nodes=" << reached_stairs
              << " towers=" << total_towers << "\n";
}

void test_cross_band_runs() {
    // 34x1 puts chunk #1 at a partial 2x1 edge. Two bands have genuinely
    // overlapping floor ranges, but their reserved anchors never collide.
    bool found_overlap = false;
    for (std::uint64_t seed = 1; seed < 64 && !found_overlap; ++seed) {
        Maze m({34,1,90,seed,0,0});
        StairNetwork net(m,{23,23,10000,0,10000});
        auto a = net.for_chunk(1,0,0), b = net.for_chunk(1,0,1);
        require(a && b, "missing cross-band candidate tower");
        require(a->x != b->x || a->y != b->y, "overlapping bands share a stairwell footprint");
        const std::uint64_t zmin=std::max(a->bottom,b->bottom), zmax=std::min(a->top,b->top);
        if (zmin > zmax) continue;
        found_overlap = true;
        for (std::uint64_t z=zmin;z<=zmax;++z) {
            auto at_a=net.at({a->x,a->y,z}), at_b=net.at({b->x,b->y,z});
            require(at_a && at_a->band == 0, "previous-band staircase vanished at boundary");
            require(at_b && at_b->band == 1, "current-band staircase vanished at boundary");
        }
    }
    require(found_overlap, "never produced overlapping two-band staircase intervals");
    // One-cell chunks cannot reserve two distinct XY positions. The odd band
    // is deliberately omitted so no overlapping stair structures are created.
    Maze tiny({1,1,90,7,0,0});
    StairNetwork one(tiny,{23,23,10000,0,10000});
    require(one.for_chunk(0,0,0).has_value(), "tiny even band missing");
    require(!one.for_chunk(0,0,1).has_value(), "tiny odd band was not suppressed");
    require(one.for_chunk(0,0,2).has_value(), "tiny second even band missing");
    for (std::uint64_t z=0;z<90;++z) {
        auto t=one.at({0,0,z});
        if (t) require(t->bottom <= z && t->top >= z, "tiny tower out of interval");
    }
    std::cout << "ok: long runs cross band boundaries without overlapping XY anchors\n";
}

void test_variable_heights() {
    Maze m({64,64,80,31,0,0});
    StairNetwork net(m,{39,1,10000,8000,0});
    std::uint64_t min_seen=UINT64_MAX,max_seen=0,total=0,landing_count=0;
    for(std::uint64_t cx=0;cx<2;++cx) for(std::uint64_t cy=0;cy<2;++cy)
        for(std::uint64_t band=0;band<2;++band) {
            auto t=net.for_chunk(cx,cy,band);
            if (!t) continue;
            ++total;
            std::uint64_t rise=t->top-t->bottom;
            min_seen=std::min(min_seen,rise); max_seen=std::max(max_seen,rise);
            require(rise>=1 && rise<=39,"staircase span outside configured limits");
            for(std::uint64_t z=t->bottom;z<=t->top;++z)
                if(net.has_landing(*t,z)) ++landing_count;
        }
    require(total>=4 && max_seen>min_seen,"staircases do not vary in height");
    require(landing_count>total*2,"intermediate stops not generated");
    std::cout << "ok: variable rises and intermediate-access floors\n";
}

void test_random_access_huge() {
    Maze m({1000000000000ULL,1000000000007ULL,3000000ULL,12345,0,0},1);
    StairSettings settings{100000,1,10000,5000,2500};
    StairNetwork one(m,settings), two(m,settings);
    const std::uint64_t cx = (m.settings().width-1)/32, cy=(m.settings().height-1)/32;
    const std::uint64_t last_band=(m.settings().floors-1)/one.band_size();
    for (auto c : std::vector<std::uint64_t>{0ULL,1ULL,17ULL,cx}) for (auto b : std::vector<std::uint64_t>{0ULL,1ULL,last_band}) {
        auto a=one.for_chunk(c,std::min(c,cy),b), d=two.for_chunk(c,std::min(c,cy),b);
        require(a.has_value() == d.has_value(),"huge tower generation nondeterministic");
        if (!a) continue;
        require(a->x == d->x && a->y == d->y && a->bottom == d->bottom && a->top == d->top,
                "huge tower parameters nondeterministic");
        require(a->top > a->bottom && a->top < m.settings().floors,"huge tower out of bounds");
        require(one.at({a->x,a->y,a->bottom}).has_value() &&
                one.at({a->x,a->y,a->top}).has_value(),"endpoints not queryable");
    }
    require(!one.for_chunk(cx+1,cy+1,last_band+1),"invalid huge-world chunk present");
    std::cout << "ok: gigantic world random-access staircase queries\n";
}


void test_underside_construction_sections() {
    // A 63-story stairwell contains complete per-story flights.
    // The underside design can change ONLY at a floor boundary, and by
    // default changes are grouped into runs of four whole stories.
    Maze m({32,32,64,2401,0,0});
    StairSettings settings{63,63,10000,3000,0,5000,4};
    StairNetwork a(m,settings),b(m,settings);
    const auto t=a.for_chunk(0,0,0);
    require(t && t->bottom == 0 && t->top == 63,"missing long stair for style test");
    for (std::uint64_t z=0;z<63;++z) {
        const auto value=a.solid_under(*t,z);
        require(value==b.solid_under(*t,z),"stair understructure must be deterministic");
        if ((z%settings.construction_section_floors)!=0)
            require(value==a.solid_under(*t,z-1),
                    "underside style changed mid-section instead of at a floor-group boundary");
    }
    std::size_t observed_changes=0;
    for (std::uint64_t seed=1;seed<=32;++seed) {
        Maze world({32,32,64,seed,0,0});
        StairNetwork net(world,settings);
        auto tower=net.for_chunk(0,0,0);
        require(tower.has_value(),"missing forced stair in seeded structural test");
        for (std::uint64_t z=settings.construction_section_floors;z<tower->top;
             z+=settings.construction_section_floors) {
            if (net.solid_under(*tower,z)!=net.solid_under(*tower,z-1))
                ++observed_changes;
        }
    }
    require(observed_changes>0,"the same large staircase must be allowed to change between floors");
    StairNetwork always_solid(m,{63,63,10000,0,10000,10000,1});
    StairNetwork always_hollow(m,{63,63,10000,0,10000,0,1});
    for (std::uint64_t z=0;z<t->top;++z) {
        require(always_solid.solid_under(*t,z),"solid-only mode generated hollow flight");
        require(!always_hollow.solid_under(*t,z),"hollow-only mode generated solid flight");
    }
    bool rejected=false;
    try { (void)a.solid_under(*t,t->top); }
    catch(const std::out_of_range&) {rejected=true;}
    require(rejected,"stair top landing erroneously considered a flight");
    std::cout << "ok: deterministic whole-flight underside sections; changes only between floors\n";
}

void test_last_floor_arithmetic() {
    constexpr std::uint64_t maximum = std::numeric_limits<std::uint64_t>::max();
    Maze m({1,2,maximum,301,0,0},1);
    StairNetwork net(m,{1000,1,10000,7500,2000});
    for (std::uint64_t floor : {maximum-1,maximum-2,maximum-999,maximum-1001}) {
        const auto candidate=net.at({0,0,floor});
        if (candidate) {
            require(candidate->top < maximum && candidate->bottom < candidate->top,
                    "staircase overflowed world floor boundary");
            auto edges=net.neighbors({{0,0,floor},Space::StairInterior});
            for(auto dest:edges) require(dest.cell.z < maximum,"stairs neighbor overflowed U64 ceiling");
        }
    }
    require(!net.at({0,0,maximum}),"out-of-range floor returned a staircase");
    std::cout << "ok: world height near UINT64_MAX does not overflow\n";
}

void test_invalid_config() {
    Maze m({1,1,1,1,0,0});
    bool bad = false;
    try { StairNetwork s(m,{0,1,10000,10000,10000}); } catch (const std::invalid_argument&) { bad = true; }
    require(bad,"accepted zero max_rise");
    bad = false;
    try { StairNetwork s(m,{12,13,10000,10000,10000}); } catch (const std::invalid_argument&) { bad = true; }
    require(bad,"accepted min_rise > max_rise");
    bad = false;
    try { StairNetwork s(m,{12,1,10001,10000,10000}); } catch (const std::invalid_argument&) { bad = true; }
    require(bad,"accepted out-of-range density");
    bad = false;
    try { StairNetwork s(m,{std::numeric_limits<std::uint64_t>::max(),1,10000,10000,10000}); }
    catch (const std::invalid_argument&) { bad = true; }
    require(bad,"accepted overflowing max_rise");
    bad = false;
    try { StairNetwork s(m,{12,1,10000,0,0,10001,4}); }
    catch (const std::invalid_argument&) { bad = true; }
    require(bad,"accepted out-of-range solid understructure percentage");
    bad = false;
    try { StairNetwork s(m,{12,1,10000,0,0,5000,0}); }
    catch (const std::invalid_argument&) { bad = true; }
    require(bad,"accepted zero-story construction section");
    std::cout << "ok: configuration validation\n";
}
}

int main() {
    try {
        test_enclosed_23_floor_run();
        test_all_floor_landings();
        test_optional_disabled();
        test_cross_band_runs();
        test_variable_heights();
        test_augmented_connectivity({1,1,24,1,0,0}, {23,23,10000,0,10000});
        test_augmented_connectivity({33,35,8,2,1000,0}, {7,1,10000,3000,2000});
        test_augmented_connectivity({50,40,25,3,100,10}, {24,4,7500,5000,5000});
        test_augmented_connectivity({63,68,13,4,400,100}, {12,1,2500,7000,3000});
        test_random_access_huge();
        test_underside_construction_sections();
        test_last_floor_arithmetic();
        test_invalid_config();
        std::cout << "ALL STAIR TESTS PASSED\n";
    } catch (const std::exception& e) {
        std::cerr << "FAILED: " << e.what() << '\n';
        return 1;
    }
}
