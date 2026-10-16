import assert from "node:assert/strict";
import {ElevationMaze,PITCH,STORY,WIDTH,HEADROOM,SLAB,opposite,vertical} from "../elevation/stairs.mjs";
import {ConnectedDetours3D} from "../elevation/combined.mjs";

// All paths are one enclosed width × headroom tube; XY overlap is legal
// where the upper floor slab clears the lower passage's headroom.
const node=(m,x,y,z)=>m.index(x,y,z);
{
  const m=new ElevationMaze({width:8,height:8,levels:4,generate:false});
  const loA=node(m,2,2,0),loB=node(m,3,2,0);
  const hiA=node(m,2,2,1),hiB=node(m,3,2,2);
  m.join(loA,loB,1);m.join(hiA,hiB,5);
  m.used[loA]=m.used[loB]=m.used[hiA]=m.used[hiB]=1;
  assert.equal(m.overpasses().length,1,"a hallway and staircase can share XY without meeting");
  const crossing=m.overpasses()[0];
  assert.deepEqual(crossing.from,{x:2,y:2});
  assert.deepEqual(crossing.to,{x:3,y:2});
  assert.equal(crossing.lower.kind,"flat");
  assert.equal(crossing.upper.kind,"stairs");
  assert.equal(crossing.connected,false,"separate enclosed corridors never join automatically");
  assert.ok(crossing.minimumHeadroomMeters>=HEADROOM,"the corridor below can be traversed");
  assert.equal(crossing.minimumHeadroomMeters,STORY-SLAB);
  assert.deepEqual(m.links(loA).map(e=>e.to),[loB],"lower corridor cannot climb into overhead stair");
  assert.deepEqual(m.links(hiA).map(e=>e.to),[hiB],"upper staircase cannot drop into lower corridor");
  assert.ok(m.geometry().every(edge=>edge.enclosure==="walls-floor-ceiling"));
  assert.throws(()=>m.join(loA,hiB,5),/Only one enclosed passage|collision/,
    "can't offer level and ascending choices in the exact same compass direction");
}
{
  const m=new ElevationMaze({width:8,height:8,levels:3,generate:false});
  const a=node(m,2,2,0),flat=node(m,3,2,0),up=node(m,3,2,1);
  m.join(a,flat,1);
  assert.throws(()=>m.join(a,up,5),/Only one enclosed passage/,
    "a stair cannot be walk-around furniture inside an existing level corridor");
  assert.equal(m.overpasses().length,0);
}
{
  // A staircase also cannot share the same *physical* swept volume with
  // a flat passage when they meet at a grade, even with unique headings.
  const m=new ElevationMaze({width:8,height:8,levels:3,generate:false});
  const a=node(m,2,2,0),b=node(m,3,2,0),c=node(m,2,2,1),d=node(m,3,2,1);
  m.join(a,b,1);
  assert.equal(m.clear(c,d),true,"a different-level corridor can sit directly above");
  assert.equal(m.clear(a,d),false,"a climbing ramp overlaps the level floor at its low end");
}
assert.ok(STORY-(STORY/PITCH)*WIDTH>=HEADROOM+SLAB,
  "orthogonal 1.5m corridor ribbons sharing a projected grid vertex have safe upper/lower separation");

// Make it impossible for an enclosed junction to offer BOTH a level
// and stair route in the same compass heading. No aesthetic bypasses.
const headingCount=(mask,d)=>Number(!!(mask&(1<<d)))+Number(!!(mask&(1<<(d+4))))+Number(!!(mask&(1<<(d+8))));
let overpassWorlds=0,crossings=0;
for(const seed of ["LOST-6549","UNDER-THE-STAIRS","HIDDEN-BELOW","VERTICAL-DETOURS"]){
  for(const [width,height,levels] of [[14,14,4],[28,28,6],[36,29,7]]){
    const m=new ConnectedDetours3D({width,height,levels,stairWeight:2,seed});
    for(let i=0;i<m.volume;i++)if(m.used[i])for(let d=0;d<4;d++)
      assert.ok(headingCount(m.bits[i],d)<=1,
        "one and only one possible passage (flat/up/down) in any heading");
    for(const x of m.overpasses()){
      assert.equal(x.connected,false);
      assert.ok(x.minimumHeadroomMeters>=HEADROOM);
      assert.ok(x.lower.kind==="flat"&&x.upper.kind==="stairs"||
        x.lower.kind==="stairs"&&x.upper.kind==="flat");
      crossings++;
    }
    assert.equal(m.overpasses().length,m.overpassCount);
    assert.ok(m.geometry().every(e=>e.enclosure==="walls-floor-ceiling"&&e.widthMeters===WIDTH&&e.headroomMeters===HEADROOM));
    console.log("PASS ENCLOSED",seed,width+"x"+height+"x"+levels,
      "stairs",m.stairs,"safe overpasses",m.overpassCount);
    overpassWorlds++;
  }
}
console.log("ENCLOSED PASSAGES AND VERTICAL UNDERPASSES VERIFIED",overpassWorlds,"worlds",crossings,"underpass crossings");
