#include "stair_geometry.hpp"

#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>

using namespace labyrinth::geometry;

int main(int argc, char** argv) {
    try {
        const std::string mode = argc > 1 ? argv[1] : "";
        if (argc != 3 || (mode != "open" && mode != "walled" &&
                          mode != "solid" && mode != "solid-walled")) {
            std::cout << "Usage: maze-geometry <open|walled|solid|solid-walled> <output.obj>\n"
                         "open = hollow underside, no walls; walled = hollow with walls;\n"
                         "solid = continuous masonry infill, no walls; solid-walled = both.\n"
                         "Each exported flight may use a different style in a taller stairwell.\n";
            return argc == 1 ? 0 : 2;
        }
        Flight f{};
        f.solid_under = (mode == "solid" || mode == "solid-walled");
        f.sides = (mode == "walled" || mode == "solid-walled")
                  ? Sides::Walled : Sides::Open;
        const Player p{};
        const bool high = crosses_under(f,{3.6,-0.6},{3.6,1.6},p);
        const bool low = crosses_under(f,{1.2,-0.6},{1.2,1.6},p);
        std::ofstream output(argv[2],std::ios::binary);
        if (!output) throw std::runtime_error("cannot create OBJ output");
        write_demo_room_obj(output,f);
        output.close();
        if (!output) throw std::runtime_error("OBJ output write failed");
        std::cout << "Stairs (0,0,0) to (4,0,1): "
                  << (climbable(f,p) ? "CLIMBABLE" : "BLOCKED") << '\n'
                  << "Ground route across Y at X=3.6: " << (high?"OPEN UNDERPASS":"BLOCKED") << '\n'
                  << "Ground route across Y at X=1.2: " << (low?"OPEN UNDERPASS":"BLOCKED") << '\n'
                  << "Walls: " << (f.sides==Sides::Open?"none":"floor-reaching enclosure") << '\n'
                  << "Undercarriage: " << (f.solid_under?"fully solid infill":"uniform hollow slab") << '\n'
                  << "Mesh: " << argv[2] << '\n'
                  << "OBJ export is a test room, NOT connected to the full maze geometry yet.\n";
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << '\n';
        return 1;
    }
}
