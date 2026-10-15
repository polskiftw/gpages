#pragma once

#include "stair_geometry.hpp"

namespace labyrinth::demo {

// Dedicated handcrafted playground. It deliberately does NOT carve corridors
// from Maze/StairNetwork yet: geometry-reservation integration is the next step.
// All coordinates are dimensionless (tile width=1, story separation=1).
struct Pose {
    double x = -0.70;
    double y = 0.50;
    double feet = 0.0;
};

class Room {
public:
    static constexpr double MinX=-2.0, MaxX=7.0, MinY=-3.0, MaxY=3.0;
    Room();
    const geometry::Flight& flight() const { return flight_; }
    const geometry::Player& player() const { return player_; }
    void configure(bool solid_undercarriage, bool side_walls);
    Pose spawn() const { return {}; }

    // Walks in small steps to prevent tunneling through the stair underside.
    // Resolves X and Y separately for basic sliding against blocked geometry.
    // Returns true iff at least one horizontal step succeeded.
    bool move(Pose& pose, double dx, double dy) const;

    // A valid standing location for an already established foot elevation.
    bool standable(Pose pose) const;

private:
    bool attempt(Pose& pose, double x, double y) const;
    geometry::Flight flight_;
    geometry::Player player_;
};

} // namespace labyrinth::demo
