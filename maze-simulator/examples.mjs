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
    // Encoded for portability: can recover the exact SVG from CI logs as well.
    console.log("SAMPLE:"+name+":"+gzipSync(Buffer.from(svg)).toString("base64"));
  }
}
