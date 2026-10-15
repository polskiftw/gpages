#include "stair_geometry.hpp"

#include <algorithm>
#include <cmath>
#include <iomanip>
#include <ostream>
#include <stdexcept>

namespace labyrinth::geometry {
namespace {
constexpr double Epsilon = 1e-10;

bool finite(double v) { return std::isfinite(v); }
void positive(double v, const char* name) {
    if (!finite(v) || !(v > 0.0)) throw std::invalid_argument(name);
}

double progress(const Flight& f, double x) {
    const double t = f.reversed ? (f.origin.x + f.run - x) / f.run
                                : (x - f.origin.x) / f.run;
    return std::clamp(t, 0.0, 1.0);
}

struct Point3 { double x, y, z; };

class Obj {
public:
    explicit Obj(std::ostream& o) : out(o) { out << "# dimensionless stair test room: local coordinates\n"; }
    void group(const char* name) { out << "g " << name << '\n'; }
    void quad(Point3 a, Point3 b, Point3 c, Point3 d) {
        const std::uint64_t n = next_vertex;
        for (auto p : {a,b,c,d})
            out << "v " << p.x << ' ' << p.y << ' ' << p.z << '\n';
        out << "f " << n << ' ' << n+1 << ' ' << n+2 << '\n'
            << "f " << n << ' ' << n+2 << ' ' << n+3 << '\n';
        next_vertex += 4;
    }
    void box(Point3 low, Point3 high) {
        quad({low.x,low.y,high.z},{high.x,low.y,high.z},{high.x,high.y,high.z},{low.x,high.y,high.z});
        quad({low.x,high.y,low.z},{high.x,high.y,low.z},{high.x,low.y,low.z},{low.x,low.y,low.z});
        quad({low.x,low.y,low.z},{high.x,low.y,low.z},{high.x,low.y,high.z},{low.x,low.y,high.z});
        quad({high.x,high.y,low.z},{low.x,high.y,low.z},{low.x,high.y,high.z},{high.x,high.y,high.z});
        quad({low.x,high.y,low.z},{low.x,low.y,low.z},{low.x,low.y,high.z},{low.x,high.y,high.z});
        quad({high.x,low.y,low.z},{high.x,high.y,low.z},{high.x,high.y,high.z},{high.x,low.y,high.z});
    }
private:
    std::ostream& out;
    std::uint64_t next_vertex = 1;
};
}

void validate(const Flight& f) {
    if (!finite(f.origin.x) || !finite(f.origin.y) || !finite(f.base_z))
        throw std::invalid_argument("flight origin/base must be finite");
    positive(f.run, "flight run must be positive");
    positive(f.width, "flight width must be positive");
    positive(f.rise, "flight rise must be positive");
    positive(f.slab, "stair slab thickness must be positive");
    if (f.steps == 0 || f.steps > 1000000)
        throw std::invalid_argument("flight steps must be 1..1000000");
    if (!finite(f.origin.x + f.run) || !finite(f.origin.y + f.width) ||
        !finite(f.base_z + f.rise))
        throw std::invalid_argument("flight extents overflow");
}

void validate(const Player& p) {
    positive(p.radius, "player radius must be positive");
    positive(p.height, "player height must be positive");
    positive(p.max_step_up, "maximum step-up must be positive");
}

bool in_footprint(const Flight& f, Point2 p) {
    validate(f);
    if (!finite(p.x) || !finite(p.y)) return false;
    return p.x >= f.origin.x && p.x <= f.origin.x + f.run &&
           p.y >= f.origin.y && p.y <= f.origin.y + f.width;
}

double tread_height(const Flight& f, double x) {
    validate(f);
    if (!finite(x)) throw std::invalid_argument("x must be finite");
    const double t = progress(f, x);
    // A single tread is flat, and risers increase by rise/steps.
    const double stair_index = std::ceil(t * f.steps - Epsilon);
    return f.base_z + f.rise * std::clamp(stair_index, 0.0, static_cast<double>(f.steps)) / f.steps;
}

double underside_height(const Flight& f, double x) {
    validate(f);
    if (!finite(x)) throw std::invalid_argument("x must be finite");
    // A thin continuous sloped supporting slab instead of a solid triangular
    // pile of stairs; the void underneath is actual empty navigable volume.
    return f.base_z + progress(f,x) * f.rise - f.slab;
}

bool climbable(const Flight& f, const Player& p) {
    validate(f); validate(p);
    return f.width + Epsilon >= 2.0 * p.radius &&
           f.rise / f.steps <= p.max_step_up + Epsilon;
}

bool crosses_under(const Flight& f, Point2 start, Point2 finish, const Player& p) {
    validate(f); validate(p);
    if (!finite(start.x) || !finite(start.y) || !finite(finish.x) || !finite(finish.y))
        throw std::invalid_argument("crossing endpoints must be finite");
    // The test is specifically a corridor crossing beneath the stairs in Y,
    // rather than walking up the stairs in X. Its endpoints must lie on
    // opposite sides with the player's full disc clear of the footprint.
    const double lower_y = f.origin.y - p.radius;
    const double upper_y = f.origin.y + f.width + p.radius;
    if (!((start.y < lower_y && finish.y > upper_y) ||
          (finish.y < lower_y && start.y > upper_y))) return false;
    if (f.sides == Sides::Walled) return false;

    // Solve exactly where the line crosses the broadened stair strip.
    // At both strip boundaries the body's X-radius must remain inside the run.
    const double x_low = start.x + (finish.x - start.x) * (lower_y - start.y) / (finish.y - start.y);
    const double x_high = start.x + (finish.x - start.x) * (upper_y - start.y) / (finish.y - start.y);
    const double x_min = std::min(x_low,x_high) - p.radius;
    const double x_max = std::max(x_low,x_high) + p.radius;
    if (x_min < f.origin.x || x_max > f.origin.x + f.run) return false;

    // The slab slopes monotonically. Check the LOWEST underside anywhere
    // under the whole swept player footprint, not merely its center.
    const double worst_x = f.reversed ? x_max : x_min;
    return underside_height(f,worst_x) - f.base_z >= p.height + Epsilon;
}

Flight switchback_story(Flight template_flight, std::uint32_t local_story) {
    validate(template_flight);
    template_flight.base_z += static_cast<double>(local_story) * template_flight.rise;
    template_flight.reversed = template_flight.reversed != ((local_story & 1U) != 0);
    validate(template_flight);
    return template_flight;
}

void write_demo_room_obj(std::ostream& out, const Flight& f) {
    validate(f);
    out << std::fixed << std::setprecision(9);
    Obj obj(out);
    obj.group("ground_floor");
    obj.box({f.origin.x-1.0, f.origin.y-1.5, f.base_z-0.04},
            {f.origin.x+f.run+2.0,f.origin.y+f.width+1.5,f.base_z});
    obj.group("elevated_landing");
    const double x1=f.reversed ? f.origin.x-1.0 : f.origin.x+f.run;
    const double x2=f.reversed ? f.origin.x : f.origin.x+f.run+1.0;
    obj.box({x1,f.origin.y,f.base_z+f.rise-0.04},
            {x2,f.origin.y+f.width,f.base_z+f.rise});
    obj.group("stairs_with_open_underneath");
    const double len = f.run / f.steps;
    for (std::uint32_t i=0;i<f.steps;++i) {
        const double ta=static_cast<double>(i)/f.steps;
        const double tb=static_cast<double>(i+1)/f.steps;
        const double xa=f.reversed ? f.origin.x+f.run-ta*f.run : f.origin.x+ta*f.run;
        const double xb=f.reversed ? f.origin.x+f.run-tb*f.run : f.origin.x+tb*f.run;
        const double y0=f.origin.y,y1=f.origin.y+f.width;
        const double top=f.base_z+tb*f.rise;
        const double under_a=f.base_z+ta*f.rise-f.slab;
        const double under_b=f.base_z+tb*f.rise-f.slab;
        (void)len;
        obj.quad({xa,y0,top},{xb,y0,top},{xb,y1,top},{xa,y1,top});
        obj.quad({xa,y1,under_a},{xb,y1,under_b},{xb,y0,under_b},{xa,y0,under_a});
        obj.quad({xa,y0,under_a},{xb,y0,under_b},{xb,y0,top},{xa,y0,top});
        obj.quad({xa,y1,top},{xb,y1,top},{xb,y1,under_b},{xa,y1,under_a});
        obj.quad({xa,y0,under_a},{xa,y0,top},{xa,y1,top},{xa,y1,under_a});
        obj.quad({xb,y0,top},{xb,y0,under_b},{xb,y1,under_b},{xb,y1,top});
    }
    if (f.sides == Sides::Walled) {
        obj.group("floor_reaching_enclosure_walls");
        constexpr double t=0.06;
        const double top=f.base_z+f.rise+0.75;
        obj.box({f.origin.x,f.origin.y-t,f.base_z},
                {f.origin.x+f.run,f.origin.y,top});
        obj.box({f.origin.x,f.origin.y+f.width,f.base_z},
                {f.origin.x+f.run,f.origin.y+f.width+t,top});
    }
}

} // namespace labyrinth::geometry
