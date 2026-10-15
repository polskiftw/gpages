#include "playable_mesh.hpp"

#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace labyrinth::demo {
namespace {
struct P { float x,y,z; };

void emit_triangle(Mesh& mesh,P a,P b,P c,Material m) {
    mesh.triangle(a.x,a.y,a.z,b.x,b.y,b.z,c.x,c.y,c.z,m);
}
void emit_quad(Mesh& mesh,P a,P b,P c,P d,Material m) {
    mesh.quad(a.x,a.y,a.z,b.x,b.y,b.z,c.x,c.y,c.z,d.x,d.y,d.z,m);
}
}

void Mesh::triangle(float ax,float ay,float az,float bx,float by,float bz,
                    float cx,float cy,float cz,Material m) {
    const float abx=bx-ax,aby=by-ay,abz=bz-az;
    const float acx=cx-ax,acy=cy-ay,acz=cz-az;
    float nx=aby*acz-abz*acy, ny=abz*acx-abx*acz, nz=abx*acy-aby*acx;
    const float length=std::sqrt(nx*nx+ny*ny+nz*nz);
    if (length<=1e-8f) return;
    nx/=length;ny/=length;nz/=length;
    const auto v=[&](float x,float y,float z) {
        const bool flat=std::abs(nz)>0.65f;
        const bool xwall=std::abs(nx)>std::abs(ny);
        const float u=flat?x:(xwall?y:x);
        const float vv=flat?y:z;
        vertices_.push_back({x,y,z,nx,ny,nz,u,vv,static_cast<float>(m)});
    };
    v(ax,ay,az);v(bx,by,bz);v(cx,cy,cz);
}

void Mesh::quad(float ax,float ay,float az,float bx,float by,float bz,
                float cx,float cy,float cz,float dx,float dy,float dz,Material m) {
    triangle(ax,ay,az,bx,by,bz,cx,cy,cz,m);
    triangle(ax,ay,az,cx,cy,cz,dx,dy,dz,m);
}

void Mesh::box(float x0,float y0,float z0,float x1,float y1,float z1,Material m) {
    if (!(x0<x1 && y0<y1 && z0<z1)) throw std::invalid_argument("box extents must be positive");
    quad(x0,y0,z1,x1,y0,z1,x1,y1,z1,x0,y1,z1,m);
    quad(x0,y1,z0,x1,y1,z0,x1,y0,z0,x0,y0,z0,m);
    quad(x0,y0,z0,x1,y0,z0,x1,y0,z1,x0,y0,z1,m);
    quad(x1,y1,z0,x0,y1,z0,x0,y1,z1,x1,y1,z1,m);
    quad(x0,y1,z0,x0,y0,z0,x0,y0,z1,x0,y1,z1,m);
    quad(x1,y0,z0,x1,y1,z0,x1,y1,z1,x1,y0,z1,m);
}

Mesh make_test_room_mesh(const geometry::Flight& flight) {
    geometry::validate(flight);
    Mesh m;
    // Simple enclosed brick playground: full ground, outer shell, one
    // ceiling/upper-story opening over the staircase and elevated landing.
    m.box(-2,-3,-.09f, 7,3,0,Material::Floor);
    m.box(-2,-3,2,7,3,2.09f,Material::Ceiling);
    m.box(-2,-3,0,-1.92f,3,2.05f,Material::Brick);
    m.box(6.92f,-3,0,7,3,2.05f,Material::Brick);
    m.box(-2,-3,0,7,-2.92f,2.05f,Material::Brick);
    m.box(-2,2.92f,0,7,3,2.05f,Material::Brick);
    // At Z=1 the lower corridor has a ceiling. Only the central stairwell
    // and upper catwalk have a genuine floor/ceiling opening.
    m.box(-2,-2.92f,.99f,-.30f,2.92f,1.05f,Material::Ceiling);
    m.box(-.30f,-2.92f,.99f,6.92f,-.22f,1.05f,Material::Ceiling);
    m.box(-.30f,1.22f,.99f,6.92f,2.92f,1.05f,Material::Ceiling);
    // The upper landing is a distinct z=1 support; floor below it stays
    // walkable with enough ground-level head clearance.
    m.box(4,0,.94f,6.92f,1,1,Material::Floor);
    // Ground-level stripe visually indicates the full Y-crossing under the
    // high end. It is cosmetic and has no special navigation permission.
    m.box(3.48f,-2.90f,.002f,3.72f,-1.16f,.006f,Material::Outline);
    m.box(3.48f,1.16f,.002f,3.72f,2.90f,.006f,Material::Outline);

    // Actual geometry for the existing mathematically-tested flight:
    // each tread/riser is flat; the hollow underside is ONE uninterrupted
    // inclined plane; the solid underside is ONE continuous filled wedge.
    const float run=static_cast<float>(flight.run);
    const float start_x=static_cast<float>(flight.origin.x);
    const float yy0=static_cast<float>(flight.origin.y);
    const float yy1=static_cast<float>(flight.origin.y+flight.width);
    const float zz0=static_cast<float>(flight.base_z);
    const float rise=static_cast<float>(flight.rise);
    const float slab=static_cast<float>(flight.slab);
    const auto along=[&](float t) {
        return flight.reversed ? start_x+run-t*run : start_x+t*run;
    };
    const float low=along(0),high=along(1);
    if (!flight.solid_under) {
        emit_quad(m,{low,yy0,zz0-slab},{low,yy1,zz0-slab},
                    {high,yy1,zz0+rise-slab},{high,yy0,zz0+rise-slab},
                    Material::HollowUnderside);
    } else {
        // A simple uninterrupted masonry infill prism, not random chunks.
        const float toe=along(slab/rise);
        emit_triangle(m,{toe,yy0,zz0},{high,yy0,zz0},
                        {high,yy0,zz0+rise-slab},Material::Brick);
        emit_triangle(m,{high,yy1,zz0},{toe,yy1,zz0},
                        {high,yy1,zz0+rise-slab},Material::Brick);
        emit_quad(m,{toe,yy0,zz0},{toe,yy1,zz0},
                    {high,yy1,zz0+rise-slab},{high,yy0,zz0+rise-slab},
                    Material::Brick);
        emit_quad(m,{high,yy0,zz0},{high,yy1,zz0},
                    {high,yy1,zz0+rise-slab},{high,yy0,zz0+rise-slab},
                    Material::Brick);
    }
    for (std::uint32_t i=0;i<flight.steps;++i) {
        const float ta=static_cast<float>(i)/flight.steps;
        const float tb=static_cast<float>(i+1)/flight.steps;
        const float xa=along(ta),xb=along(tb);
        const float top=zz0+tb*rise;
        const float prev=zz0+ta*rise;
        const float bottom_a=std::max(zz0,prev-slab);
        const float bottom_b=std::max(zz0,top-slab);
        emit_quad(m,{xa,yy0,top},{xb,yy0,top},
                    {xb,yy1,top},{xa,yy1,top},Material::Tread);
        emit_quad(m,{xa,yy0,prev},{xa,yy1,prev},
                    {xa,yy1,top},{xa,yy0,top},Material::Tread);
        emit_quad(m,{xa,yy0,bottom_a},{xb,yy0,bottom_b},
                    {xb,yy0,top},{xa,yy0,top},Material::HollowUnderside);
        emit_quad(m,{xb,yy1,bottom_b},{xa,yy1,bottom_a},
                    {xa,yy1,top},{xb,yy1,top},Material::HollowUnderside);
    }
    if (flight.sides==geometry::Sides::Walled) {
        m.box(0,-.06f,0,4,0,1.75f,Material::Brick);
        m.box(0,1,0,4,1.06f,1.75f,Material::Brick);
    }
    return m;
}

} // namespace labyrinth::demo
