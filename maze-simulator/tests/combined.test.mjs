import assert from "node:assert/strict";
import {ConnectedDetours3D,COMBINED_VERSION} from "../elevation/combined.mjs";
import {ElevationMaze,vertical,opposite,STORY,HEADROOM,SLAB} from "../elevation/stairs.mjs";
import {SOLID,N,E,S,W} from "../maze-core.mjs";
import {floorPlanSvg,runProfileSvg} from "../elevation/view.mjs";
const popcount=x=>{let n=0;for(;x;x&=x-1)n++;return n;};
let longCount=0,worlds=0;
for(const seed of ["LOST-6549","NO-PREFABS","CONNECTED-DETOURS","A-REAL-LOST-PLACE"]){
  for(const [width,height,levels] of [[6,6,2],[12,14,3],[28,28,6],[36,29,7]]){
    const stairWeight=2,opts={width,height,levels,stairWeight,seed};
    const a=new ConnectedDetours3D(opts),b=new ConnectedDetours3D(opts);
    assert.equal(a.version,COMBINED_VERSION);
    assert.deepEqual(a.bits,b.bits,"seed reproduces horizontal/vertical passages");
    assert.deepEqual(a.used,b.used,"seed reproduces solid mass");
    assert.deepEqual(a.start,b.start);assert.deepEqual(a.exit,b.exit);
    assert.deepEqual(a.flatMasks,b.flatMasks);
    assert.equal(a.routeLength,b.routeLength);
    assert.equal(a.floorMetrics.length,levels);
    assert.ok(a.stairs>0,"at least one real stair exists");
    assert.ok(a.stairs/a.edges<.04,"rare stairs remain a small share of total travel");
    assert.ok(a.stairCountByBoundary.every((n,z)=>n>=1&&n<=a.maxRampsPerBoundary),
      "every floor boundary has a physical connection within the hard cap");
    // Preserve precisely the existing, user-approved Connected Detours graph
    // on each floor: not one horizontal wall may change during integration.
    for(let z=0;z<levels;z++)for(let y=0;y<height;y++)for(let x=0;x<width;x++){
      const p=a.flatMasks[z][y*width+x],i=a.index(x,y,z);
      assert.equal(a.bits[i]&15,p&15,"all original level corridors preserved byte-for-byte");
      if(p&SOLID&&!a.flightInterior[i])assert.ok(!a.used[i],"old masonry remains solid");
      if(a.flightInterior[i]){
        assert.ok(p&SOLID,"uninterrupted through-stair must occupy originally solid masonry");
        assert.ok(a.through(i),"through-flight cannot acquire a compulsory landing or branch");
        assert.equal(popcount(a.bits[i]),2);
      }
    }
    // No magical vertical shaft and no impossible diagonal jumps.
    const walk=new Uint8Array(a.volume),queue=[a.index(a.start.x,a.start.y,a.start.z)];
    walk[queue[0]]=1;
    let edges=0,stairs=0;
    for(let qi=0;qi<queue.length;qi++){
      const i=queue[qi],from=a.loc(i);
      for(const {to,code} of a.links(i)){
        assert.ok(a.used[to],"passages terminate at walkable graph nodes");
        assert.ok(a.bits[to]&(1<<opposite(code)),"connections are reciprocal");
        const end=a.loc(to);
        assert.equal(Math.abs(end.x-from.x)+Math.abs(end.y-from.y),1,"one horizontal cell per movement");
        assert.equal(end.z-from.z,vertical(code),"movement grade matches edge");
        assert.ok(Math.abs(end.z-from.z)<=1,"never skip a whole story");
        if(i<to){edges++;if(vertical(code))stairs++;}
        if(!walk[to]){walk[to]=1;queue.push(to);}
      }
    }
    assert.equal(queue.length,a.usedCount,"all walkable cells across all floors connected");
    assert.equal(edges,a.edges);
    assert.equal(stairs,a.stairs);
    assert.ok(a.routeLength>0&&a.exit.z!==a.start.z,"distant exit on another floor");
    for(const records of a.reservations.values())for(let k=0;k<records.length;k++)
      for(let j=k+1;j<records.length;j++){
        const d0=(records[k].z0-records[j].z0)*STORY,d1=(records[k].z1-records[j].z1)*STORY;
        assert.ok(d0*d1>0&&Math.min(Math.abs(d0),Math.abs(d1))>=HEADROOM+SLAB,
          "all co-projected ribbons have enough vertical separation");
      }
    const sample=floorPlanSvg(a,Math.min(levels-1,1));
    assert.ok(sample.svg.includes('<svg')&&sample.stats.flats>0);
    for(const run of a.runs())if(run.steps>=3){
      longCount++;
      for(let i=1;i<run.vertices.length-1;i++){
        const p=run.vertices[i];assert.ok(a.flightInterior[a.index(p.x,p.y,p.z)],
          "three-floor uninterrupted stair run must have real protected interiors");
      }
      assert.ok(runProfileSvg(run).includes("m vertical change"));
    }
    for(const z of [0,1,Math.floor(levels/2)])if(z<levels){
      let flatEdges=0;
      for(let k=0;k<a.area;k++)flatEdges+=popcount(a.flatMasks[z][k]&15);
      assert.ok(flatEdges>20,"a floor must retain a deep level-corridor graph");
    }
    console.log("PASS INTEGRATED",seed,width+"x"+height+"x"+levels,
      "flat-median",a.floorMetrics.map(x=>x.median).join("/"),
      "stairs",a.stairs,"total",a.edges,"stair%",a.stairShare,
      "uninterrupted runs",a.runs().filter(r=>r.steps>=3).length);
    worlds++;
  }
}
assert.ok(longCount>0,"at least one randomly discovered three-story stair run must occur");
// Varying the user-facing stair setting must never break floor connectivity,
// exceed per-boundary limits or alter the original horizontal maze style.
for(const stairWeight of [1,4,8]){
  for(const [width,height,levels] of [[6,6,2],[16,18,4],[28,28,6]]){
    const m=new ConnectedDetours3D({width,height,levels,seed:"DENSITY-EDGE-CASES",stairWeight});
    assert.ok(m.stairCountByBoundary.every(x=>x>0&&x<=m.maxRampsPerBoundary));
    assert.equal(m.levelCounts.length,levels);
    assert.ok(m.stairShare<6,"stairs must remain less prevalent than level corridors");
    assert.ok(m.floorMetrics.every(x=>x.median>0),"each floor retains meaningful level branches");
    for(let z=0;z<levels;z++)for(let k=0;k<m.area;k++)
      assert.equal(m.bits[z*m.area+k]&15,m.flatMasks[z][k]&15);
    console.log("PASS STAIR DENSITY",stairWeight,width+"x"+height+"x"+levels,
      "stairs",m.stairs,"edges",m.edges,"percent",m.stairShare);
  }
}
assert.equal(COMBINED_VERSION,"detours-elevation-v3");
assert.throws(()=>new ConnectedDetours3D({width:2,height:16,levels:3}),/width/);
console.log("ALL INTEGRATED TOPOLOGY CHECKS PASSED",worlds,"worlds;",longCount,"naturally continuous flights");
