// Full-world seeded graph carving, plus targeted repair of shallow dead ends.
// Renderer chunk sizes are never consulted or encoded in any passage.
import {N,E,S,W,UP,DOWN,randomNumber} from './maze-core.mjs';
const BITS=[1,2,4,8,16,32],REVERSE=[4,8,1,2,32,16],SOLID=64;
function degree(mask){if(mask&SOLID)return 0;let d=0;while(mask){mask&=mask-1;d++;}return d;}
export function branchStats(m,cells){
  const n=cells.length;
  const bins=[0,0,0,0],depths=[];
  for(let i=0;i<n;i++){
    if(degree(cells[i])!==1)continue;
    let from=-1,current=i,depth=0;
    while(depth<n){
      const links=connections(m,cells,current);
      if(links.length!==2&&current!==i)break;
      depth++;
      const next=links.find(x=>x.i!==from);
      if(!next)break;
      from=current;current=next.i;
      if(current===i)break;
    }
    depths.push(depth);
    bins[depth<=2?0:depth<=4?1:depth<=8?2:3]++;
  }
  return {total:depths.length,bins,short:bins[0]+bins[1],median:depths.sort((a,b)=>a-b)[Math.floor(depths.length/2)]||0};
}
function possible(m,i,seen,arr,dir,mask=0){
  const {width:w,height:h,levels,area}=m,xy=i%area,x=xy%w,y=Math.floor(xy/w),z=Math.floor(i/area);
  let n=0;
  const add=(j,d)=>{if((mask&BITS[d])||seen&&seen[j])return;arr[n]=j;dir[n++]=d;};
  if(y>0)add(i-w,0);if(x+1<w)add(i+1,1);if(y+1<h)add(i+w,2);if(x>0)add(i-1,3);
  if(z+1<levels&&m.stairCandidate(xy,z))add(i+area,4);
  if(z>0&&m.stairCandidate(xy,z-1))add(i-area,5);
  return n;
}
function connections(m,cells,i){
  const neighbors=new Uint32Array(6),dirs=new Uint8Array(6);
  const n=possible(m,i,null,neighbors,dirs,0);const result=[];
  for(let k=0;k<n;k++)if(cells[i]&BITS[dirs[k]])result.push({i:neighbors[k],dir:dirs[k]});
  return result;
}
export function carveWorld(m,T){
  const {width:w,height:h,levels,volume:n,area,profile:p}=m;
  const cells=new Uint8Array(n),seen=new Uint8Array(n),heading=new Uint8Array(n);
  heading.fill(255);
  const active=new Uint32Array(n),opts=new Uint32Array(6),dirs=new Uint8Array(6),random=randomNumber(m.hash(T.RNG));
  const root=m.hash(T.ROOT)%n;active[0]=root;seen[root]=1;
  let used=1,visited=1;
  const growFrom=(i,previous)=>{
    const length=possible(m,i,seen,opts,dirs);if(!length)return -1;
    let k=random(length);
    if(previous<6&&p.straight&&random(100)<p.straight)
      for(let q=0;q<length;q++)if(dirs[q]===previous){k=q;break;}
    if(p.lookahead&&length>1&&random(100)<75){
      // A small lookahead encourages a run into actual unexplored space,
      // rather than immediately getting trapped in a single-cell pocket.
      let best=-1,choice=k;const candidates=new Uint32Array(6),candidateDirs=new Uint8Array(6);
      for(let q=0;q<length;q++){
        const j=opts[q],count=possible(m,j,seen,candidates,candidateDirs);
        if(count>best){best=count;choice=q;}
        else if(count===best&&random(2)===0)choice=q;
      }
      if(random(100)<65)k=choice;
    }
    const j=opts[k],d=dirs[k];cells[i]|=BITS[d];cells[j]|=REVERSE[d];seen[j]=1;
    heading[j]=d;active[used++]=j;visited++;return j;
  };
  while(used){
    const at=p.mode==='strokes'?random(used):random(100)<p.latest?used-1:random(used);
    let cursor=active[at];
    const target=p.mode==='strokes'?p.minRun+random(p.maxRun-p.minRun+1):1;
    for(let step=0;step<target;step++){
      const next=growFrom(cursor,heading[cursor]);
      if(next<0){ // Remove only the stalled cell; older branch nodes remain viable.
        let pos=-1;
        // Avoid O(n) scans: 'cursor' is the newly appended final active cell
        // except in step 0, where 'at' indexes the chosen active start.
        pos=step===0?at:used-1;
        active[pos]=active[--used];
        break;
      }
      cursor=next;
    }
  }
  if(visited!==n)throw new Error('Global maze disconnected during carving: '+visited+'/'+n);
  const before=branchStats(m,cells);
  let pruned=0;
  const pruneNeighbors=new Uint32Array(6),pruneDirs=new Uint8Array(6);
  // Prune short terminal *spurs* into solid masonry, rather than filling
  // the map with tiny braids. A removed leaf cannot disconnect the remaining
  // passages. Never touch a staircase; protected vertical links persist.
  for(let pass=0;pass<5;pass++){
    let changed=0;
    for(let i=0;i<n;i++){
      if(degree(cells[i])!==1)continue;
      const stem=[i];let previous=-1,at=i,junction=-1;
      for(let distance=0;distance<p.pruneDepth;distance++){
        if(cells[at]&(UP|DOWN)){junction=-1;break;}
        const q=possible(m,at,null,pruneNeighbors,pruneDirs);let next=-1;
        for(let k=0;k<q;k++)if((cells[at]&BITS[pruneDirs[k]])&&pruneNeighbors[k]!==previous){next=pruneNeighbors[k];break;}
        if(next<0)break;
        if(degree(cells[next])>=3){junction=next;break;}
        if(degree(cells[next])!==2||(cells[next]&(UP|DOWN)))break;
        stem.push(next);previous=at;at=next;
      }
      if(junction<0||stem.length>p.pruneDepth)continue;
      // Remove one terminal edge at a time, preserving reciprocal openness.
      for(let k=0;k<stem.length;k++){
        const from=stem[k],to=k+1<stem.length?stem[k+1]:junction;
        const length=possible(m,from,null,pruneNeighbors,pruneDirs);
        let reverse=-1;
        for(let j=0;j<length;j++)if(pruneNeighbors[j]===to){reverse=REVERSE[pruneDirs[j]];break;}
        if(reverse<0)throw new Error('Pruning corrupted a corridor');
        cells[from]=SOLID;cells[to]&=~reverse;
      }
      pruned+=stem.length;changed+=stem.length;
    }
    if(!changed)break;
  }
  const afterPrune=branchStats(m,cells);
  const stage=randomNumber(m.hash(T.REPAIR));
  const stamp=new Uint32Array(n),distance=new Uint8Array(n),queue=new Uint32Array(n);
  let generation=0,repairs=0,clusterRepairs=0;
  const neighbors=new Uint32Array(6),ndirs=new Uint8Array(6);
  const boundedVisit=(i,maxDepth)=>{
    const mark=++generation;let head=0,tail=1;queue[0]=i;stamp[i]=mark;distance[i]=0;
    while(head<tail){
      const v=queue[head++],dd=distance[v];if(dd>=maxDepth)continue;
      const cc=possible(m,v,null,neighbors,ndirs);
      for(let k=0;k<cc;k++)if(cells[v]&BITS[ndirs[k]]){
        const j=neighbors[k];if(stamp[j]===mark)continue;
        stamp[j]=mark;distance[j]=dd+1;queue[tail++]=j;
      }
    }
    return mark;
  };
  const stemDepth=(i,stop)=>{
    let previous=-1,current=i,depth=0;
    while(depth<=stop){
      depth++;
      if(current!==i&&degree(cells[current])!==2)return {depth,junction:degree(cells[current])>=3};
      const len=possible(m,current,null,neighbors,ndirs);
      let next=-1;
      for(let k=0;k<len;k++)if(cells[current]&BITS[ndirs[k]]&&neighbors[k]!==previous){next=neighbors[k];break;}
      if(next<0)return {depth,junction:false};
      previous=current;current=next;
    }
    return {depth,junction:false};
  };
  const braid=(i)=>{
    if(degree(cells[i])!==1)return false;
    // Only open connections that make a meaningful topological detour:
    // reject neighboring edges whose existing roundabout is <= 9 steps.
    const mark=boundedVisit(i,9),length=possible(m,i,null,neighbors,ndirs,cells[i]);
    let choice=-1,best=-1;
    for(let k=0;k<length;k++){
      const j=neighbors[k],d=degree(cells[j]);
      if(stamp[j]===mark||(cells[j]&SOLID))continue;
      const score=(d===2?40:d===1?30:d===3?10:0)+stage(20);
      if(score>best){choice=k;best=score;}
    }
    if(choice<0)return false;
    const j=neighbors[choice],d=ndirs[choice];
    cells[i]|=BITS[d];cells[j]|=REVERSE[d];return true;
  };
  // Repairs may indirectly alter neighbors' stem lengths, hence two passes.
  for(let pass=0;pass<2;pass++){
    const order=[];
    for(let i=0;i<n;i++)if(degree(cells[i])===1){const info=stemDepth(i,p.repairDepth);if(info.junction&&info.depth<=p.repairDepth)order.push([info.depth,m.hash(T.REPAIR,i,pass),i]);}
    order.sort((a,b)=>a[0]-b[0]||a[1]-b[1]);
    for(const [, ,i] of order){
      if(degree(cells[i])!==1)continue;
      const info=stemDepth(i,p.repairDepth);
      if(info.junction&&info.depth<=p.repairDepth&&braid(i))repairs++;
    }
  }
  // Some medium-length leaves in tight local clusters also get reconnected.
  // This addresses the comb-teeth look without fully braiding the labyrinth.
  for(let i=0;i<n;i++){
    if(degree(cells[i])!==1||stage(100)>=p.cluster)continue;
    const xy=i%area,x=xy%w,y=Math.floor(xy/w),z=Math.floor(i/area),off=z*area;
    let otherLeaf=false;
    for(let dy=-3;dy<=3&&!otherLeaf;dy++)for(let dx=-3;dx<=3;dx++){
      if(dx===0&&dy===0||Math.abs(dx)+Math.abs(dy)>3)continue;
      const xx=x+dx,yy=y+dy;
      if(xx>=0&&yy>=0&&xx<w&&yy<h&&degree(cells[off+yy*w+xx])===1){otherLeaf=true;break;}
    }
    if(otherLeaf&&stemDepth(i,12).depth<=12&&braid(i))clusterRepairs++;
  }
  // Low-density seeded long-cycle openings; reject 4-cell miniloops.
  let loopsAdded=0;
  for(let z=0;z<levels;z++)for(let y=0;y<h;y++)for(let x=0;x<w;x++){
    const i=z*area+y*w+x,xy=y*w+x;
    if(!(cells[i]&SOLID)){
      if(x+1<w&&!(cells[i+1]&SOLID)&&!(cells[i]&E)&&m.hash(T.LOOP,xy,z,0)%1000<p.cycles){
        const mark=boundedVisit(i,8);if(stamp[i+1]!==mark){cells[i]|=E;cells[i+1]|=W;loopsAdded++;}
      }
      if(y+1<h&&!(cells[i+w]&SOLID)&&!(cells[i]&S)&&m.hash(T.LOOP,xy,z,1)%1000<p.cycles){
        const mark=boundedVisit(i,8);if(stamp[i+w]!==mark){cells[i]|=S;cells[i+w]|=N;loopsAdded++;}
      }
    }
    if(z+1<levels&&!(cells[i]&SOLID)&&!(cells[i+area]&SOLID)&&!(cells[i]&UP)&&m.stairCandidate(xy,z)&&m.hash(T.STAIRLOOP,xy,z)%100<p.extraStairs){
      cells[i]|=UP;cells[i+area]|=DOWN;
    }
  }
  const after=branchStats(m,cells);
  // Exact shortest-path travel distance for exit placement.
  let source=m.hash(T.START)%n;
  while(cells[source]&SOLID)source=(source+1)%n;
  const navigable=n-pruned,dist=new Int32Array(n),travel=new Uint32Array(n);
  dist.fill(-1);travel[0]=source;dist[source]=0;
  let head=0,tail=1;
  while(head<tail){
    const i=travel[head++],xy=i%area,x=xy%w,y=Math.floor(xy/w),mask=cells[i],v=dist[i]+1;
    const push=j=>{if(dist[j]===-1){dist[j]=v;travel[tail++]=j;}};
    if(mask&N)push(i-w);if(mask&E)push(i+1);if(mask&S)push(i+w);if(mask&W)push(i-1);
    if(mask&UP)push(i+area);if(mask&DOWN)push(i-area);
  }
  if(tail!==navigable)throw new Error('Global maze disconnected after pruning/repair ('+tail+'/'+navigable+')');
  const startFloor=Math.floor(source/area);let farthest=0;
  for(let i=0;i<n;i++)if(dist[i]>=0&&(levels===1||Math.floor(i/area)!==startFloor)&&dist[i]>farthest)farthest=dist[i];
  const threshold=Math.max(1,Math.ceil(farthest*0.85)),pick=randomNumber(m.hash(T.END));
  let candidates=0,exit=-1;
  for(let i=0;i<n;i++)if((levels===1||Math.floor(i/area)!==startFloor)&&dist[i]>=threshold&&pick(++candidates)===0)exit=i;
  if(exit<0||exit===source)throw new Error('Missing distant exit');
  return {cells,start:m.location(source),exit:m.location(exit),routeLength:dist[exit],metrics:{before,afterPrune,after,pruned,repairs,clusterRepairs,loopsAdded,navigable}};
}
