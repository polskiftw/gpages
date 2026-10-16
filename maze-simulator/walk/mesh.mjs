// Geometry produced directly from ConnectedDetours3D: no prefabs, no
// hidden bypasses, and no arbitrary openings. Every edge is a sealed tube.
// Flat nodes get square landings; uninterrupted climbing nodes get NO pad.
import {PITCH,STORY,WIDTH,HEADROOM,SLAB,DIRECTIONS} from "../elevation/stairs.mjs";
import {edgeHeight,hasPad,halfWidth,padFraction} from "./navigation.mjs";
export const SURFACE=Object.freeze({BRICK:0,STONE:1,CEILING:2,STAIRS:3,EXIT:4});
const EPS=0.016;
const mix=(a,b,t)=>a+(b-a)*t;
export function buildMesh(maze,{center=maze.start,radius=13,floorRadius=2}={}){
  const data=[],stats={pads:0,through:0,edges:0,stairs:0,caps:0,triangles:0};
  const emit=(p,n,u,v,mat)=>data.push(p[0],p[1],p[2],n[0],n[1],n[2],u,v,mat);
  const tri=(a,b,c,mat,ua,ub,uc)=>{
    const v=[b[0]-a[0],b[1]-a[1],b[2]-a[2]],w=[c[0]-a[0],c[1]-a[1],c[2]-a[2]];
    let nx=v[1]*w[2]-v[2]*w[1],ny=v[2]*w[0]-v[0]*w[2],nz=v[0]*w[1]-v[1]*w[0];
    const norm=Math.hypot(nx,ny,nz)||1;nx/=norm;ny/=norm;nz/=norm;
    const n=[nx,ny,nz];emit(a,n,...ua,mat);emit(b,n,...ub,mat);emit(c,n,...uc,mat);stats.triangles++;
  };
  const quad=(a,b,c,d,mat,uv=[[0,0],[1,0],[1,1],[0,1]])=>{
    tri(a,b,c,mat,uv[0],uv[1],uv[2]);tri(a,c,d,mat,uv[0],uv[2],uv[3]);
  };
  const span=(a,b,l,r,y)=>[[a[0]+r[0]*l,y,a[1]+r[1]*l],[b[0]+r[0]*l,y,b[1]+r[1]*l]];
  const pad=(i)=>{
    const {x,y,z}=maze.loc(i),cx=x*PITCH,cz=y*PITCH,cy=z*STORY,h=halfWidth;
    const p0=[cx-h,cy,cz-h],p1=[cx+h,cy,cz-h],p2=[cx+h,cy,cz+h],p3=[cx-h,cy,cz+h];
    quad(p0,p1,p2,p3,SURFACE.STONE);
    const dy=cy+HEADROOM+EPS;
    quad([cx-h,dy,cz-h],[cx-h,dy,cz+h],[cx+h,dy,cz+h],[cx+h,dy,cz-h],SURFACE.CEILING);
    for(let code=0;code<4;code++)if(!maze.headingOccupied(i,code)){
      const d=DIRECTIONS[code],cx0=cx+d.dx*h,cz0=cz+d.dy*h,side=[-d.dy*h,d.dx*h];
      const a=[cx0-side[0],cy-SLAB,cz0-side[1]],b=[cx0+side[0],cy-SLAB,cz0+side[1]];
      const t=[cx0+side[0],dy,cz0+side[1]],u=[cx0-side[0],dy,cz0-side[1]];
      quad(a,b,t,u,SURFACE.BRICK,[[0,0],[WIDTH/.62,0],[WIDTH/.62,HEADROOM/.32],[0,HEADROOM/.32]]);
      stats.caps++;
    }
    if(x===maze.exit.x&&y===maze.exit.y&&z===maze.exit.z){
      const k=0.36,hh=cy+EPS*2;
      quad([cx-k,hh,cz-k],[cx+k,hh,cz-k],[cx+k,hh,cz+k],[cx-k,hh,cz+k],SURFACE.EXIT);
    }
    stats.pads++;
  };
  const edge=(i,j)=>{
    const a=maze.loc(i),b=maze.loc(j),dx=b.x-a.x,dz=b.y-a.y,rx=-dz,rz=dx;
    const steps=a.z===b.z?1:16,kind=a.z===b.z?SURFACE.STONE:SURFACE.STAIRS;
    // Include grade breakpoints exactly, so landings meet the slope without
    // holes or unexpected slopes inside their square landing floor.
    // The square landing occupies the last WIDTH/2 of every incoming edge.
    // NEVER put corridor sidewalls inside that square: doing so would make
    // legal N-to-E turns look blocked by internal floating wall panels.
    // Straight through-flight nodes have no landing and remain continuous.
    const start=padFraction(maze,i),end=1-padFraction(maze,j);
    const cuts=[start,end];
    if(steps>1)for(let s=1;s<steps;s++)cuts.push(start+(end-start)*s/steps);
    cuts.sort((x,y)=>x-y);
    const point=(t,side,offset=0)=>{
      const elevation=edgeHeight(maze,i,j,t)+offset;
      return [(a.x+dx*t)*PITCH+rx*side,elevation,(a.y+dz*t)*PITCH+rz*side];
    };
    for(let k=1;k<cuts.length;k++){
      const t0=cuts[k-1],t1=cuts[k];
      if(t1-t0<1e-6)continue;
      const l0=point(t0,-halfWidth),r0=point(t0,halfWidth);
      const l1=point(t1,-halfWidth),r1=point(t1,halfWidth);
      const long0=t0*PITCH,long1=t1*PITCH;
      const floorUv=[[0,long0*2],[WIDTH*2,long0*2],[WIDTH*2,long1*2],[0,long1*2]];
      quad(l0,r0,r1,l1,kind,floorUv);
      // Solid underside of the floor slab, including stairs. This is what
      // someone walking below sees as their ceiling when routes stack.
      const l0b=point(t0,-halfWidth,-SLAB),r0b=point(t0,halfWidth,-SLAB),
        l1b=point(t1,-halfWidth,-SLAB),r1b=point(t1,halfWidth,-SLAB);
      quad(l0b,l1b,r1b,r0b,SURFACE.CEILING,floorUv);
      const l0t=point(t0,-halfWidth,HEADROOM+EPS),r0t=point(t0,halfWidth,HEADROOM+EPS),
        l1t=point(t1,-halfWidth,HEADROOM+EPS),r1t=point(t1,halfWidth,HEADROOM+EPS);
      quad(l0t,l1t,r1t,r0t,SURFACE.CEILING,floorUv);
      // Sidewalls hide the sloping wedge from sideways approaches. The
      // entire corridor width is stairs, never a walk-around obstacle.
      const wallUv=[[long0/.62,0],[long1/.62,0],[long1/.62,HEADROOM/.32],[long0/.62,HEADROOM/.32]];
      quad(l0b,l1b,l1t,l0t,SURFACE.BRICK,wallUv);
      quad(r0b,r0t,r1t,r1b,SURFACE.BRICK,wallUv);
    }
    stats.edges++;if(a.z!==b.z)stats.stairs++;
  };
  const cx=Math.round(center.x),cy=Math.round(center.y),level=Math.round(center.z);
  const x0=Math.max(0,cx-radius-1),x1=Math.min(maze.width-1,cx+radius+1),
    y0=Math.max(0,cy-radius-1),y1=Math.min(maze.height-1,cy+radius+1),
    z0=Math.max(0,level-floorRadius),z1=Math.min(maze.levels-1,level+floorRadius);
  for(let z=z0;z<=z1;z++)for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){
    const i=maze.index(x,y,z);if(!maze.used[i])continue;
    if(hasPad(maze,i))pad(i);else stats.through++;
    for(const {to} of maze.links(i))if(to>i)edge(i,to);
  }
  return {vertices:new Float32Array(data),stats,vertexStride:9,bounds:{x0,x1,y0,y1,z0,z1}};
}
