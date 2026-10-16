// These are exact floor-plan exports of the playable topology, not concept art.
import {mkdirSync,writeFileSync} from "node:fs";
import {ConnectedDetours3D,COMBINED_VERSION} from "./combined.mjs";
import {floorPlanSvg} from "./view.mjs";
const dir=process.argv[2]||"/tmp/maze-junction-previews";mkdirSync(dir,{recursive:true});
const seed="LOST-6549",worlds=[];
for(const choices of [0,1,2,3]){
  const m=new ConnectedDetours3D({seed,width:28,height:28,levels:6,stairWeight:2,choices});
  for(const floor of [0,2,4]){
    const {svg,stats}=floorPlanSvg(m,floor);
    writeFileSync(dir+"/choices-"+choices+"-floor-"+floor+".svg",svg);
    console.log("FLOOR CHOICES",choices,floor,JSON.stringify(stats));
  }
  worlds.push({version:COMBINED_VERSION,seed,choices,dimensions:[m.width,m.height,m.levels],
    start:m.start,exit:m.exit,junctions:m.junctions,extraDetours:m.extraChoices,
    gapBridges:m.bridges,openedSolidCells:m.carvedCells,
    floors:m.choiceDiagnostics,walkable:m.usedCount,stairPct:m.stairShare,
    shortestRoute:m.routeLength});
}
writeFileSync(dir+"/comparisons.json",JSON.stringify(worlds,null,2)+"\n");
console.log("CHOICE PREVIEW DATA",JSON.stringify(worlds.map(x=>({choices:x.choices,junctions:x.junctions,
  detours:x.extraDetours,gapBridges:x.gapBridges,openedSolidCells:x.openedSolidCells,stairPct:x.stairPct}))));
