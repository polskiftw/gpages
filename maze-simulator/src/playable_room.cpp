#include "playable_room.hpp"

#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace labyrinth::demo {
namespace {
constexpr double Eps=1e-8;
bool disc_hits_box(double x,double y,double radius,
                   double min_x,double min_y,double max_x,double max_y) {
    const double near_x=std::clamp(x,min_x,max_x);
    const double near_y=std::clamp(y,min_y,max_y);
    return (x-near_x)*(x-near_x)+(y-near_y)*(y-near_y) < radius*radius-Eps;
}
}

Room::Room() { geometry::validate(flight_); geometry::validate(player_); }

void Room::configure(bool solid_undercarriage, bool side_walls) {
    flight_.solid_under=solid_undercarriage;
    flight_.sides=side_walls ? geometry::Sides::Walled : geometry::Sides::Open;
    geometry::validate(flight_);
}

bool Room::standable(Pose p) const {
    if (!std::isfinite(p.x) || !std::isfinite(p.y) || !std::isfinite(p.feet))
        return false;
    const double r=player_.radius;
    if (p.x-r<MinX || p.x+r>MaxX || p.y-r<MinY || p.y+r>MaxY ||
        p.feet < -Eps || p.feet+player_.height > 2.0+Eps) return false;

    const bool x_on_stairs=p.x>=flight_.origin.x && p.x<=flight_.origin.x+flight_.run;
    const bool within_stairs=x_on_stairs &&
        p.y>=flight_.origin.y+r && p.y<=flight_.origin.y+flight_.width-r;
    const double tread=within_stairs ? geometry::tread_height(flight_,p.x) : 0.0;
    const bool standing_on_treads=within_stairs && std::abs(p.feet-tread)<Eps;
    const bool on_upper=p.x>=4.0 && p.x<=MaxX &&
        p.y>=r && p.y<=1.0-r && std::abs(p.feet-1.0)<Eps;
    const bool on_ground=std::abs(p.feet)<Eps;
    if (!standing_on_treads && !on_upper && !on_ground) return false;

    // A Y-facing enclosure wall is a real floor-reaching obstacle. Its
    // continuous physical collision box is independent of the underside fill.
    if (flight_.sides == geometry::Sides::Walled) {
        if (disc_hits_box(p.x,p.y,r,0.0,-0.06,4.0,0.0) ||
            disc_hits_box(p.x,p.y,r,0.0,1.0,4.0,1.06)) return false;
    }

    // When feet are on the actual treads the player is above the staircase,
    // not under its own slab. A body walking across Y beneath the high end
    // is ground-supported, so it must clear the *lowest* slab point anywhere
    // within the body's horizontal radius.
    const bool overlaps_flight=p.x+r>0.0 && p.x-r<4.0 &&
                               p.y+r>0.0 && p.y-r<1.0;
    // The first riser is lower than the player's permitted step-up. When
    // approaching the flight from its LOW end, the body radius reaches the
    // stair nose slightly before its center enters the first tread. Let the
    // next center sample perform the actual step rather than treating this
    // small lip as an impassable wall.
    const bool approaching_low_nose=within_stairs==false &&
        p.y>=flight_.origin.y+r && p.y<=flight_.origin.y+flight_.width-r &&
        ((!flight_.reversed && p.x < flight_.origin.x && p.x+r > flight_.origin.x) ||
         (flight_.reversed && p.x > flight_.origin.x+flight_.run &&
          p.x-r < flight_.origin.x+flight_.run));
    if (overlaps_flight && !standing_on_treads && !on_upper &&
        !approaching_low_nose) {
        if (flight_.solid_under) return false;
        const double worst_x=flight_.reversed ? p.x+r : p.x-r;
        const double underside=geometry::underside_height(flight_,worst_x);
        if (p.feet+player_.height > underside-Eps) return false;
    }

    // Ground-level ceiling panels occupy z=1 outside the stairwell cutout.
    // An upper-floor occupant may only stand on the real catwalk at z=1.
    if (p.feet > Eps && !standing_on_treads && !on_upper) return false;
    return true;
}

bool Room::attempt(Pose& body, double x, double y) const {
    Pose proposal{x,y,0.0};
    const double max_up=body.feet+player_.max_step_up+Eps;
    // Prefer the highest locally reachable supporting surface. A person
    // approaching the high end from *underneath* cannot jump onto it.
    // A person following the flights naturally climbs each 1/16-high riser.
    if (x>=0.0 && x<=4.0 && y>=player_.radius &&
        y<=1.0-player_.radius) {
        const double z=geometry::tread_height(flight_,x);
        if (z<=max_up) proposal.feet=std::max(proposal.feet,z);
    }
    if (x>=4.0 && x<=MaxX && y>=player_.radius &&
        y<=1.0-player_.radius && 1.0<=max_up) proposal.feet=1.0;
    if (!standable(proposal)) return false;
    body=proposal;
    return true;
}

bool Room::move(Pose& body, double dx, double dy) const {
    if (!std::isfinite(dx) || !std::isfinite(dy)) return false;
    // Avoid unbounded iteration on user-supplied input: game frame time
    // limits movement, but this guard also protects the reusable API.
    if (std::max(std::abs(dx),std::abs(dy))>10.0) return false;
    const int n=std::max(1,static_cast<int>(std::ceil(
        std::max(std::abs(dx),std::abs(dy))/0.025)));
    const double step_x=dx/n,step_y=dy/n;
    bool moved=false;
    for(int i=0;i<n;++i) {
        if (std::abs(step_x)>0.0) moved=attempt(body,body.x+step_x,body.y) || moved;
        if (std::abs(step_y)>0.0) moved=attempt(body,body.x,body.y+step_y) || moved;
    }
    return moved;
}

} // namespace labyrinth::demo
