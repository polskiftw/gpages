import assert from "node:assert/strict";
import {ConnectedDetours3D} from "../elevation/combined.mjs";
import {N,E,S,W,SOLID} from "../maze-core.mjs";
import {PITCH,STORY} from "../elevation/stairs.mjs";
import {movePlayer} from "../walk/navigation.mjs";
const BITS=[N,E,S,W],DX=[0,1,0,-1],DY=[-1,0,1,0];
function degree(b){return Number(!!(b&N))+Number(!!(b&E))+Number(!!(b&S))+Number(!!(b&W));}
function next(w,h,i,d){
  const x=i%w,y=Math.floor(i/w),xx=x+DX[d],yy=y+DY[d];
  return xx<0||yy<0||xx>=w||yy>=h?-1:yy*w+xx;
}
function neighbors(cells,w,h,i){
  const result=[];for(let d=0;d<4;d++)if(cells[i]&BITS[d]){
    const j=next(w,h,i,d);assert.ok(j>=0,"all openings remain within the floor");result.push(j);
  }return result;
}
function legacyDistance(cells,w,h,start,goal){
  const dist=new Int32Array(cells.length);dist.fill(-1);
  const q=new Int32Array(cells.length);let read=0,write=1;q[0]=start;dist[start]=0;
  while(read<write){
    const i=q[read++];if(i===goal)return dist[i];
    for(const j of neighbors(cells,w,h,i))if(dist[j]===-1){
      dist[j]=dist[i]+1;q[write++]=j;
    }
  }return -1;
}
function examineGapBridges(old,newer,w,h){
  const marked=new Uint8Array(old.length),isNew=i=>(old[i]&SOLID)&&!(newer[i]&SOLID);
  let opened=0,bridgeCount=0,example=null;
  for(let i=0;i<old.length;i++){
    if(!isNew(i))continue;
    opened++;
    assert.ok(newer[i]===(N|S)||newer[i]===(E|W),
      "every newly carved solid cell is a continuous sealed straight passage, not a dead end or room");
  }
  for(let i=0;i<old.length;i++){
    if(!isNew(i)||marked[i])continue;
    const q=[i],ends=new Set();marked[i]=1;
    for(let k=0;k<q.length;k++){
      const at=q[k];
      for(const j of neighbors(newer,w,h,at)){
        if(isNew(j)){if(!marked[j]){marked[j]=1;q.push(j);}}
        else {assert.ok(!(old[j]&SOLID),"every bridge connects to original walkable corridors");ends.add(j);}
      }
    }
    assert.ok(q.length>=1&&q.length<=4,"connector carves one to four former masonry cells");
    assert.equal(ends.size,2,"each connector has two real original-corridor entrances, not a dangling spur");
    const [a,b]=[...ends];
    if(!example)example={a,b,length:q.length};
    assert.ok(degree(old[a])===2&&degree(old[b])===2,"both ends attached at old linear corridors");
    assert.ok(degree(newer[a])===3&&degree(newer[b])===3,"both ends become genuine decision junctions");
    const distance=legacyDistance(old,w,h,a,b);
    assert.ok(distance>=14,"connector offers a meaningful existing detour rather than a trivial little loop: "+distance);
    bridgeCount++;
  }
  return {bridgeCount,opened,example};
}
const seeds=["LOST-6549","A-LOT-OF-CORRIDORS","TANGLED-WAY","LONG-HALLWAY"];
let checked=0,totalBridge=0,totalCarved=0;
for(const seed of seeds){
  const o={seed,width:28,height:28,levels:6,stairWeight:2};
  const legacy=new ConnectedDetours3D({...o,choices:0});
  const old=new ConnectedDetours3D({...o,choices:2});
  const upgraded=new ConnectedDetours3D({...o,choices:3});
  const rerun=new ConnectedDetours3D({...o,choices:3});
  assert.deepEqual(upgraded.bits,rerun.bits,"new bridges are exactly seed-reproducible");
  assert.deepEqual(upgraded.start,rerun.start);
  assert.deepEqual(upgraded.exit,rerun.exit);
  assert.ok(upgraded.junctions>old.junctions*1.10,
    "new gap crossings must add at least 10% more meaningful junctions on every seed");
  const mean=m=>m.choiceDiagnostics.reduce((sum,x)=>sum+x.after.meanToChoice,0)/m.levels;
  assert.ok(mean(upgraded)<=mean(old)*.92,
    "connectors must shorten the distance between decisions across every test seed");
  assert.ok(upgraded.bridges>=25,"actual solid-space connectors must occur throughout a 6-floor labyrinth");
  assert.ok(upgraded.carvedCells>=upgraded.bridges,"each bridge opens at least one solid cell");
  assert.ok(upgraded.stairs/upgraded.edges<.05,"stairs remain sparse");
  assert.equal(upgraded.levelCounts.length,upgraded.levels);
  let bridges=0,opened=0,walkedBridge=false;
  for(let z=0;z<o.levels;z++){
    const original=legacy.flatMasks[z],rich=upgraded.flatMasks[z];
    for(let i=0;i<original.length;i++)if(!(original[i]&SOLID)){
      assert.ok(!(rich[i]&SOLID),"original corridors never turn solid");
      assert.equal(original[i]&rich[i],original[i],"original corridor links are never deleted");
    }
    const result=examineGapBridges(original,rich,o.width,o.height);
    bridges+=result.bridgeCount;opened+=result.opened;
    if(!walkedBridge&&result.example){
      const {a,b,length}=result.example,ax=a%o.width,ay=Math.floor(a/o.width),
        bx=b%o.width,by=Math.floor(b/o.width);
      const dx=Math.sign(bx-ax),dy=Math.sign(by-ay);
      assert.equal(Math.abs(bx-ax)+Math.abs(by-ay),length+1,
        "every new corridor spans its entire former solid gap straight through");
      let player={x:ax*PITCH,z:ay*PITCH,feet:z*STORY,yaw:0,pitch:0};
      for(let edge=0;edge<length+1;edge++)for(let s=0;s<70;s++)
        player=movePlayer(upgraded,player,dx*PITCH/70,dy*PITCH/70);
      assert.ok(Math.hypot(player.x-bx*PITCH,player.z-by*PITCH)<.3,
        "actual first-person collision must permit traversing both entrances of a carved connector");
      assert.ok(Math.abs(player.feet-z*STORY)<.001,"flat connector never teleports between floors");
      walkedBridge=true;
    }
    assert.equal(result.bridgeCount,upgraded.choiceDiagnostics[z].bridges);
    assert.equal(result.opened,upgraded.choiceDiagnostics[z].carved);
    assert.equal(upgraded.floorMetrics[z].short,old.floorMetrics[z].short,
      "no old medium branch is truncated into a short dead end");
    assert.equal(upgraded.choiceDiagnostics[z].after.leaves,old.choiceDiagnostics[z].after.leaves,
      "none of the new connectors creates or removes a terminal dead-end");
    assert.ok(upgraded.choiceDiagnostics[z].after.p90Run<=old.choiceDiagnostics[z].after.p90Run,
      "90th-percentile walks without junctions must not get longer");
    checked++;
  }
  assert.ok(walkedBridge,"each full seed must contain a physically walkable cross-gap route");
  assert.equal(bridges,upgraded.bridges);
  assert.equal(opened,upgraded.carvedCells);
  totalBridge+=bridges;totalCarved+=opened;
  console.log("PASS CROSS-GAP BRIDGES",JSON.stringify({seed,bridges,opened,
    originalJunctions:old.junctions,newJunctions:upgraded.junctions,
    originalMean:Number(mean(old).toFixed(2)),newMean:Number(mean(upgraded).toFixed(2)),
    stairShare:upgraded.stairShare}));
}
for(const opts of [{width:6,height:6,levels:2,seed:"SMALL"},
  {width:12,height:14,levels:3,seed:"SMALLER"}]){
  for(const choices of [0,1,2,3]){
    const m=new ConnectedDetours3D({...opts,choices,stairWeight:2});
    assert.ok(m.routeLength>0&&m.levelCounts.every(x=>x>0));
    assert.ok(m.stairCountByBoundary.every(x=>x>0),"all floors remain connected by a real slope");
  }
}
console.log("ALL GAP CONNECTOR REGRESSIONS PASSED",checked,"floors",totalBridge,
  "meaningful new connectors",totalCarved,"former solid cells opened");
