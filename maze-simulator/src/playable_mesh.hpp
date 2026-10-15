#pragma once

#include "stair_geometry.hpp"

#include <cstdint>
#include <vector>

namespace labyrinth::demo {

enum class Material : std::uint8_t {
    Brick = 0, Floor = 1, Ceiling = 2, Tread = 3,
    HollowUnderside = 4, Outline = 5
};

// XYZ uses Z-up; material ID is a float for simple OpenGL vertex attributes.
// UV coordinates are generated from local geometry, not an external texture.
struct Vertex {
    float x,y,z, nx,ny,nz, u,v, material;
};

class Mesh {
public:
    const std::vector<Vertex>& vertices() const { return vertices_; }
    void triangle(float ax,float ay,float az,float bx,float by,float bz,
                  float cx,float cy,float cz,Material m);
    void quad(float ax,float ay,float az,float bx,float by,float bz,
              float cx,float cy,float cz,float dx,float dy,float dz,Material m);
    void box(float x0,float y0,float z0,float x1,float y1,float z1,Material m);
private:
    std::vector<Vertex> vertices_;
};

// Rebuild this bounded room mesh when 1/2/3/4 change staircase construction.
// Exactly the same analytic Flight properties are used by the collision model.
Mesh make_test_room_mesh(const geometry::Flight& flight);

} // namespace labyrinth::demo
