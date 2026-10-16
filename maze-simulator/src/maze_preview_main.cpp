#include "maze_preview.hpp"

#include <array>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

using labyrinth::Maze;
using labyrinth::Settings;
using labyrinth::preview::Stats;
using labyrinth::preview::render_floor;

namespace {
std::uint64_t number(const char* str) {
    std::string s=str;
    if(s.empty() || s.front()=='-' || s.front()=='+')
        throw std::invalid_argument("numbers must be unsigned integers");
    std::size_t used=0;
    const auto result=std::stoull(s,&used,0);
    if(used!=s.size())throw std::invalid_argument("invalid number: "+s);
    return result;
}
std::string base64(const std::string& bytes) {
    constexpr char alphabet[]="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string result;
    result.reserve((bytes.size()+2)/3*4);
    for(std::size_t i=0;i<bytes.size();i+=3) {
        const auto a=static_cast<unsigned char>(bytes[i]);
        const bool has_b=i+1<bytes.size(),has_c=i+2<bytes.size();
        const unsigned b=has_b?static_cast<unsigned char>(bytes[i+1]):0;
        const unsigned c=has_c?static_cast<unsigned char>(bytes[i+2]):0;
        result+=alphabet[a>>2];
        result+=alphabet[((a&3U)<<4)|(b>>4)];
        result+=has_b?alphabet[((b&15U)<<2)|(c>>6)]:'=';
        result+=has_c?alphabet[c&63U]:'=';
    }
    return result;
}
void write_file(const std::filesystem::path& dest,const std::string& text) {
    const auto parent=dest.parent_path();
    if(!parent.empty())std::filesystem::create_directories(parent);
    std::ofstream file(dest,std::ios::binary|std::ios::trunc);
    if(!file)throw std::runtime_error("cannot write "+dest.string());
    file<<text;
    if(!file)throw std::runtime_error("failed writing "+dest.string());
}
std::string stats_label(const Stats& m) {
    return "dead ends "+std::to_string(m.dead_ends)+" | turns "+
           std::to_string(m.turns)+" | straight "+std::to_string(m.straights)+
           " | junctions "+std::to_string(m.tee_junctions)+
           " | crossroads "+std::to_string(m.crossings)+
           " | U "+std::to_string(m.up_links)+" / D "+std::to_string(m.down_links);
}

struct Example {
    const char* id;
    const char* name;
    const char* detail;
    Settings settings;
};

// All previews below come from the actual existing core C++ generator.
// Deliberately do not fake "long hallway" or "rooms" presets until those
// algorithms have been designed and proven separately.
constexpr std::array<Example,5> examples{{
    {"perfect","No extra loops",
     "The original depth-first corridor maze. Every pair of cells has one route.",
     {32,32,1,6549,0,0}},
    {"default","Default: 6% extra openings",
     "Same seed and corridor layout, but occasional additional wall openings.",
     {32,32,1,6549,600,0}},
    {"loopy","More loops: 25%",
     "Same underlying maze, with substantially more shortcuts and crossroads.",
     {32,32,1,6549,2500,0}},
    {"alternate","Different seed",
     "Same settings as the default, with a completely different corridor arrangement.",
     {32,32,1,1995,600,0}},
    {"floors","Three floors / chunk boundary",
     "48 x 32 cells with three floors. U/D/B show abstract vertical graph links.",
     {48,32,3,6549,600,50}}
}};

void gallery(const std::filesystem::path& dir) {
    std::filesystem::create_directories(dir);
    std::vector<std::pair<std::string,std::string>> svg_assets;
    std::ostringstream dataset;
    dataset<<"[\n";
    for(std::size_t index=0;index<examples.size();++index) {
        const auto& example=examples[index];
        Maze maze(example.settings);
        if(index)dataset<<",\n";
        dataset<<"{id:\""<<example.id<<"\",name:\""<<example.name
               <<"\",detail:\""<<example.detail<<"\",seed:\""
               <<example.settings.seed<<"\",dims:\""<<example.settings.width
               <<" x "<<example.settings.height<<" x "<<example.settings.floors
               <<"\",loop:"<<example.settings.loop_bps<<",levels:[";
        for(std::uint64_t z=0;z<example.settings.floors;++z) {
            auto out=render_floor(maze,z,20);
            const auto name=std::string(example.id)+"_floor"+std::to_string(z+1)+".svg";
            write_file(dir/name,out.svg);
            svg_assets.emplace_back(name,std::move(out.svg));
            if(z)dataset<<',';
            dataset<<"{file:\""<<name<<"\",info:\""<<stats_label(out.stats)<<"\"}";
            std::cout<<"Wrote "<<(dir/name).string()<<" - "<<stats_label(out.stats)<<'\n';
        }
        dataset<<"]}";
    }
    dataset<<"\n]";

    std::ostringstream html;
    html<<R"HTML(<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Maze mathematics — top-down gallery</title>
<style>
:root{color-scheme:light;font:16px/1.55 system-ui,-apple-system,Segoe UI,sans-serif;background:#efeee9;color:#1e2834}
*{box-sizing:border-box}body{max-width:1380px;margin:0 auto;padding:24px}
header{margin-bottom:18px}h1{font-size:clamp(1.55rem,3vw,2.2rem);letter-spacing:-.04em;margin:0}
.subtitle{max-width:850px;margin:6px 0 14px;color:#475563}
main{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}
section{background:white;padding:18px;border:1px solid #c9cbd0;border-radius:12px;min-width:0}
section h2{font-size:1.06rem;margin:0 0 12px}
label{font-size:.8rem;font-weight:700;display:block;margin:7px 0 4px}
select{width:100%;border:1px solid #a2aab6;background:#fff;border-radius:6px;padding:9px 10px;font:inherit;color:#142233}
.controls{display:grid;grid-template-columns:3fr 1fr;gap:10px}
img{display:block;width:100%;height:auto;background:#f8f6f0;aspect-ratio:1/1;object-fit:contain}
.preview{border:1px solid #b7bcc3;margin:14px 0 8px;overflow:auto;background:#f8f6f0}
.metadata{color:#43505d;font-size:.85rem;min-height:55px}
a{color:#173b72;text-decoration:underline;text-underline-offset:2px}
.footnote{margin-top:20px;padding:13px 0;border-top:1px solid #bac2c6;color:#43505d}
.legend{font-size:.9rem;margin-bottom:8px;font-weight:600}
small{font-size:.8rem}
@media(max-width:850px){body{padding:13px}main{grid-template-columns:1fr}}
</style></head><body>
<header>
<h1>Maze mathematics — real generated floor plans</h1>
<p class="subtitle">These are exact outputs of the C++ maze generator, not concept art or a JavaScript reimplementation. Compare seeds, loop probabilities, and floor layers before we settle the architectural algorithms.</p>
<div class="legend">Thin dark lines = solid walls; light spaces = walkable tiles. S = start reference, U = up, D = down, B = both.</div>
</header>
<main>
<section id="panel-a"><h2>Layout A</h2><div class="controls">
<div><label for="world-a">Generation preset</label><select id="world-a"></select></div>
<div><label for="floor-a">Floor</label><select id="floor-a"></select></div>
</div><div class="preview"><img id="image-a" alt="Exact maze floor plan A"></div>
<div class="metadata" id="meta-a"></div><a id="open-a" target="_blank" rel="noopener">Open full-resolution SVG</a>
</section>
<section id="panel-b"><h2>Layout B — comparison</h2><div class="controls">
<div><label for="world-b">Generation preset</label><select id="world-b"></select></div>
<div><label for="floor-b">Floor</label><select id="floor-b"></select></div>
</div><div class="preview"><img id="image-b" alt="Exact maze floor plan B"></div>
<div class="metadata" id="meta-b"></div><a id="open-b" target="_blank" rel="noopener">Open full-resolution SVG</a>
</section>
</main>
<div class="footnote"><strong>Important:</strong> This is the existing corridor <em>graph</em>. No large rooms or corridor-width styles have been implemented yet. U/D/B are abstract vertical links, <strong>not</strong> tested physical staircases or permitted underpasses. The intended renderer will consume a future validated geometry-aware version of this math, not invent paths on its own. All assets are static: this page needs no server and works offline.</div>
<script>
const catalogue=)HTML";
    html<<dataset.str()<<";\n";
    html<<R"HTML(
function init(side,initial) {
  const world=document.getElementById('world-'+side);
  const floor=document.getElementById('floor-'+side);
  const image=document.getElementById('image-'+side);
  const meta=document.getElementById('meta-'+side);
  const link=document.getElementById('open-'+side);
  catalogue.forEach((c,i)=>world.add(new Option(c.name,i)));
  function updateWorld(){
    const c=catalogue[Number(world.value)];
    floor.replaceChildren();
    c.levels.forEach((_,i)=>floor.add(new Option(String(i+1),i)));
    updateFloor();
  }
  function updateFloor(){
    const c=catalogue[Number(world.value)],level=c.levels[Number(floor.value)];
    image.src=level.file;
    image.alt='Actual '+c.dims+' maze floor '+(Number(floor.value)+1)+' of '+c.levels.length;
    meta.textContent=c.detail+' | Seed '+c.seed+' | '+c.dims+' tiles | '+level.info;
    link.href=level.file;
  }
  world.addEventListener('change',updateWorld);
  floor.addEventListener('change',updateFloor);
  world.value=String(initial);
  updateWorld();
}
init('a',1);init('b',2);
</script></body></html>
)HTML";
    write_file(dir/"index.html",html.str());
    // Make the full gallery portable as ONE HTML file, even if the user
    // opens it directly from a download or moves it away from the SVG folder.
    // SVG bytes are the same authoritative, already-computed C++ output.
    std::string standalone=html.str();
    for(const auto& asset:svg_assets) {
        const std::string needle="file:\""+asset.first+"\"";
        const std::string replacement="file:\"data:image/svg+xml;base64,"+
                                      base64(asset.second)+"\"";
        const auto location=standalone.find(needle);
        if(location==std::string::npos)
            throw std::runtime_error("standalone gallery missing SVG reference");
        standalone.replace(location,needle.size(),replacement);
    }
    write_file(dir/"standalone.html",standalone);
    std::cout<<"Open "<<(dir/"standalone.html").string()
             <<" in Firefox (one self-contained file; no server required).\n";
}
void usage() {
    std::cout<<"Usage:\n"
       "  maze-preview gallery [output_dir]\n"
       "  maze-preview svg <path.svg> <width> <height> <floors> <seed> <loops_bps> <stairs_bps> <floor_index>\n"
       "The floor index is zero-based. The SVG preview is capped at 160x160 cells only for readability.\n"
       "Rooms, stair collision footprints and underpass volumes are NOT yet generated.\n";
}
}

int main(int argc,char** argv) {
    try {
        if(argc>=2 && std::string(argv[1])=="gallery" && argc<=3) {
            gallery(argc==3?argv[2]:"maze-preview-gallery");
            return 0;
        }
        if(argc==10 && std::string(argv[1])=="svg") {
            Settings s{};
            const std::filesystem::path dest(argv[2]);
            s.width=number(argv[3]);s.height=number(argv[4]);
            s.floors=number(argv[5]);s.seed=number(argv[6]);
            const auto loop=number(argv[7]),stair=number(argv[8]);
            if(loop>10000 || stair>10000)throw std::invalid_argument("chance in basis points must be <=10000");
            s.loop_bps=static_cast<std::uint16_t>(loop);
            s.stair_bps=static_cast<std::uint16_t>(stair);
            Maze maze(s);
            const auto level=number(argv[9]);
            const auto output=render_floor(maze,level);
            write_file(dest,output.svg);
            std::cout<<"Wrote "<<dest.string()<<" | "<<stats_label(output.stats)<<'\n';
            return 0;
        }
        usage();
        return argc==1?0:2;
    }catch(const std::exception& e) {
        std::cerr<<"Preview error: "<<e.what()<<'\n';
        return 1;
    }
}
