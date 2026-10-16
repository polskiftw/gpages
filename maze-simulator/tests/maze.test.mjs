import assert from "node:assert/strict";
import {Maze,N,E,S,W,UP,DOWN,STYLES,CHUNK} from "../maze-core.mjs";
import {floorSvg} from "../maze-map.mjs";
const DIRS=[[0,-1,0,N,S],[1,0,0,E,W],[0,1,0,S,N],[-1,0,0,W,E],[0,0,1,UP,DOWN],[0,0,-1,DOWN,UP]];
function checkWorld(opts){
  const m=new Maze(opts),volume=m.width*m.height*m.levels,visited=new Uint8Array(volume);
  const key=(x,y,z)=>(z*m.height+y)*m.width+x;
  const todo=[m.start];visited[key(m.start.x,m.start.y,m.start.z)]=1;
  let head=0,edges=0,vertical=0;
  for(let z=0;z<m.levels;z++)for(let y=0;y<m.height;y++)for(let x=0;x<m.width;x++){
    const mask=m.passages(x,y,z);
    for(const [dx,dy,dz,bit,reverse] of DIRS){
      const xx=x+dx,yy=y+dy,zz=z+dz,has=(mask&bit)!==0;
      const inside=xx>=0&&yy>=0&&zz>=0&&xx<m.width&&yy<m.height&&zz<m.levels;
      if(has){assert.ok(inside,"passage outside maze");
        assert.ok((m.passages(xx,yy,zz)&reverse)!==0,"one-way boundary passage");
        edges++;if(dz>0)vertical++;
      }else if(!inside)assert.ok(!has,"boundary must be walled");
    }
  }
  while(head<todo.length){
    const {x,y,z}=todo[head++];
    for(const n of m.neighbors(x,y,z)){
      const index=key(n.x,n.y,n.z);
      if(!visited[index]){visited[index]=1;todo.push(n);}
    }
  }
  assert.equal(todo.length,volume,"EVERY tile across EVERY level must be reachable");
  assert.equal(visited[key(m.exit.x,m.exit.y,m.exit.z)],1,"exit must be reachable");
  assert.notDeepEqual(m.start,m.exit,"different start and exit");
  if(m.levels>1)assert.ok(vertical>0,"must have vertical stairs");
  assert.ok(edges>=volume-1,"at least a spanning tree");
  assert.deepEqual([...m.generateChunk(0,0,0).cells],[...m.generateChunk(0,0,0).cells]);
  return m;
}
assert.throws(()=>new Maze({width:1,height:1,levels:1}),/at least two/);
for(const style of Object.keys(STYLES)){
  for(const [width,height,levels] of [[7,9,1],[33,27,3],[49,34,4],[1,1,7],[17,17,2]]){
    // The 1x1 multi-floor world necessarily has start/exit at the same XY but different Z.
    checkWorld({width,height,levels,style,seed:"SAFE-GRAPH"});
    console.log("PASS",style,width+"x"+height+"x"+levels);
  }
}
for(const style of Object.keys(STYLES)){
  const m=new Maze({width:63,height:51,levels:5,seed:"PORTALS",style,cacheLimit:1});
  const coords=[];
  for(let z=0;z<m.levels;z++)for(let cy=0;cy<m.counts[1];cy++)for(let cx=0;cx<m.counts[0];cx++)coords.push([cx,cy,z]);
  const normal=new Map(coords.map(c=>[c.join(","),[...m.generateChunk(...c).cells]]));
  coords.reverse();
  for(const c of coords)assert.deepEqual([...m.chunk(...c).cells],normal.get(c.join(",")),"cache order should not affect output");
  console.log("PASS order independence",style);
}
{
  const m=new Maze({width:49,height:33,levels:5,seed:"TREE"});
  const vertices=m.counts.reduce((a,b)=>a*b,1);
  let count=0;const seen=new Set(["0,0,0"]),queue=[[0,0,0]];
  while(queue.length){
    const a=queue.shift();
    for(const b of [[a[0]+1,a[1],a[2]],[a[0]-1,a[1],a[2]],[a[0],a[1]+1,a[2]],[a[0],a[1]-1,a[2]],[a[0],a[1],a[2]+1],[a[0],a[1],a[2]-1]]){
      if(!b.every((v,i)=>v>=0&&v<m.counts[i])||!m.baseEdge(a,b))continue;
      const k=b.join(",");if(!seen.has(k)){seen.add(k);queue.push(b);}
      if(a.join(",")<k)count++;
    }
  }
  assert.equal(seen.size,vertices,"macro graph connected");
  assert.equal(count,vertices-1,"macro graph is exactly a spanning tree");
}
{
  const a=new Maze({width:47,height:38,levels:3,seed:"ONE"});
  const b=new Maze({width:47,height:38,levels:3,seed:"TWO"});
  let differences=0;
  for(let y=0;y<38;y++)for(let x=0;x<47;x++)if(a.passages(x,y,0)!==b.passages(x,y,0))differences++;
  assert.ok(differences>200,"seed should change the maze");
  const svg=floorSvg(a,{floor:0,width:32,height:32,x0:0,y0:0});
  assert.ok(svg.svg.includes('<path class="wall"')&&svg.stats.cells===1024);
  assert.equal(floorSvg(a,{floor:0,width:32,height:32}).svg,svg.svg,"SVG deterministic");
}
console.log("All topology, symmetry, reachability, LRU-order and SVG checks passed.");
