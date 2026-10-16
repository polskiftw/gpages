// Continuous physical movement constrained by the exact seeded maze graph.
// Collision is not a separate maze: every acceptable position lies inside a
// connected pad or enclosed edge ribbon. Overhead routes cannot be jumped to.
import {PITCH,STORY,WIDTH,vertical,DIRECTIONS} from "../elevation/stairs.mjs";
export const WALK_RADIUS=0.22,EYE_HEIGHT=1.58,MAX_FLOOR_STEP=0.43;
export const halfWidth=WIDTH/2;
export const hasPad=(maze,i)=>!!maze.used[i]&&!maze.through(i);
export function padFraction(maze,i){return hasPad(maze,i)?halfWidth/PITCH:0;}
export function edgeHeight(maze,i,j,t){
  const a=maze.loc(i),b=maze.loc(j);
  if(a.z===b.z)return a.z*STORY;
  const p0=padFraction(maze,i),p1=padFraction(maze,j);
  const f=Math.max(0,Math.min(1,(t-p0)/(1-p0-p1)));
  return STORY*(a.z+(b.z-a.z)*f);
}
export function ribbonSample(maze,i,j,x,z,radius=WALK_RADIUS){
  const a=maze.loc(i),b=maze.loc(j);
  const dx=b.x-a.x,dy=b.y-a.y,ax=a.x*PITCH,az=a.y*PITCH;
  const t=((x-ax)*dx+(z-az)*dy)/PITCH;
  const off=Math.abs((x-ax)*dy-(z-az)*dx);
  if(t<0||t>1||off>halfWidth-radius)return null;
  return {height:edgeHeight(maze,i,j,t),i,j,t,kind:a.z===b.z?"corridor":"stairs"};
}
export function floorAt(maze,x,z,expectedElevation,radius=WALK_RADIUS){
  const cx=Math.round(x/PITCH),cy=Math.round(z/PITCH);
  let best=null,delta=Infinity;
  const candidate=(hit)=>{
    if(!hit)return;
    const d=Math.abs(hit.height-expectedElevation);
    if(d<delta){delta=d;best=hit;}
  };
  // Three lattice coordinates around the player are enough because every
  // corridor segment is only one lattice edge long. All elevation levels
  // are inspected, but the current *physical height* chooses the route.
  for(let yy=Math.max(0,cy-1);yy<=Math.min(maze.height-1,cy+1);yy++)
    for(let xx=Math.max(0,cx-1);xx<=Math.min(maze.width-1,cx+1);xx++)
      for(let level=0;level<maze.levels;level++){
        const i=maze.index(xx,yy,level);
        if(!maze.used[i])continue;
        if(hasPad(maze,i)&&Math.abs(x-xx*PITCH)<=halfWidth-radius&&
           Math.abs(z-yy*PITCH)<=halfWidth-radius)
          candidate({height:level*STORY,i,kind:"landing"});
        for(const link of maze.links(i))if(i<link.to)
          candidate(ribbonSample(maze,i,link.to,x,z,radius));
      }
  return delta<=MAX_FLOOR_STEP?best:null;
}
export function initialPose(maze){
  const s=maze.start,i=maze.index(s.x,s.y,s.z),links=maze.links(i);
  const d=links.length?DIRECTIONS[links[0].code%4]:DIRECTIONS[0];
  return {x:s.x*PITCH,z:s.y*PITCH,feet:s.z*STORY,
    yaw:Math.atan2(d.dx,-d.dy),pitch:0};
}
export function movePlayer(maze,pose,deltaX,deltaZ){
  const candidate=(x,z,feet)=>{
    const found=floorAt(maze,x,z,feet);
    return found?{x,z,feet:found.height}:null;
  };
  // Sub-step movement prevents crossing even a thin closed wall in one frame.
  const n=Math.max(1,Math.ceil(Math.hypot(deltaX,deltaZ)/0.12));
  let next={...pose};
  for(let step=0;step<n;step++){
    const dx=deltaX/n,dz=deltaZ/n;
    const both=candidate(next.x+dx,next.z+dz,next.feet);
    const xOnly=both||candidate(next.x+dx,next.z,next.feet);
    const zOnly=xOnly||candidate(next.x,next.z+dz,next.feet);
    if(zOnly){next.x=zOnly.x;next.z=zOnly.z;next.feet=zOnly.feet;}
  }
  return next;
}
export function distanceToExit(maze,pose){
  return Math.hypot(pose.x-maze.exit.x*PITCH,pose.z-maze.exit.y*PITCH,
    (pose.feet-maze.exit.z*STORY)*2);
}
