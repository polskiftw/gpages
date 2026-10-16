#include "maze_preview.hpp"

#include <algorithm>
#include <array>
#include <sstream>
#include <stdexcept>
#include <vector>

namespace labyrinth::preview {
namespace {
std::uint32_t degree(std::uint8_t passages) {
    unsigned d=0;
    for (std::uint8_t bit : {North,East,South,West})
        if (passages & bit) ++d;
    return d;
}

void wall(std::ostringstream& path,std::uint64_t x1,std::uint64_t y1,
          std::uint64_t x2,std::uint64_t y2,Stats& stats) {
    path << 'M' << x1 << ' ' << y1 << 'L' << x2 << ' ' << y2;
    ++stats.wall_segments;
}
}

FloorPreview render_floor(Maze& maze,std::uint64_t floor,std::uint32_t tile_px) {
    const Settings settings=maze.settings();
    if(floor>=settings.floors)
        throw std::out_of_range("preview floor outside world");
    if(settings.width>160 || settings.height>160 || tile_px<12 || tile_px>48)
        throw std::invalid_argument("SVG preview limited to 160x160 tiles and tile size 12..48 pixels");

    constexpr std::uint32_t margin=24,top=86,bottom=106;
    const std::uint64_t W=settings.width,H=settings.height;
    const std::uint64_t image_width=2*margin+W*tile_px;
    const std::uint64_t image_height=top+H*tile_px+bottom;
    std::vector<std::uint8_t> bits;
    bits.reserve(static_cast<std::size_t>(W*H));
    Stats stats;
    stats.tiles=W*H;
    for(std::uint64_t y=0;y<H;++y) for(std::uint64_t x=0;x<W;++x) {
        const auto p=maze.passages({x,y,floor});
        bits.push_back(p);
        if(p & Up)++stats.up_links;
        if(p & Down)++stats.down_links;
        const auto deg=degree(p);
        if(deg==0)++stats.isolated;
        else if(deg==1)++stats.dead_ends;
        else if(deg==2) {
            if(((p&North)&&(p&South))||((p&East)&&(p&West)))++stats.straights;
            else ++stats.turns;
        } else if(deg==3)++stats.tee_junctions;
        else ++stats.crossings;
    }

    std::ostringstream walls;
    for(std::uint64_t y=0;y<H;++y) for(std::uint64_t x=0;x<W;++x) {
        const auto p=bits[static_cast<std::size_t>(y*W+x)];
        const auto x0=margin+x*tile_px,x1=x0+tile_px;
        const auto y0=top+y*tile_px,y1=y0+tile_px;
        // Each grid face is emitted at most once: north/west faces per cell,
        // and the final southern/eastern world boundary only.
        if(!(p&North))wall(walls,x0,y0,x1,y0,stats);
        if(!(p&West))wall(walls,x0,y0,x0,y1,stats);
        if(x+1==W && !(p&East))wall(walls,x1,y0,x1,y1,stats);
        if(y+1==H && !(p&South))wall(walls,x0,y1,x1,y1,stats);
    }

    std::ostringstream out;
    out << "<svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\""
           " aria-label=\"Exact deterministic top-down maze floor plan\" viewBox=\"0 0 "
        << image_width << ' ' << image_height << "\">\n"
           "<rect width=\"100%\" height=\"100%\" fill=\"#f8f6f0\"/>\n"
           "<style>text{font-family:Arial,DejaVu Sans,sans-serif;fill:#182332}"
           ".meta{font-size:13px;font-weight:400}"
           ".heading{font-size:21px;font-weight:700}"
           ".marker{font-size:11px;font-weight:700;fill:white;text-anchor:middle;"
           "dominant-baseline:central}</style>\n"
           "<text class=\"heading\" x=\"24\" y=\"34\">PROCEDURAL MAZE — FLOOR "
        << floor+1 << '/' << settings.floors << "</text>\n"
           "<text class=\"meta\" x=\"24\" y=\"57\">"
        << "seed " << settings.seed << "  |  " << W << " × " << H
        << " tiles  |  extra openings " << settings.loop_bps
        << "/10000  |  vertical chance " << settings.stair_bps
        << "/10000</text>\n"
           "<rect x=\"" << margin << "\" y=\"" << top << "\" width=\""
        << W*tile_px << "\" height=\"" << H*tile_px
        << "\" fill=\"#fffdf9\" />\n"
           "<path fill=\"none\" stroke=\"#171c23\" stroke-width=\"2.4\""
           " stroke-linejoin=\"miter\" stroke-linecap=\"square\""
           " data-wall-segments=\"" << stats.wall_segments
        << "\" d=\"" << walls.str() << "\"/>\n";

    for(std::uint64_t y=0;y<H;++y) for(std::uint64_t x=0;x<W;++x) {
        const auto p=bits[static_cast<std::size_t>(y*W+x)];
        const bool up=(p&Up)!=0,down=(p&Down)!=0;
        const bool start=(floor==0 && x==0 && y==0);
        if(!up && !down && !start)continue;
        const auto cx=margin+x*tile_px+tile_px/2;
        const auto cy=top+y*tile_px+tile_px/2;
        const auto radius=std::max<std::uint32_t>(6,tile_px/3);
        // Two shapes encode the semantics even without color perception.
        // S is the deterministic reference origin, U/D/B are ABSTRACT
        // vertical passage bits and not physical staircase footprints.
        if(start) {
            out << "<rect x=\"" << cx-radius << "\" y=\"" << cy-radius
                << "\" width=\"" << 2*radius << "\" height=\""
                << 2*radius << "\" fill=\"#101820\"/>\n";
            out << "<text class=\"marker\" x=\"" << cx << "\" y=\""
                << cy << "\">S</text>\n";
        } else {
            const char marker=up && down?'B':up?'U':'D';
            if(up && down) {
                out << "<rect x=\"" << cx-radius << "\" y=\"" << cy-radius
                    << "\" width=\"" << 2*radius << "\" height=\""
                    << 2*radius << "\" fill=\"#182332\"/>\n";
            } else {
                out << "<circle cx=\"" << cx << "\" cy=\"" << cy << "\" r=\""
                    << radius << "\" fill=\"#182332\"/>\n";
            }
            out << "<text class=\"marker\" x=\"" << cx << "\" y=\""
                << cy << "\">" << marker << "</text>\n";
        }
    }

    const auto footer=top+H*tile_px+30;
    out << "<text class=\"meta\" x=\"24\" y=\"" << footer
        << "\">Dead ends " << stats.dead_ends << "   Turns " << stats.turns
        << "   Straight " << stats.straights
        << "   3-way " << stats.tee_junctions
        << "   4-way " << stats.crossings << "</text>\n"
           "<text class=\"meta\" x=\"24\" y=\"" << footer+22
        << "\">S = reference origin    U = abstract up    D = abstract down"
           "    B = both directions</text>\n"
           "<text class=\"meta\" x=\"24\" y=\"" << footer+43
        << "\">Walls are exactly those returned by Maze::passages."
           " No physical stair space or rooms are modeled yet.</text>\n"
           "</svg>\n";

    return {out.str(),stats};
}
} // namespace labyrinth::preview
