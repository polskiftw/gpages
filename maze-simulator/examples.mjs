// Exact mathematical sample SVGs for offline inspection and GitHub Actions.
import {mkdirSync,writeFileSync} from "node:fs";
import {gzipSync} from "node:zlib";
import {Maze} from "./maze-core.mjs";
import {floorSvg} from "./maze-map.mjs";
const destination=process.argv[2]||"/tmp/maze-samples";
mkdirSync(destination,{recursive:true});
const seed="LOST-6549",width=40,height=40,levels=3;
for(const style of ["winding","balanced","branching"]){
  const maze=new Maze({width,height,levels,seed,style});
  const floors=style==="balanced"?[0,1,2]:[0];
  for(const z of floors){
    const name=style+"-floor-"+(z+1)+".svg";
    const {svg,stats}=floorSvg(maze,{floor:z,width,height});
    writeFileSync(destination+"/"+name,svg);
    console.log("METRIC",name,JSON.stringify(stats));
    const passages=new Uint8Array(width*height);
    for(let y=0;y<height;y++)for(let x=0;x<width;x++)passages[y*width+x]=maze.passages(x,y,z);
    console.log("PASSAGES:"+name+":"+gzipSync(Buffer.from(passages)).toString("base64"));
    // Encoded for portability: can recover the exact SVG from CI logs as well.
    console.log("SAMPLE:"+name+":"+gzipSync(Buffer.from(svg)).toString("base64"));
  }
}
