import assert from "node:assert/strict";
import {Maze,N,E,S,W,UP,DOWN,ENGINE_VERSION,MAX_CELLS,STYLES} from "../maze-core.mjs";
import {floorSvg} from "../maze-map.mjs";
const DIRECTIONS=[[0,-1,0,N,S],[1,0,0,E,W],[0,1,0,S,N],[-1,0,0,W,E],[0,0,1,UP,DOWN],[0,0,-1,DOWN,UP]];
function verify(opts){
  const m=new Maze(opts),n=m.volume,seen=new Uint8Array(n),queue=new Uint32Array(n);
  const start=m.index(m.start.x,m.start.y,m.start.z);
  seen[start]=1;queue[0]=start;let head=0,tail=1,edges=0,stairs=0;
  for(let z=0;z<m.levels;z++)for(let y=0;y<m.height;y++)for(let x=0;x<m.width;x++){
    const bits=m.passages(x,y,z);
    for(const [dx,dy,dz,forward,backward] of DIRECTIONS){
      const xx=x+dx,yy=y+dy,zz=z+dz;
      const inside=xx>=0&&xx<m.width&&yy>=0&&yy<m.height&&zz>=0&&zz<m.levels;
      if(bits&forward){
        assert.ok(inside,"passage outside world");
        assert.ok(m.passages(xx,yy,zz)&backward,"nonreciprocal opening");
        edges++;if(dz>0)stairs++;
      }
    }
  }
  while(head<tail){
    const {x,y,z}=m.location(queue[head++]);
    for(const to of m.neighbors(x,y,z)){
      const j=m.index(to.x,to.y,to.z);
      if(!seen[j]){seen[j]=1;queue[tail++]=j;}
    }
  }
  assert.equal(tail,n,"every global maze cell must be connected");
  assert.ok(edges>=n-1,"must contain spanning tree");
  if(m.levels>1)assert.ok(stairs>=m.levels-1,"each floor pair must be traversable");
  const end=m.index(m.exit.x,m.exit.y,m.exit.z);
  assert.ok(end!==start&&seen[end],"distinct and reachable exit");
  if(m.levels>1)assert.notEqual(m.exit.z,m.start.z,"multi-floor exit on different floor");
  assert.ok(m.routeLength>0,"meaningful path length");
  assert.equal(m.cells.length,n,"one contiguous world buffer");
  return m;
}
assert.equal(ENGINE_VERSION,"global-v3");
assert.throws(()=>new Maze({width:1,height:1,levels:1}),/At least two/);
assert.throws(()=>new Maze({width:4096,height:4096,levels:2}),/Limit/);
assert.throws(()=>new Maze({width:11,height:9,levels:1,style:"unknown"}),/Unknown/);
for(const style of Object.keys(STYLES)){
  for(const [width,height,levels] of [[7,9,1],[33,27,3],[49,34,4],[1,1,7],[17,17,2],[48,48,3],[2,1,1]]){
    const m=verify({width,height,levels,seed:"SINGLE-GLOBAL-MAZE",style});
    console.log("PASS global connectivity",style,width+"x"+height+"x"+levels,"route",m.routeLength);
  }
}
// The actual topology must be independent of where any renderer requests
// windows/chunks. Reconstruct an entire floor using many different sizes.
for(const style of Object.keys(STYLES)){
  const opts={width:65,height:55,levels:3,seed:"NO-CHECKERBOARD",style};
  const m=verify(opts),original=new Uint8Array(m.cells),full=floorSvg(m,{floor:1,x0:0,y0:0,width:65,height:55});
  for(const chunkSize of [7,16,24,32,64]){
    const sampled=new Uint8Array(m.volume);
    for(let z=0;z<m.levels;z++)for(let y0=0;y0<m.height;y0+=chunkSize)for(let x0=0;x0<m.width;x0+=chunkSize){
      for(let y=y0;y<Math.min(y0+chunkSize,m.height);y++)
        for(let x=x0;x<Math.min(x0+chunkSize,m.width);x++)
          sampled[m.index(x,y,z)]=m.passages(x,y,z);
    }
    assert.deepEqual(sampled,original,"render chunk size must not change topology");
  }
  const same=new Maze(opts);
  assert.deepEqual(same.cells,original,"same dimensions, seed and style must regenerate byte-for-byte");
  assert.deepEqual(same.start,m.start);assert.deepEqual(same.exit,m.exit);
  assert.equal(same.routeLength,m.routeLength);
  assert.equal(floorSvg(same,{floor:1,width:65,height:55}).svg,full.svg);
  assert.ok(!("counts" in m)&&!("baseEdge" in m)&&!("generateChunk" in m),"no chunk generation primitives");
  console.log("PASS seed, slice independence and actual SVG",style);
}
// The former 16-cell borders are ordinary edges now, not chokepoints.
{
  const m=new Maze({width:96,height:96,levels:3,seed:"NO-MORE-MINIMAZES",style:"balanced"});
  let seam=0,near=0,sc=0,nc=0;
  for(let z=0;z<m.levels;z++)for(let y=0;y<m.height;y++)for(let x=1;x<m.width;x++){
    const open=(m.passages(x-1,y,z)&E)!==0;
    if(x%16===0){sc++;if(open)seam++;}
    else if(x%16===1||x%16===15){nc++;if(open)near++;}
  }
  const a=seam/sc,b=near/nc;
  assert.ok(a>0.3&&a/b>0.65&&a/b<1.5,"16-cell seam must not be a boundary bottleneck");
  console.log("PASS no checkerboard seams",a.toFixed(3),b.toFixed(3));
}
{
  const a=new Maze({width:48,height:40,levels:3,seed:"ALPHA"});
  const b=new Maze({width:48,height:40,levels:3,seed:"BETA"});
  let diff=0;for(let i=0;i<a.cells.length;i++)if(a.cells[i]!==b.cells[i])diff++;
  assert.ok(diff>a.volume/4,"different seeds should give substantially different mazes");
  console.log("PASS seed sensitivity",diff,"/",a.volume);
}
{
  const start=Date.now(),m=new Maze({width:256,height:256,levels:8,seed:"HALF-MILLION",style:"balanced"});
  assert.equal(m.volume,524288);
  const sample=floorSvg(m,{floor:7,x0:175,y0:175,width:32,height:32});
  assert.equal(sample.stats.cells,1024);
  console.log("PASS 524288-cell global world, only a 32x32 view rendered",Date.now()-start,"ms");
}
console.log("GLOBAL-V3 mathematical maze tests passed.");
