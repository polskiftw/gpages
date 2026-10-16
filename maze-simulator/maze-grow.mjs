// One growing-tree traversal over the ENTIRE 3D grid. No chunk size, seams,
// macro portals, per-chunk starts, or submazes anywhere in this algorithm.
import {N,E,S,W,UP,DOWN,randomNumber} from "./maze-core.mjs";
export function carveWorld(m,T){
  const {width:w,height:h,levels,volume,area,profile:p}=m;
  const cells=new Uint8Array(volume),seen=new Uint8Array(volume),heading=new Uint8Array(volume);
  heading.fill(255);
  const active=new Uint32Array(volume),random=randomNumber(m.hash(T.RNG));
  const options=new Uint32Array(6),dirs=new Uint8Array(6);
  const bits=[N,E,S,W,UP,DOWN],reverse=[S,W,N,E,DOWN,UP];
  let used=1,visited=1;
  const root=m.hash(T.ROOT)%volume;
  active[0]=root;seen[root]=1;
  while(used){
    const at=random(100)<p.latest?used-1:random(used),i=active[at];
    const xy=i%area,x=xy%w,y=Math.floor(xy/w),z=Math.floor(i/area);
    let n=0;
    if(y>0&&!seen[i-w]){options[n]=i-w;dirs[n++]=0;}
    if(x+1<w&&!seen[i+1]){options[n]=i+1;dirs[n++]=1;}
    if(y+1<h&&!seen[i+w]){options[n]=i+w;dirs[n++]=2;}
    if(x>0&&!seen[i-1]){options[n]=i-1;dirs[n++]=3;}
    if(z+1<levels&&!seen[i+area]&&m.stairCandidate(xy,z)){options[n]=i+area;dirs[n++]=4;}
    if(z>0&&!seen[i-area]&&m.stairCandidate(xy,z-1)){options[n]=i-area;dirs[n++]=5;}
    if(!n){active[at]=active[--used];continue;}
    let k=random(n),direction=heading[i];
    if(direction<6&&p.straight&&random(100)<p.straight){
      for(let j=0;j<n;j++)if(dirs[j]===direction){k=j;break;}
    }
    const neighbor=options[k],d=dirs[k];
    cells[i]|=bits[d];cells[neighbor]|=reverse[d];
    seen[neighbor]=1;heading[neighbor]=d;active[used++]=neighbor;visited++;
  }
  if(visited!==volume)throw new Error("Internal error: incomplete global carve ("+visited+"/"+volume+")");
  // A small seeded number of additional passages makes loops. Nothing is
  // ever removed, so all vertices stay connected and seam locations stay ordinary.
  for(let z=0;z<levels;z++){
    const floor=z*area;
    for(let y=0;y<h;y++)for(let x=0;x<w;x++){
      const xy=y*w+x,i=floor+xy;
      if(x+1<w&&!(cells[i]&E)&&m.hash(T.LOOP,xy,z,0)%100<p.loops){
        cells[i]|=E;cells[i+1]|=W;
      }
      if(y+1<h&&!(cells[i]&S)&&m.hash(T.LOOP,xy,z,1)%100<p.loops){
        cells[i]|=S;cells[i+w]|=N;
      }
      if(z+1<levels&&!(cells[i]&UP)&&m.stairCandidate(xy,z)&&m.hash(T.STAIRLOOP,xy,z)%100<p.extraStairs){
        cells[i]|=UP;cells[i+area]|=DOWN;
      }
    }
  }
  // Choose a random seeded starting cell, then a seeded random exit among
  // cells at least 85% as far as the most distant reachable cell. Require
  // another floor for the exit when the maze has multiple floors.
  const source=m.hash(T.START)%volume,queue=active,dist=new Int32Array(volume);
  dist.fill(-1);queue[0]=source;dist[source]=0;
  let head=0,tail=1;
  while(head<tail){
    const i=queue[head++],xy=i%area,x=xy%w,y=Math.floor(xy/w),v=dist[i]+1,mask=cells[i];
    const push=j=>{if(dist[j]!==-1)return;dist[j]=v;queue[tail++]=j;};
    if(mask&N)push(i-w);
    if(mask&E)push(i+1);
    if(mask&S)push(i+w);
    if(mask&W)push(i-1);
    if(mask&UP)push(i+area);
    if(mask&DOWN)push(i-area);
  }
  if(tail!==volume)throw new Error("Internal error: disconnected maze graph");
  const startFloor=Math.floor(source/area);
  let farthest=0;
  for(let i=0;i<volume;i++)if((levels===1||Math.floor(i/area)!==startFloor)&&dist[i]>farthest)
    farthest=dist[i];
  const threshold=Math.max(1,Math.ceil(farthest*0.85)),pick=randomNumber(m.hash(T.END));
  let candidates=0,exit=-1;
  for(let i=0;i<volume;i++){
    if((levels!==1&&Math.floor(i/area)===startFloor)||dist[i]<threshold)continue;
    if(pick(++candidates)===0)exit=i;
  }
  if(exit===-1||exit===source)throw new Error("Internal error: no distant exit");
  return {cells,start:m.location(source),exit:m.location(exit),routeLength:dist[exit]};
}
