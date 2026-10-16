// Reproducible stair-density tuning using exactly the production math engine.
import {ElevationMaze} from "./stairs.mjs";
for(const stairWeight of [2,3,4,5,8,12,17,24]){
  const m=new ElevationMaze({width:28,height:28,levels:6,seed:"LOST-6549",stairWeight});
  const runs=m.runs(),n=m.edges-m.pruned;
  console.log("DENSITY",JSON.stringify({stairWeight,navigable:m.usedCount,
    connections:n,stairSegments:m.stairs,stairPercent:Math.round(100*m.stairs/n),
    runsAtLeast3:runs.filter(r=>r.steps>=3).length,longest:runs[0]?.steps||0,
    junctions:Array.from(m.used).reduce((sum,on,i)=>sum+(on&&m.links(i).length>=3?1:0),0),
    path:m.routeLength}));
}
