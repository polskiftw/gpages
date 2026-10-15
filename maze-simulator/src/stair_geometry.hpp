#pragma once

#include <cstdint>
#include <iosfwd>

namespace labyrinth::geometry {

// All dimensions are dimensionless world units: tile width = 1, story rise = 1.
// World coordinates can be integer/64-bit elsewhere; meshes should be local
// to the camera to avoid loss of floating-point precision in huge mazes.
struct Point2 { double x, y; };

struct Player {
    double radius = 0.12;       // Horizontal collision footprint.
    double height = 0.65;       // Standing height above the current floor.
    double max_step_up = 0.07;  // Allows one of sixteen 1/16-height risers.
};

enum class Sides : std::uint8_t { Open, Walled };

// A single real flight connecting two adjacent story landings. A long
// staircase composes many such flights (alternating direction), regardless
// of how many intermediate landings connect to outside corridors.
struct Flight {
    Point2 origin{0.0, 0.0}; // lower-X, lower-Y corner of its horizontal footprint
    double base_z = 0.0;
    double run = 4.0;
    double width = 1.0;
    double rise = 1.0;
    double slab = 0.06; // Thin underside supporting the treads.
    std::uint32_t steps = 16;
    bool reversed = false; // If true, ascends toward *decreasing* X.
    Sides sides = Sides::Open;
};

void validate(const Flight& flight);
void validate(const Player& player);

// A point strictly inside the flight footprint has a staircase surface.
// Outside its horizontal footprint this returns nullopt-style false.
bool in_footprint(const Flight& f, Point2 location);
double tread_height(const Flight& f, double x);
double underside_height(const Flight& f, double x);

// Can a player climb the complete staircase as a sequence of small steps?
// Assumes no unrelated maze walls at its two end landings.
bool climbable(const Flight& f, const Player& p);

// Analytic conservative clearance check for a straight ground-level path
// crossing *perpendicularly across* the width of this staircase. Includes
// player radius and the sloped underside; any floor-reaching side walls
// block the crossing. This intentionally does NOT grant access to the
// staircase's upper surface or override the maze's planned corridor walls.
// The caller must separately ensure both ends are valid maze corridors.
bool crosses_under(const Flight& f, Point2 start, Point2 finish, const Player& p);

// World-local repeated switchback flight: stories alternate climb direction.
// Use a small LOCAL story index relative to the renderer's origin, not a
// huge absolute uint64 world Z cast to double.
Flight switchback_story(Flight template_flight, std::uint32_t local_story);

// Write a self-contained OBJ demo room (no material libraries/dependencies).
// OBJ contains a ground slab, stair treads with sloped thin undersides, an
// upper landing, and optionally floor-reaching side walls.
void write_demo_room_obj(std::ostream& out, const Flight& flight);

} // namespace labyrinth::geometry
