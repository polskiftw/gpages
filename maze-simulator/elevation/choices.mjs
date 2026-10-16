// Meaningful junction density on a whole Connected Detours floor. We add
// only genuine long-loop *choices* between existing walkable corridors.
// No prefab rooms, new dead-end spurs, short square braids or chunk seams.
// Unmodified mode (0) retains the exact original v1 passage graph.
import {N,E,S,W,SOLID,randomNumber} from "../maze-core.mjs";
import {branchStats} from "../maze-grow.mjs";
const BITS=[N,E,S,W],REVERSE=[S,W,N,E];
const degree=b=>b&SOLID?0:(!!(b&N))+(!!(b&E))+(!!(b&S))+(!!(b&W));
function neighbors(w,h,i,callback){
  const x=i%w,y=Math.floor(i/w);
  if(y)callback(i-w,0);
  if(x+1<w)callback(i+1,1);
  if(y+1<h)callback(i+w,2);
  if(x)callback(i-1,3);
}
function distanceFrom(m,cells,source){
  const n=cells.length,dist=new Int32Array(n),queue=new Int32Array(n);
  dist.fill(-1);let read=0,write=0;
  for(let i=0;i<n;i++)if(degree(cells[i])&&source(i)){
    dist[i]=0;queue[write++]=i;
  }
  while(read<write){
    const i=queue[read++],next=dist[i]+1;
    neighbors(m.width,m.height,i,(j,d)=>{
      if((cells[i]&BITS[d])&&dist[j]<0){dist[j]=next;queue[write++]=j;}
    });
  }
  return dist;
}
export function choiceStats(m,cells=m.cells){
  const n=cells.length,deg=new Uint8Array(n);let junctions=0,leaves=0,walkable=0;
  for(let i=0;i<n;i++){const d=degree(cells[i]);deg[i]=d;if(d){walkable++;if(d>=3)junctions++;if(d===1)leaves++;}}
  const dist=distanceFrom(m,cells,i=>deg[i]>=3);
  const values=[];
  for(let i=0;i<n;i++)if(deg[i])values.push(dist[i]>=0?dist[i]:m.width+m.height);
  values.sort((a,b)=>a-b);
  const quantile=p=>values[Math.min(values.length-1,Math.floor(values.length*p))]||0;
  // A corridor "run" is the number of edges walked before a real junction
  // or dead-end, allowing bends: long turns without choices still count.
  const visited=new Set(),runs=[];
  for(let i=0;i<n;i++)if(deg[i]&&deg[i]!==2)
    neighbors(m.width,m.height,i,(j,d)=>{
      if(!(cells[i]&BITS[d]))return;
      const edge=(a,b)=>Math.min(a,b)*n+Math.max(a,b);
      if(visited.has(edge(i,j)))return;
      let a=i,b=j,length=0;
      while(true){
        visited.add(edge(a,b));length++;
        if(deg[b]!==2||length>n)break;
        let next=-1;
        neighbors(m.width,m.height,b,(k,dir)=>{if(k!==a&&(cells[b]&BITS[dir]))next=k;});
        if(next<0)break;
        a=b;b=next;
      }
      runs.push(length);
    });
  runs.sort((a,b)=>a-b);
  const r=p=>runs[Math.min(runs.length-1,Math.floor(runs.length*p))]||0;
  return {walkable,junctions,leaves,choicePer100:Number((junctions*100/Math.max(1,walkable)).toFixed(2)),
    meanToChoice:Number((values.reduce((a,b)=>a+b,0)/Math.max(1,values.length)).toFixed(2)),
    p90ToChoice:quantile(.90),maxToChoice:values.at(-1)||0,
    p50Run:r(.5),p90Run:r(.9),longestRun:runs.at(-1)||0};
}

// Bridge the solid whitespace *between* two existing long passages. This is
// different from v10's direct-neighbor braids: it can carve 1-4 solid cells
// to join separated corridors, with a two-ended route and NO new leaves.
// Intermediate cells form a sealed degree-2 corridor and receive no doors to
// side routes automatically. Every resulting opening is a true graph edge.
export function bridgeSolidGaps(m,{maxGap=4}={}){
  if(m.levels!==1)throw new RangeError("Physical connector operates on a complete flat floor");
  const {width:w,height:h}=m,cells=m.cells,n=cells.length;
  const original=choiceStats(m,cells),deg=new Uint8Array(n);
  for(let i=0;i<n;i++)deg[i]=degree(cells[i]);
  const choices=distanceFrom(m,cells,i=>deg[i]>=3);
  const leaves=distanceFrom(m,cells,i=>deg[i]===1);
  const rand=randomNumber(m.salt^0xc3e9a75f);
  const candidates=[];
  // A plausible connector walks through *solid* cells only, in a cardinal
  // direction, and ends at another degree-2 corridor. No side exits,
  // diagonal shortcuts, or invented staircase/room templates.
  const step=[-w,1,w,-1];
  for(let i=0;i<n;i++){
    if(deg[i]!==2||choices[i]<2||leaves[i]>=0&&leaves[i]<3)continue;
    const x=i%w,y=Math.floor(i/w);
    for(const d of [1,2]){
      const remain=d===1?w-x-1:h-y-1;
      if(remain<2)continue;
      for(let gap=1;gap<=Math.min(maxGap,remain-1);gap++){
        const end=i+step[d]*(gap+1);
        if(deg[end]===0)continue;
        if(deg[end]!==2||choices[end]<2||leaves[end]>=0&&leaves[end]<3)break;
        let clear=true;
        for(let t=1;t<=gap;t++)if(cells[i+step[d]*t]!==SOLID){clear=false;break;}
        if(!clear)break;
        const score=300*Math.min(choices[i],choices[end])+
          50*(choices[i]+choices[end])-gap*65+rand(130);
        candidates.push({i,end,d,gap,score});
        break;
      }
    }
  }
  candidates.sort((a,b)=>b.score-a.score||a.i-b.i||a.end-b.end);
  const seen=new Uint32Array(n),dist=new Uint16Array(n),queue=new Int32Array(n);
  const minExistingDetour=14,aim=Math.max(1,Math.ceil(original.walkable/13));
  let serial=0,bridges=0,carved=0;
  const endpoints=[];
  const longExistingRoute=(src,dst)=>{
    const mark=++serial;let read=0,write=1;
    queue[0]=src;seen[src]=mark;dist[src]=0;
    while(read<write){
      const i=queue[read++],next=dist[i]+1;
      if(next>=minExistingDetour)continue;
      neighbors(w,h,i,(j,d)=>{
        if(!(cells[i]&BITS[d])||seen[j]===mark)return;
        seen[j]=mark;dist[j]=next;queue[write++]=j;
      });
      if(seen[dst]===mark)return false;
    }
    return true;
  };
  for(const c of candidates){
    if(bridges>=aim)break;
    if(deg[c.i]!==2||deg[c.end]!==2)continue;
    let clear=true;
    for(let t=1;t<=c.gap;t++)if(cells[c.i+step[c.d]*t]!==SOLID){clear=false;break;}
    if(!clear)continue;
    // Don't fill every void with a checkerboard of cross-corridors.
    const near=p=>endpoints.some(q=>Math.abs(p%w-q%w)+Math.abs(Math.floor(p/w)-Math.floor(q/w))<2);
    if(near(c.i)||near(c.end)||!longExistingRoute(c.i,c.end))continue;
    cells[c.i]|=BITS[c.d];
    for(let t=1;t<=c.gap;t++)cells[c.i+step[c.d]*t]=BITS[c.d]|REVERSE[c.d];
    cells[c.end]|=REVERSE[c.d];
    deg[c.i]++;deg[c.end]++;
    endpoints.push(c.i,c.end);carved+=c.gap;bridges++;
  }
  m.metrics.after=branchStats(m,cells);
  return {bridges,carved,aim,candidates:candidates.length,minExistingDetour,maxGap,
    before:original,after:choiceStats(m,cells)};
}

export function enrichChoices(m,intensity=1){
  if(!Number.isInteger(intensity)||intensity<0||intensity>3)
    throw new RangeError("Choices must be 0 (original), 1 (more), or 2 (many)");
  if(intensity===3){
    const first=enrichChoices(m,2);
    const fill=bridgeSolidGaps(m);
    m.metrics.navigable+=fill.carved;
    return {mode:3,added:first.added+fill.bridges,goal:first.goal+fill.aim,
      bridges:fill.bridges,carved:fill.carved,connectors:first.added,
      candidates:first.candidates+fill.candidates,minLoop:first.minLoop,
      before:first.before,after:fill.after};
  }
  const cells=m.cells,w=m.width,h=m.height,n=cells.length,before=choiceStats(m,cells);
  if(!intensity)return {mode:0,added:0,goal:0,minLoop:0,before,after:before};
  if(m.levels!==1)throw new RangeError("Enrichment works on full-size flat floors only");
  const deg=new Uint8Array(n);
  for(let i=0;i<n;i++)deg[i]=degree(cells[i]);
  const toChoice=distanceFrom(m,cells,i=>deg[i]>=3);
  const toDeadEnd=distanceFrom(m,cells,i=>deg[i]===1);
  // Hard goal measured in new LONG alternatives per 25/15 navigable cells.
  // Only existing degree-2 corridors are linked: every new edge creates
  // two T-junctions, NOT two new 1-cell dead ends.
  const goal=Math.max(1,Math.ceil(before.walkable/(intensity===1?25:15)));
  const minLoop=Math.max(8,Math.min(intensity===1?14:12,Math.floor(Math.min(w,h)*.62)));
  const leafGuard=Math.max(3,Math.min(intensity===1?7:5,Math.floor(Math.min(w,h)/3)));
  const junctionGuard=intensity===1?2:1;
  const spacing=intensity===1?3:2;
  const random=randomNumber(m.salt^(intensity*0x7feb352d)^0xbaf76212);
  const candidates=[];
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){
    const i=y*w+x;if(deg[i]!==2||(toDeadEnd[i]>=0&&toDeadEnd[i]<leafGuard)||toChoice[i]<junctionGuard)continue;
    for(const [j,d] of [[x+1<w?i+1:-1,1],[y+1<h?i+w:-1,2]]){
      if(j<0||deg[j]!==2||(cells[i]&BITS[d])||(toDeadEnd[j]>=0&&toDeadEnd[j]<leafGuard)||toChoice[j]<junctionGuard)continue;
      // Prioritize locations where players currently go furthest without
      // encountering any junction. Tie-break by a deterministic seed.
      const score=Math.min(toChoice[i],toChoice[j])*1024+
        (toChoice[i]+toChoice[j])*70+random(500);
      candidates.push({i,j,d,score});
    }
  }
  candidates.sort((a,b)=>b.score-a.score||a.i-b.i||a.j-b.j);
  const seen=new Uint32Array(n),dist=new Uint16Array(n),queue=new Int32Array(n);
  let serial=0,added=0;
  const detourIsLong=(src,dst)=>{
    const mark=++serial;let read=0,write=1;
    queue[0]=src;seen[src]=mark;dist[src]=0;
    while(read<write){
      const i=queue[read++],next=dist[i]+1;
      if(next>=minLoop)continue;
      neighbors(w,h,i,(j,d)=>{
        if(!(cells[i]&BITS[d])||seen[j]===mark)return;
        if(j===dst){seen[dst]=mark;return;}
        seen[j]=mark;dist[j]=next;queue[write++]=j;
      });
      if(seen[dst]===mark)return false;
    }
    return true;
  };
  const chosen=[];
  for(const c of candidates){
    if(added>=goal)break;
    if(deg[c.i]!==2||deg[c.j]!==2)continue;
    const x=c.i%w,y=Math.floor(c.i/w);
    let crowded=false;
    for(const j of chosen){
      const xx=j%w,yy=Math.floor(j/w);
      if(Math.abs(xx-x)+Math.abs(yy-y)<spacing){crowded=true;break;}
    }
    if(crowded||!detourIsLong(c.i,c.j))continue;
    cells[c.i]|=BITS[c.d];cells[c.j]|=REVERSE[c.d];
    deg[c.i]++;deg[c.j]++;chosen.push(c.i,c.j);added++;
  }
  m.metrics.after=branchStats(m,cells);
  return {mode:intensity,added,goal,candidates:candidates.length,minLoop,before,after:choiceStats(m,cells)};
}
