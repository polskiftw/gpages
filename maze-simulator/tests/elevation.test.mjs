import assert from "node:assert/strict";
import {ElevationMaze,PITCH,STORY,WIDTH,HEADROOM,SLAB,VERSION,popcount,vertical,direction,opposite} from "../elevation/stairs.mjs";
import {floorPlanSvg,runProfileSvg} from "../elevation/view.mjs";
assert.ok(STORY-(STORY/PITCH)*WIDTH>HEADROOM+SLAB,"Stacked perpendicular stair ribbons need corner clearance");
const fixtures=[{width:24,height:24,levels:6,seed:"LOST-6549"},{width:30,height:24,levels:5,seed:"CONTINUOUS-RISE"},{width:18,height:27,levels:7,seed:"NO-PREFABS"}];
let longestSeen=0,ascending=0,descending=0,mixedLandings=0,flatAfterLongRun=0;
for(const opts of fixtures){
  const m=new ElevationMaze(opts),second=new ElevationMaze(opts);
  assert.equal(m.bits.length,m.volume);
  assert.ok(m.stairs/m.edges<.035,"rare default must stay under 3.5% stair segments");
  for(const used of m.stairCountByBoundary)assert.ok(used<=m.maxRampsPerBoundary,"floor-pair staircase budget must be respected");
  assert.deepEqual(m.bits,second.bits,"seed should reproduce every connection");
  assert.deepEqual(m.used,second.used);
  assert.deepEqual(m.start,second.start);
  assert.deepEqual(m.exit,second.exit);
  assert.equal(m.routeLength,second.routeLength);
  assert.ok(m.levelCounts.every(n=>n>0),"all requested floors occupied");
  assert.ok(m.usedCount>m.volume*.2,"should fill a meaningful fraction of world");
  assert.ok(m.routeLength>8,"exit should take meaningful exploration");
  assert.notEqual(m.exit.z,m.start.z,"exit on a different level");
  const seen=new Uint8Array(m.volume),queue=[m.index(m.start.x,m.start.y,m.start.z)];
  seen[queue[0]]=1;
  for(let qi=0;qi<queue.length;qi++){
    const i=queue[qi];
    for(const {to,code} of m.links(i)){
      assert.ok(m.used[to],"all corridors lead to walkable cells");
      assert.ok(m.bits[to]&(1<<opposite(code)),"reciprocal opening");
      const a=m.loc(i),b=m.loc(to);
      assert.equal(Math.abs(a.x-b.x)+Math.abs(a.y-b.y),1,"no teleporting, diagonal XY, or vertical shafts");
      assert.equal(b.z-a.z,vertical(code),"actual grade is encoded in graph");
      assert.ok(Math.abs(b.z-a.z)<=1,"each step climbs at most one floor");
      if(vertical(code)>0)ascending++;
      if(vertical(code)<0)descending++;
      if(!seen[to]){seen[to]=1;queue.push(to);}
    }
  }
  assert.equal(queue.length,m.usedCount,"all navigable cells connected");
  for(let i=0;i<m.volume;i++)if(!m.used[i])assert.equal(m.bits[i],0,"solid cells must have no passages");
  const edges=m.geometry();
  assert.equal(edges.length,m.edges-m.pruned,"geometry derived only from actual stored connections");
  for(const edge of edges){
    assert.equal(Math.abs(edge.a[0]-edge.b[0])+Math.abs(edge.a[1]-edge.b[1]),PITCH);
    assert.ok(Math.abs(edge.riseMeters)===STORY||edge.riseMeters===0);
    assert.equal(edge.widthMeters,WIDTH);
    assert.equal(edge.headroomMeters,HEADROOM);
  }
  // Validate every overlapping XY passage reservation has sufficient
  // headroom for the entire segment, not only at one end.
  for(const records of m.reservations.values())for(let a=0;a<records.length;a++)for(let b=a+1;b<records.length;b++){
    const d0=(records[a].z0-records[b].z0)*STORY,d1=(records[a].z1-records[b].z1)*STORY;
    assert.ok(d0*d1>0&&Math.min(Math.abs(d0),Math.abs(d1))>=HEADROOM+SLAB,"occupied ribbons collide");
  }
  const runs=m.runs();
  const map=floorPlanSvg(m,1);
  assert.ok(map.svg.includes('class="rise-marker"'),"compact stair glyphs should be drawn from real graph edges");
  const labeled=floorPlanSvg(m,1,{showLabels:true});
  assert.ok(labeled.svg.includes('class="rise"'),"accessible exact U/D labels should be available");
  assert.notEqual(map.svg,labeled.svg);
  if(runs.length)assert.ok(runProfileSvg(runs[0]).includes("m vertical change"),"real side profiles derive from stair runs");

  for(let i=0;i<m.volume;i++)if(m.used[i]){
    const links=m.links(i);
    if(links.length<3)continue;
    const up=links.filter(x=>vertical(x.code)>0),down=links.filter(x=>vertical(x.code)<0);
    if(up.some(a=>down.some(b=>direction(a.code)!==direction(b.code))))mixedLandings++;
  }
  for(const run of runs)if(run.steps>=3){
    const tail=run.vertices.at(-1),i=m.index(tail.x,tail.y,tail.z);
    if(m.links(i).some(e=>vertical(e.code)===0))flatAfterLongRun++;
  }

  for(const run of runs){
    assert.equal(run.vertices.length,run.steps+1);
    assert.equal(Math.abs(run.rise),run.steps,"a continuous stair run climbs without mandatory intermediate landings");
    for(let k=1;k<run.vertices.length;k++){
      const a=run.vertices[k-1],b=run.vertices[k];
      assert.equal(Math.abs(a.x-b.x)+Math.abs(a.y-b.y),1);
      assert.equal(Math.abs(a.z-b.z),1);
      if(k>1){
        const prev=run.vertices[k-2];
        assert.deepEqual([b.x-a.x,b.y-a.y,b.z-a.z],[a.x-prev.x,a.y-prev.y,a.z-prev.z],
          "a single flight is a straight continuous series of stair edges");
      }
    }
    if(run.steps>1)for(let k=1;k<run.vertices.length-1;k++)
      assert.ok(m.through(m.index(run.vertices[k].x,run.vertices[k].y,run.vertices[k].z)),
        "stair interiors do not require a landing at floor boundaries");
  }
  longestSeen=Math.max(longestSeen,runs[0]?.steps||0);
  console.log("PASS",opts.seed,"cells",m.usedCount,"levels",m.levelCounts.join("/"),
    "stairEdges",m.stairs,"landings",m.usedCount-runs.reduce((s,r)=>s+Math.max(0,r.steps-1),0),
    "longest continuous climb",runs[0]?.steps||0,"exit steps",m.routeLength);
}
// The original reported bug: even the "minimum" should be mostly level paths.
for(const density of [1,2,4,8]){
  for(const [w,h,levels,seed] of [[6,6,2,"TINY"],[10,8,5,"NARROW"],[28,28,6,"LOST-6549"],[32,29,7,"RANDOM-WORLD"]]){
    const m=new ElevationMaze({width:w,height:h,levels,seed,stairWeight:density});
    assert.ok(m.levelCounts.every(c=>c>0),"all story levels must be reached at every density");
    assert.ok(m.stairCountByBoundary.every(c=>c>0&&c<=m.maxRampsPerBoundary),
      "every neighboring floor pair must connect, never exceeding its hard cap");
    const proportion=m.stairs/(m.edges-m.pruned);
    if(density===1&&w>10)assert.ok(proportion<.02,
      "minimum density must be under 2% actual stairs, not the old 26%");
    if(density===2&&w>10)assert.ok(proportion<.035,
      "default stair proportion must stay comfortably below 3.5%");
    if(w>10)assert.ok(m.usedCount/m.volume>.75,
      "rare-stair maze should not sacrifice most of its level corridors");
    console.log("RARE",density,w+"x"+h+"x"+levels,seed,
      "stair-edges",m.stairs,"proportion",proportion.toFixed(4),
      "longest-flight",m.runs()[0]?.steps||0);
  }
}
assert.ok(ascending>0&&descending>0,"up and down stairs are reciprocal");
assert.ok(longestSeen>=3,"rare seeded ramps can still continue across three floors");
// With genuinely rare stairs, an up/down split is possible but not mandatory for every sampled world.
assert.ok(flatAfterLongRun>0,"an uninterrupted multistory flight should reach a flat landing");
console.log("Mixed-height branching landings:",mixedLandings,"long flights ending at level routes:",flatAfterLongRun);
assert.equal(VERSION,"elevation-lattice-2");
assert.throws(()=>new ElevationMaze({width:1,height:8,levels:3}),/width/);
assert.throws(()=>new ElevationMaze({width:12,height:12,levels:0}),/levels/);
console.log("Elevation math validated: no prefab turns, no mandatory floor landings, no impossible graph moves.");
