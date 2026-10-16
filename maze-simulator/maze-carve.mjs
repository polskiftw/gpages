// One self-contained chunk at a time. Same chunk coordinates + seed => same passages.
const DX=[0,1,0,-1],DY=[-1,0,1,0],BIT=[1,2,4,8],OPPOSITE=[4,8,1,2];
const T={CHUNK:20,LOOP:21,OFFSET:23,VX:24,VY:25};
function rng(seed){
  let x=(seed||0x9e3779b9)>>>0;
  return max=>{x^=x<<13;x^=x>>>17;x^=x<<5;return (x>>>0)%max;};
}
export function carveChunk(m,cx,cy,z){
  if(![cx,cy,z].every((v,i)=>Number.isInteger(v)&&v>=0&&v<m.counts[i]))
    throw new RangeError("chunk outside world");
  const ox=cx*16,oy=cy*16,w=Math.min(16,m.width-ox),h=Math.min(16,m.height-oy);
  const cells=new Uint8Array(w*h),seen=new Uint8Array(w*h),random=rng(m.hash(T.CHUNK,cx,cy,z)),p=m.profile;
  const id=(x,y)=>y*w+x;
  const open=(i,j,d)=>{cells[i]|=BIT[d];cells[j]|=OPPOSITE[d];};
  const options=i=>{
    const x=i%w,y=Math.floor(i/w),out=[];
    for(let d=0;d<4;d++){
      const xx=x+DX[d],yy=y+DY[d];
      if(xx>=0&&yy>=0&&xx<w&&yy<h&&!seen[id(xx,yy)])out.push([id(xx,yy),d]);
    }
    return out;
  };
  seen[0]=1;
  if(p.mode==="prim"){
    const frontier=[];
    const add=i=>{for(const [j,d] of options(i))frontier.push([i,j,d]);};
    add(0);
    while(frontier.length){
      const choice=random(frontier.length),edge=frontier[choice];
      frontier[choice]=frontier[frontier.length-1];frontier.pop();
      const [i,j,d]=edge;
      if(seen[j])continue;
      seen[j]=1;open(i,j,d);add(j);
    }
  }else{
    const active=[{i:0,d:-1}];
    while(active.length){
      const last=p.mode==="dfs"||random(100)<p.recent,at=last?active.length-1:random(active.length);
      const a=active[at],available=options(a.i);
      if(!available.length){active.splice(at,1);continue;}
      let chosen=available[random(available.length)];
      if(a.d>=0&&random(100)<p.straight){
        const straight=available.find(([,d])=>d===a.d);
        if(straight)chosen=straight;
      }
      const [j,d]=chosen;
      seen[j]=1;open(a.i,j,d);active.push({i:j,d});
    }
  }
  // Internal braiding adds loops without affecting guaranteed reachability.
  for(let y=0;y<h;y++)for(let x=0;x<w;x++){
    const i=id(x,y);
    if(x+1<w&&!(cells[i]&2)&&m.hash(T.LOOP,ox+x,oy+y,z,0)%100<p.loops)open(i,i+1,1);
    if(y+1<h&&!(cells[i]&4)&&m.hash(T.LOOP,ox+x,oy+y,z,1)%100<p.loops)open(i,i+w,2);
  }
  // Every cross-chunk portal is calculated symmetrically by both neighbors.
  const here=[cx,cy,z];
  const seam=(nx,ny,nz,dir)=>{
    const other=[nx,ny,nz];
    if(!other.every((v,i)=>v>=0&&v<m.counts[i])||!m.macroEdge(here,other))return;
    if(dir===2||dir===8){
      const y=m.edgeHash(here,other,T.OFFSET)%h;
      cells[id(dir===2?w-1:0,y)]|=dir;
    }else if(dir===4||dir===1){
      const x=m.edgeHash(here,other,T.OFFSET)%w;
      cells[id(x,dir===4?h-1:0)]|=dir;
    }else{
      const x=m.edgeHash(here,other,T.VX)%w,y=m.edgeHash(here,other,T.VY)%h;
      cells[id(x,y)]|=dir;
    }
  };
  seam(cx,cy-1,z,1);seam(cx+1,cy,z,2);seam(cx,cy+1,z,4);seam(cx-1,cy,z,8);
  seam(cx,cy,z+1,16);seam(cx,cy,z-1,32);
  return {cx,cy,z,ox,oy,w,h,cells};
}
