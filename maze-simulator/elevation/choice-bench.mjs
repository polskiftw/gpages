// Repeatable decision-density benchmarks. Actual merged passage graphs only.
import {ConnectedDetours3D} from "./combined.mjs";
for(const seed of ["LOST-6549","A-LOT-OF-CORRIDORS","TANGLED-WAY","LONG-HALLWAY"]){
  for(const choices of [0,1,2,3]){
    const world=new ConnectedDetours3D({seed,width:28,height:28,levels:6,stairWeight:2,choices});
    const m=world.choiceDiagnostics;
    const total=m.reduce((a,x)=>a+x.after.junctions,0);
    const mean=m.reduce((a,x)=>a+x.after.meanToChoice,0)/m.length;
    const old=m.reduce((a,x)=>a+x.before.junctions,0);
    const oldMean=m.reduce((a,x)=>a+x.before.meanToChoice,0)/m.length;
    console.log("CHOICE-DENSITY",JSON.stringify({seed,choices,walkable:world.usedCount,
      oldJunctions:old,junctions:total,extraEdges:world.extraChoices,
      gapBridges:world.bridges,carvedSolidCells:world.carvedCells,
      oldMeanToChoice:Number(oldMean.toFixed(2)),meanToChoice:Number(mean.toFixed(2)),
      oldP90:m.map(x=>x.before.p90ToChoice),p90:m.map(x=>x.after.p90ToChoice),
      oldP90Run:m.map(x=>x.before.p90Run),p90Run:m.map(x=>x.after.p90Run),
      oldLongest:m.map(x=>x.before.longestRun),longest:m.map(x=>x.after.longestRun),
      oldLeaf:m.map(x=>x.before.leaves),leaves:m.map(x=>x.after.leaves),
      floorAdded:m.map(x=>x.added),stairPct:world.stairShare,walkDistance:world.routeLength}));
  }
}
