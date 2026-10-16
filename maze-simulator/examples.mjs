// Exact global-v3 mathematical SVG plans for CI and direct inspection.
import {mkdirSync,writeFileSync} from "node:fs";
import {gzipSync} from "node:zlib";
import {Maze,ENGINE_VERSION} from "./maze-core.mjs";
import {floorSvg} from "./maze-map.mjs";
const out=process.argv[2]||"/tmp/maze-samples";
mkdirSync(out,{recursive:true});
const seed="LOST-6549",width=64,height=64,levels=3;
for(const style of ["winding","balanced","branching"]){
  const maze=new Maze({width,height,levels,seed,style});
  for(const z of (style==="balanced"?[0,1,2]:[0])){
    const name=style+"-floor-"+(z+1)+".svg",map=floorSvg(maze,{floor:z,width,height});
    writeFileSync(out+"/"+name,map.svg);
    const plane=maze.cells.subarray(z*width*height,(z+1)*width*height);
    const data=gzipSync(Buffer.from(plane)).toString("base64");
    console.log("METRIC",JSON.stringify({name,version:ENGINE_VERSION,seed,style,floor:z+1,dims:[width,height,levels],start:maze.start,exit:maze.exit,routeLength:maze.routeLength,...map.stats}));
    console.log("PASSAGES:"+name+":"+data);
    console.log("SAMPLE:"+name+":"+gzipSync(Buffer.from(map.svg)).toString("base64"));
  }
}
