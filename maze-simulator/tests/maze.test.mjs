import assert from 'node:assert/strict';
import {Maze,N,E,S,W,UP,DOWN,SOLID,STYLES,ENGINE_VERSION} from '../maze-core.mjs';
import {branchStats} from '../maze-grow.mjs';
import {floorSvg} from '../maze-map.mjs';
const DIRS=[[0,-1,0,N,S],[1,0,0,E,W],[0,1,0,S,N],[-1,0,0,W,E],[0,0,1,UP,DOWN],[0,0,-1,DOWN,UP]];
function check(opts){
 const maze=new Maze(opts),m=maze.cells,queue=new Uint32Array(maze.volume),seen=new Uint8Array(maze.volume);
 const start=maze.index(maze.start.x,maze.start.y,maze.start.z);
 let head=0,tail=1,n=0,stairs=0;
 queue[0]=start;seen[start]=1;
 for(let z=0;z<maze.levels;z++)for(let y=0;y<maze.height;y++)for(let x=0;x<maze.width;x++){
  const mask=maze.passages(x,y,z);
  if(mask&SOLID){assert.equal(mask,SOLID,'wall mass cannot contain passage bits');continue;}
  n++;assert.ok(mask,'no isolated navigable cell');
  for(const [dx,dy,dz,fwd,back] of DIRS){
   if(!(mask&fwd))continue;
   const nx=x+dx,ny=y+dy,nz=z+dz;
   assert.ok(nx>=0&&nx<maze.width&&ny>=0&&ny<maze.height&&nz>=0&&nz<maze.levels,'all edges inside bounds');
   assert.ok(maze.passages(nx,ny,nz)&back,'every passage must be reciprocal');
   if(dz>0)stairs++;
  }
 }
 while(head<tail){
  const cell=maze.location(queue[head++]);
  for(const pos of maze.neighbors(cell.x,cell.y,cell.z)){
   const i=maze.index(pos.x,pos.y,pos.z);
   assert.ok(!(m[i]&SOLID),'no walking into wall mass');
   if(!seen[i]){seen[i]=1;queue[tail++]=i;}
  }
 }
 assert.equal(tail,n,'all remaining navigable cells must be mutually reachable');
 assert.equal(n,maze.metrics.navigable);
 if(maze.levels>1)assert.ok(stairs>=maze.levels-1,'floor connections must survive spur removal');
 const exit=maze.index(maze.exit.x,maze.exit.y,maze.exit.z);
 assert.ok(exit!==start&&seen[exit]&&maze.routeLength>0,'distant distinct reachable exit');
 if(maze.levels>1)assert.notEqual(maze.exit.z,maze.start.z,'exit on another floor');
 assert.deepEqual(branchStats(maze,m),maze.metrics.after,'dead-end metrics derived from real passages');
 return maze;
}
assert.equal(ENGINE_VERSION,'global-v4');
assert.throws(()=>new Maze({width:1,height:1,levels:1}),/At least two/);
assert.throws(()=>new Maze({width:4096,height:4096,levels:2}),/Limit/);
assert.throws(()=>new Maze({width:11,height:9,levels:1,style:'unknown'}),/Unknown/);
for(const style of Object.keys(STYLES)){
 for(const seed of ['LOST-6549','FIVE-LEVELS','ANY-SEED']){
  for(const [width,height,levels] of [[2,1,1],[1,1,7],[7,9,1],[17,21,2],[33,29,3],[65,55,4]]){
   const opts={width,height,levels,style,seed},a=check(opts),b=new Maze(opts);
   assert.deepEqual(a.cells,b.cells,'same seed yields identical world');
   assert.deepEqual(a.start,b.start);assert.deepEqual(a.exit,b.exit);
   assert.ok(!('generateChunk' in a)&&!('baseEdge' in a),'no chunk topology');
   console.log('PASS',style,seed,width+'x'+height+'x'+levels,'median',a.metrics.after.median);
  }
 }
}
// Quantify and guard the specific user-reported regression.
for(const style of Object.keys(STYLES)){
 const a=check({width:64,height:64,levels:3,seed:'LOST-6549',style});
 assert.ok(a.metrics.after.short<a.metrics.before.short*0.06,'remove at least 94% of short dead ends');
 assert.ok(a.metrics.after.median>=7,'preserve medium and long detours');
 const plan=floorSvg(a,{floor:0,width:64,height:64});
 assert.equal(plan.stats.cells+plan.stats.solid,4096);
 assert.ok(plan.stats.solid>0&&plan.svg.includes('class="solid"'),'actual masonry shown in SVG');
 for(const size of [7,16,24,32,64]){
  const slice=new Uint8Array(a.volume);
  for(let z=0;z<a.levels;z++)for(let y0=0;y0<a.height;y0+=size)for(let x0=0;x0<a.width;x0+=size)
   for(let y=y0;y<Math.min(a.height,y0+size);y++)for(let x=x0;x<Math.min(a.width,x0+size);x++)
    slice[a.index(x,y,z)]=a.passages(x,y,z);
  assert.deepEqual(slice,a.cells,'render window chunks cannot influence topology');
 }
 console.log('DEPTH',style,JSON.stringify(a.metrics));
}
{
 const m=check({width:96,height:96,levels:3,seed:'NO-CHUNKS-EVER',style:'deep'});
 let seam=0,neighbor=0,ss=0,ns=0;
 for(let z=0;z<m.levels;z++)for(let y=0;y<m.height;y++)for(let x=1;x<m.width;x++){
  const open=(m.passages(x-1,y,z)&E)!==0;
  if(x%16===0){ss++;if(open)seam++;}
  else if(x%16===1||x%16===15){ns++;if(open)neighbor++;}
 }
 assert.ok(seam/ss>0.2,'old chunk lines cannot become walls');
 assert.ok((seam/ss)/(neighbor/ns)>0.65,'old chunk lines cannot create chokepoints');
}
for(const style of Object.keys(STYLES)){
 const begin=Date.now(),m=check({width:192,height:192,levels:8,seed:'PERFORMANCE',style});
 console.log('BENCH',style,m.volume,Date.now()-begin+'ms');
}
console.log('GLOBAL V4 TESTS PASSED');
