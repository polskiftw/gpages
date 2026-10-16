import assert from "node:assert/strict";
import {ConnectedDetours3D} from "../elevation/combined.mjs";
import {choiceStats,enrichChoices} from "../elevation/choices.mjs";
import {Maze,SOLID} from "../maze-core.mjs";
const COUNT=4;
const seeds=["LOST-6549","A-LOT-OF-CORRIDORS","TANGLED-WAY","LONG-HALLWAY"];
assert.throws(()=>new ConnectedDetours3D({choices:-1}),/Choices/);
assert.throws(()=>new ConnectedDetours3D({choices:4}),/Choices/);
for(const seed of seeds){
  const opts={seed,width:28,height:28,levels:6,stairWeight:2};
  const versions=[0,1,2].map(choices=>new ConnectedDetours3D({...opts,choices}));
  const [old,moderate,many]=versions;
  for(const world of versions){
    const again=new ConnectedDetours3D({...opts,choices:world.choices});
    assert.deepEqual(world.bits,again.bits,"full graph deterministic for each mode");
    assert.deepEqual(world.start,again.start);assert.deepEqual(world.exit,again.exit);
    assert.ok(world.stairs/world.edges<.04,"rare stairs stay rare with more branches");
    assert.equal(world.flatMasks.length,opts.levels);
    assert.ok(world.routeLength>0);
    for(let z=0;z<opts.levels;z++)for(let k=0;k<world.area;k++){
      const legacy=old.flatMasks[z][k],newer=world.flatMasks[z][k];
      assert.equal(legacy&SOLID, newer&SOLID,"never open new solid rooms or close existing passages");
      assert.equal((legacy&newer),legacy,"no original corridor can be deleted");
      assert.equal(newer&0x30,0,"no abstract vertical portals inserted into planar topology");
    }
    for(let z=0;z<opts.levels;z++){
      assert.equal(world.choiceDiagnostics[z].before.leaves,world.choiceDiagnostics[z].after.leaves,
        "no new tiny dead-end spurs or lost terminal paths");
      assert.equal(world.floorMetrics[z].short,old.floorMetrics[z].short,
        "no preexisting short leaf becomes newly short");
    }
  }
  assert.ok(moderate.junctions>old.junctions*1.15,
    "moderate style must materially improve number of meaningful junctions");
  assert.ok(many.junctions>old.junctions*1.45,
    "high choice mode should substantially outperform old sparse layout");
  assert.ok(many.junctions>moderate.junctions*1.15,
    "the two branching settings must be genuinely different");
  const mean=w=>w.choiceDiagnostics.reduce((a,x)=>a+x.after.meanToChoice,0)/w.levels;
  assert.ok(mean(many)<mean(old)*.8,
    "average distance to next real choice should shrink by at least 20%");
  for(let z=0;z<old.levels;z++){
    const base=old.choiceDiagnostics[z].after,rich=many.choiceDiagnostics[z].after;
    assert.ok(rich.p90Run<=base.p90Run,"worst typical choice-free walks cannot increase");
  }
  console.log("PASS DENSER CHOICES",JSON.stringify({seed,baseJunctions:old.junctions,
    moderateJunctions:moderate.junctions,manyJunctions:many.junctions,
    baseMean:Number(mean(old).toFixed(2)),manyMean:Number(mean(many).toFixed(2)),
    added:many.extraChoices,stairPct:many.stairShare}));
}
{
  const m=new ConnectedDetours3D({width:28,height:28,levels:6,seed:"LOST-6549",choices:0,stairWeight:2});
  assert.equal(m.routeLength,100,"original reproducible LOST-6549 labyrinth must remain the same");
  assert.equal(m.edges,3585,"legacy edge count unchanged");
  assert.equal(m.stairs,35,"legacy stairs unchanged");
  assert.equal(m.usedCount,3542,"legacy solid passages unchanged");
}
for(const options of [{width:8,height:8,levels:2,seed:"SMALL"},{width:12,height:14,levels:3,seed:"NARROW"}]){
  for(const choices of [0,1,2]){
    const m=new ConnectedDetours3D({...options,choices});
    assert.ok(m.usedCount>0&&m.routeLength>0);
    assert.ok(m.stairCountByBoundary.every(x=>x>=1));
  }
}
console.log("CHOICE-RICH MAZE REGRESSIONS PASSED: old seeds, rare stairs, connectivity and deep detours preserved");
