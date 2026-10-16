// Connected Detours + physically directed, sparse, no-template multistory stairs.
// Preserve the proven v4 *flat corridor graph* on each entire floor. Only
// additional seeded Euclidean ramp edges join independently connected floors.
// The renderer has no say in the mathematics. No chunk-aligned portals.
import {Maze,SOLID,E,S} from "../maze-core.mjs";
import {ElevationMaze,vertical} from "./stairs.mjs";
export const COMBINED_VERSION="detours-elevation-v1";
function seededRandom(seed){let x=seed>>>0;return max=>{x=(x+0x6d2b79f5)>>>0;let t=Math.imul(x^(x>>>15),1|x);t^=t+Math.imul(t^(t>>>7),61|t);return ((t^(t>>>14))>>>0)%max;};}

export class ConnectedDetours3D extends ElevationMaze{
  constructor({width=28,height=28,levels=6,seed="LOST-6549",stairWeight=2}={}){
    super({width,height,levels,seed,stairWeight,generate:false});
    this.version=COMBINED_VERSION;this.model="connected";
    this.floorMetrics=[];this.flatMasks=[];this.stairOrigins=Array.from({length:levels-1},()=>[]);
    this.longRunsAdded=0;this.flightInterior=new Uint8Array(this.volume);this.build();
  }
  flat(z){
    // Full-floor global Connected Detours topology, not 16x16 submazes.
    // The same real maze-core algorithm produces all paths, pruning and loops.
    return new Maze({width:this.width,height:this.height,levels:1,
      style:"interconnected",seed:this.seed+"|"+COMBINED_VERSION+"|floor="+z});
  }
  build(){
    const {width:w,height:h,levels,area}=this;
    // 1. Generate each full-sized level from the previously approved maze
    //    algorithm; do not change a single horizontal edge.
    for(let z=0;z<levels;z++){
      const planar=this.flat(z),offset=z*area;
      this.flatMasks.push(new Uint8Array(planar.cells));this.floorMetrics.push(planar.metrics.after);
      for(let k=0;k<area;k++)if(!(planar.cells[k]&SOLID))this.used[offset+k]=1;
      for(let y=0;y<h;y++)for(let x=0;x<w;x++){
        const k=y*w+x,bits=planar.cells[k],i=offset+k;
        if(bits&SOLID)continue;
        if(bits&E)this.join(i,i+1,1);
        if(bits&S)this.join(i,i+w,2);
      }
    }
    // 2. Every neighboring pair of floors gets at least one real diagonal
    //    (in X/Z or Y/Z) walking ramp. The entire 3D maze is connected
    //    because each floor's independent Connected Detours maze is connected.
    for(let z=0;z<levels-1;z++)
      if(!this.single(z,0,true))throw new Error("No physically clear ramp can reach floor "+(z+1));
    // 3. Small quantities of independently chosen stair links. Occasionally
    //    a climb passes through original solid cells at intermediate stories
    //    and reaches a walkable landing several floors up. No prefabs, no
    //    compulsory landing when the height passes another story.
    const target=Math.max(1,Math.min(this.maxRampsPerBoundary,Math.ceil(area*this.stairWeight/240)));
    const random=seededRandom(this.hash(1505));
    for(let z=0;z<levels-2;z++)if(random(100)<65&&this.stairCountByBoundary[z]<target)
      if(this.longChain(z,target))this.longRunsAdded++;
    for(let z=0;z<levels-1;z++){
      let misses=0;
      while(this.stairCountByBoundary[z]<target&&misses<3){
        if(!this.single(z,100+misses+this.stairCountByBoundary[z],false))misses++;else misses=0;
      }
    }
    this.usedCount=0;this.levelCounts=[];
    for(let z=0;z<levels;z++){
      let count=0;
      for(let i=z*area;i<(z+1)*area;i++)count+=this.used[i];
      if(!count)throw new Error("An elevation contains no navigable space");
      this.levelCounts.push(count);this.usedCount+=count;
    }
    this.selectEndpoints();
    this.flatDeadEnds=this.floorMetrics.reduce((sum,x)=>sum+x.total,0);
    this.flatShortEnds=this.floorMetrics.reduce((sum,x)=>sum+x.short,0);
    this.stairShare=Number((100*this.stairs/this.edges).toFixed(2));
    this.longest=this.runs()[0]||null;
  }
  tooNear(z,a,b){
    for(const place of this.stairOrigins[z]){
      if(Math.abs(place.x-a.x)+Math.abs(place.y-a.y)<3)return true;
      if(Math.abs(place.x-b.x)+Math.abs(place.y-b.y)<3)return true;
    }
    return false;
  }
  possibleStep(i,code,requiredUsed){
    const j=this.neighbor(i,code);
    if(j<0||this.used[j]!==requiredUsed||this.flightInterior[i]||this.flightInterior[j]||this.bits[i]&(1<<code))return -1;
    if(!this.clear(i,j))return -1;
    if(this.stairCountByBoundary[this.loc(i).z]>=this.maxRampsPerBoundary)return -1;
    return j;
  }
  single(z,nonce=0,mandatory=false){
    const random=seededRandom(this.hash(1600+nonce,z)),offset=z*this.area;
    let chosen=null,seen=0;
    for(let k=0;k<this.area;k++){
      const i=offset+k;if(!this.used[i])continue;
      for(let d=0;d<4;d++){
        const code=4+d,j=this.possibleStep(i,code,1);
        if(j<0)continue;
        const a=this.loc(i),b=this.loc(j);
        if(!mandatory&&this.tooNear(z,a,b))continue;
        if(random(++seen)===0)chosen={i,j,code,a,b};
      }
    }
    if(!chosen)return false;
    this.join(chosen.i,chosen.j,chosen.code);
    this.stairOrigins[z].push(chosen.a);
    return true;
  }
  traceChain(i,code,target){
    const chain=[],dir=code%4;
    let at=i;
    while(true){
      const j=this.neighbor(at,4+dir);
      if(j<0||this.flightInterior[at]||this.flightInterior[j]||!this.clear(at,j))return null;
      const z=this.loc(at).z;
      if(this.stairCountByBoundary[z]>=target||this.stairCountByBoundary[z]>=this.maxRampsPerBoundary)return null;
      chain.push({from:at,to:j,code:4+dir,z});
      if(this.used[j])return chain.length>=3?chain:null;
      if(this.bits[j])return null; // A through-flight needs a real empty cell.
      if(chain.length>=this.levels-1)return null;
      at=j;
    }
  }
  longChain(z,target){
    const random=seededRandom(this.hash(1777,z)),offset=z*this.area;
    let chosen=null,seen=0;
    for(let k=0;k<this.area;k++){
      const i=offset+k;
      if(!this.used[i]||this.flightInterior[i])continue;
      for(let d=0;d<4;d++){
        const chain=this.traceChain(i,4+d,target);
        if(!chain)continue;
        if(chain.some(edge=>this.tooNear(edge.z,this.loc(edge.from),this.loc(edge.to))))continue;
        if(random(++seen)===0)chosen=chain;
      }
    }
    if(!chosen)return false;
    // All intermediate cells were SOLID on their floor. They become walkable
    // only as a straight through-flight, with no floor-specific exits.
    for(let k=0;k<chosen.length;k++){
      const edge=chosen[k];this.join(edge.from,edge.to,edge.code);this.used[edge.to]=1;
      if(k+1<chosen.length)this.flightInterior[edge.to]=1;
      this.stairOrigins[edge.z].push(this.loc(edge.from));
    }
    return true;
  }
  selectEndpoints(){
    const occupied=[];
    for(let i=0;i<this.volume;i++)if(this.used[i])occupied.push(i);
    const origin=occupied[this.hash(1800)%occupied.length];
    this.start=this.loc(origin);
    const dist=new Int32Array(this.volume),queue=new Uint32Array(this.volume);
    dist.fill(-1);queue[0]=origin;dist[origin]=0;
    let read=0,write=1;
    while(read<write){
      const i=queue[read++];
      for(const {to} of this.links(i))if(dist[to]===-1){
        dist[to]=dist[i]+1;queue[write++]=to;
      }
    }
    if(write!==this.usedCount)throw new Error("Connected Detours floors are disconnected after adding stairs ("+write+"/"+this.usedCount+")");
    let far=0;
    for(const i of occupied)if(this.loc(i).z!==this.start.z&&dist[i]>far)far=dist[i];
    const threshold=Math.max(1,Math.ceil(far*.85)),random=seededRandom(this.hash(1801));
    let count=0,exit=-1;
    for(const i of occupied)if(this.loc(i).z!==this.start.z&&dist[i]>=threshold)
      if(random(++count)===0)exit=i;
    if(exit<0)throw new Error("No distant connected exit exists");
    this.exit=this.loc(exit);this.routeLength=dist[exit];
  }
}
