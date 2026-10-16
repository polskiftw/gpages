// Export exact navigation data from the production elevation-aware engine.
// SVG diagrams and JSON are generated from one seed, not hand-drawn.
import {mkdirSync,writeFileSync} from "node:fs";
import {ElevationMaze,PITCH,STORY,WIDTH,HEADROOM,SLAB,VERSION} from "./stairs.mjs";
import {floorPlanSvg,runProfileSvg} from "./view.mjs";
const output=process.argv[2]||"/tmp/stair-preview";
mkdirSync(output,{recursive:true});
const world=new ElevationMaze({width:28,height:28,levels:6,seed:"LOST-6549",stairWeight:17});
const runs=world.runs(),biggest=runs[0]||null;
for(let z=0;z<world.levels;z++){
  const data=floorPlanSvg(world,z,{highlight:biggest});
  writeFileSync(output+"/floor-"+z+".svg",data.svg);
  console.log("FLOOR",z,JSON.stringify(data.stats));
}
writeFileSync(output+"/longest-continuous-run.svg",runProfileSvg(biggest));
const info={version:VERSION,seed:world.seed,dimensions:[world.width,world.height,world.levels],
  scale:{horizontalStepMeters:PITCH,levelHeightMeters:STORY,widthMeters:WIDTH,headroomMeters:HEADROOM,slabMeters:SLAB},
  start:world.start,exit:world.exit,routeLength:world.routeLength,
  navigableCells:world.usedCount,levelCounts:world.levelCounts,edges:world.edges,stairSegments:world.stairs,
  extraDetours:world.loops,removedDeadEndCells:world.pruned,
  uninterruptedRunsAtLeast3:runs.filter(r=>r.steps>=3).length,
  maxUninterruptedClimb:biggest?.steps||0,example:biggest};
writeFileSync(output+"/stats.json",JSON.stringify(info,null,2)+"\n");
console.log("EXAMPLE",JSON.stringify(info));
