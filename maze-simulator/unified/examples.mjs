// Export exact floor plans, naturally continuous stair profile and metrics.
// Images are derived ONLY from ConnectedDetours3D's actual 3D navigation graph.
import {mkdirSync,writeFileSync} from "node:fs";
import {ConnectedDetours3D,COMBINED_VERSION} from "../elevation/combined.mjs";
import {floorPlanSvg,runProfileSvg} from "../elevation/view.mjs";
const dir=process.argv[2]||"/tmp/combined-maze";
mkdirSync(dir,{recursive:true});
const maze=new ConnectedDetours3D({width:28,height:28,levels:6,stairWeight:2,seed:"LOST-6549"});
const runs=maze.runs(),long=runs.find(r=>r.steps>=3)||runs[0]||null;
for(let z=0;z<maze.levels;z++){
  const map=floorPlanSvg(maze,z,{highlight:long});
  writeFileSync(dir+"/floor-"+z+".svg",map.svg);
  console.log("FLOOR",z,JSON.stringify(map.stats));
}
writeFileSync(dir+"/continuous-flight.svg",runProfileSvg(long));
const stats={version:COMBINED_VERSION,seed:maze.seed,dimensions:[maze.width,maze.height,maze.levels],
  navigable:maze.usedCount,edges:maze.edges,stairs:maze.stairs,stairPercentage:maze.stairShare,
  start:maze.start,exit:maze.exit,shortestPath:maze.routeLength,
  floorDeadEnds:maze.floorMetrics,protectedThroughCells:maze.flightInterior.reduce((s,x)=>s+x,0),
  multistoryRuns:runs.filter(r=>r.steps>=3).length,longestStair:maze.longest,
  stairBudgetPerFloorPair:maze.maxRampsPerBoundary,
  stairCountPerFloorPair:[...maze.stairCountByBoundary]};
writeFileSync(dir+"/stats.json",JSON.stringify(stats,null,2)+"\n");
console.log("COMBINED",JSON.stringify(stats));
