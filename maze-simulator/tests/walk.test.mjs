import assert from "node:assert/strict";
import {ConnectedDetours3D} from "../elevation/combined.mjs";
import {PITCH,STORY,WIDTH,HEADROOM,vertical} from "../elevation/stairs.mjs";
import {WALK_RADIUS,halfWidth,hasPad,edgeHeight,initialPose,movePlayer,floorAt,
  distanceToExit,ribbonSample} from "../walk/navigation.mjs";
import {buildMesh,SURFACE} from "../walk/mesh.mjs";
const approx=(a,b,eps=0.1)=>assert.ok(Math.abs(a-b)<=eps,a+" should be near "+b);
let explored=0;
for(const [seed,width,height,levels] of [
  ["LOST-6549",28,28,6],["UNDER-STAIRS",18,20,5],["TURNED-PASSAGES",10,11,3]
]){
  const m=new ConnectedDetours3D({seed,width,height,levels,stairWeight:2});
  const start=initialPose(m),at=floorAt(m,start.x,start.z,start.feet);
  assert.ok(at,"seed entrance must be inside a valid closed passage");
  approx(at.height,m.start.z*STORY,1e-5);
  const full=buildMesh(m,{center:{x:width/2,y:height/2,z:Math.floor(levels/2)},radius:100,floorRadius:100});
  assert.equal(full.stats.edges,m.edges,"one geometric tube per real graph edge");
  assert.equal(full.stats.stairs,m.stairs,"all stairs are sloping tubes, not arbitrary teleporters");
  assert.equal(full.stats.pads+full.stats.through,m.usedCount,"all walkable graph nodes are represented");
  assert.equal(full.vertices.length%27,0,"all surfaces must be real triangles");
  assert.ok(full.stats.caps>0,"blocked compass directions must have real brick caps");
  assert.ok(full.stats.triangles>m.edges*10,"sidewalls, floors and ceilings must exist");
  assert.ok(full.vertices.every(Number.isFinite),"finite mesh coordinates, normals and textures");
  const materialCounts=new Map();
  for(let j=8;j<full.vertices.length;j+=9){
    const mat=full.vertices[j];materialCounts.set(mat,(materialCounts.get(mat)||0)+1);
    assert.ok(Object.values(SURFACE).includes(mat));
  }
  assert.ok(materialCounts.has(SURFACE.BRICK)&&materialCounts.has(SURFACE.STONE));
  assert.ok(materialCounts.has(SURFACE.CEILING)&&materialCounts.has(SURFACE.STAIRS));
  let expectedCaps=0;
  for(let i=0;i<m.volume;i++)if(hasPad(m,i))
    for(let d=0;d<4;d++)if(!m.headingOccupied(i,d))expectedCaps++;
  assert.equal(full.stats.caps,expectedCaps,"every missing exit closes with an actual opaque wall");
  const sample=buildMesh(m,{center:m.start,radius:7,floorRadius:1});
  assert.ok(sample.stats.triangles<full.stats.triangles,
    "render only nearby mesh; no global geometry load for each view");
  assert.deepEqual(m.bits,new ConnectedDetours3D({seed,width,height,levels,stairWeight:2}).bits,
    "building or streaming geometry cannot mutate deterministic maze topology");
  // Try to walk into a blocked compass heading from a real flat landing.
  let blockedChecked=false;
  for(let i=0;i<m.volume&&!blockedChecked;i++){
    if(!hasPad(m,i))continue;
    const p=m.loc(i),start={x:p.x*PITCH,z:p.y*PITCH,feet:p.z*STORY,yaw:0,pitch:0};
    for(const [d,dx,dz] of [[0,0,-1],[1,1,0],[2,0,1],[3,-1,0]]){
      if(m.headingOccupied(i,d))continue;
      const final=movePlayer(m,start,dx*2,dz*2);
      assert.ok(Math.hypot(final.x-start.x,final.z-start.z)<halfWidth,
        "blocked direction must not permit stepping through a brick wall");
      blockedChecked=true;break;
    }
  }
  assert.ok(blockedChecked);
  // Take one complete real rising staircase head-on. The collision solver
  // must reach the upper level continuously instead of teleporting or
  // allowing a player to sidestep the staircase.
  let stairTested=false;
  for(let i=0;i<m.volume&&!stairTested;i++)if(m.used[i])for(const {to} of m.links(i)){
    const a=m.loc(i),b=m.loc(to);
    if(b.z!==a.z+1)continue;
    const dx=b.x-a.x,dz=b.y-a.y;
    let state={x:a.x*PITCH,z:a.y*PITCH,feet:a.z*STORY,yaw:0,pitch:0};
    for(let t=0;t<80;t++)state=movePlayer(m,state,dx*PITCH/80,dz*PITCH/80);
    approx(state.x,b.x*PITCH,.25);approx(state.z,b.y*PITCH,.25);
    approx(state.feet,b.z*STORY,.15);
    const middleX=(a.x+b.x)*PITCH/2,middleZ=(a.y+b.y)*PITCH/2,
      centerY=edgeHeight(m,i,to,.5);
    const side=movePlayer(m,{x:middleX,z:middleZ,feet:centerY},-dz*2,dx*2);
    assert.ok(Math.hypot(side.x-middleX,side.z-middleZ)<halfWidth,
      "sidewall must stop walking around a ramp");
    stairTested=true;break;
  }
  assert.ok(stairTested,"test world contains actual accessible stairs");
  for(const o of m.overpasses().slice(0,12)){
    const x=(o.from.x+o.to.x)*PITCH/2,z=(o.from.y+o.to.y)*PITCH/2;
    // The actual grade can flatten slightly at a real landing. Do not
    // assume that half the horizontal span is half the change in height.
    const elevation=(segment)=>edgeHeight(m,
      m.index(o.from.x,o.from.y,segment.startLevel),
      m.index(o.to.x,o.to.y,segment.endLevel),.5);
    const lower=elevation(o.lower),upper=elevation(o.upper);
    assert.ok(upper-lower>HEADROOM);
    const low=floorAt(m,x,z,lower),high=floorAt(m,x,z,upper);
    assert.ok(low&&high,"separate underpass and overhead staircase must both have walkable surfaces");
    approx(low.height,lower,.01);approx(high.height,upper,.01);
    assert.ok(!floorAt(m,x,z,(lower+upper)/2),
      "player cannot teleport into the void halfway between vertically stacked paths");
  }
  // No square platform geometry at the middle of an uninterrupted climb.
  const pads=Array.from({length:m.volume},(_,i)=>i).filter(i=>hasPad(m,i)).length;
  assert.equal(full.stats.pads,pads);
  const initialDistance=distanceToExit(m,start);
  assert.ok(initialDistance>0);
  explored+=m.usedCount;
  console.log("PASS WALKABLE 3D",seed,
    "nodes",m.usedCount,"edges",m.edges,"stairs",m.stairs,
    "brick caps",full.stats.caps,"triangles",full.stats.triangles,
    "local triangles",sample.stats.triangles);
}
console.log("ALL GRAPH-DERIVED MESH/COLLISION TESTS PASSED",explored,"navigable positions");
