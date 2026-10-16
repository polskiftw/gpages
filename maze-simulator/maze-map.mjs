// Exact renderer of Maze.passages. No maze generation or wall decisions here.
import {N,E,S,W,UP,DOWN,SOLID} from "./maze-core.mjs";
const unit=16,pad=10;
const popcount=x=>{let n=0;while(x){x&=x-1;n++;}return n;};
export function floorSvg(maze,{floor=0,x0=0,y0=0,width=32,height=32,small=false}={}){
  const cols=Math.min(width,maze.width-x0),rows=Math.min(height,maze.height-y0);
  const lines=[],marks=[],mass=[],stats={cells:0,solid:0,dead:0,straight:0,turns:0,junctions:0,vertical:0};
  if(cols<1||rows<1)throw new RangeError("window outside maze");
  for(let yy=0;yy<rows;yy++)for(let xx=0;xx<cols;xx++){
    const x=xx+x0,y=yy+y0,b=maze.passages(x,y,floor),left=pad+xx*unit,top=pad+yy*unit;
    const point=(a,b)=>a+" "+b,blocked=!!(b&SOLID);
    if(blocked){mass.push('<rect class="solid" x="'+left+'" y="'+top+'" width="'+unit+'" height="'+unit+'"/>');stats.solid++;}
    if(!(b&N)&&(!blocked||y===y0||!(maze.passages(x,y-1,floor)&SOLID)))lines.push("M "+point(left,top)+" h "+unit);
    if(!(b&W)&&(!blocked||x===x0||!(maze.passages(x-1,y,floor)&SOLID)))lines.push("M "+point(left,top)+" v "+unit);
    if(xx===cols-1&&!(b&E))lines.push("M "+point(left+unit,top)+" v "+unit);
    if(yy===rows-1&&!(b&S))lines.push("M "+point(left,top+unit)+" h "+unit);
    const count=popcount(b&(N|E|S|W)),cx=left+unit/2,cy=top+unit/2;
    if(!blocked)stats.cells++;if(count===1)stats.dead++;if(count>=3)stats.junctions++;
    if(count===2){if((b&N&&b&S)||(b&E&&b&W))stats.straight++;else stats.turns++;}
    if(b&(UP|DOWN)){
      stats.vertical++;
      if(!small) {
        const label=(b&UP)&&(b&DOWN)?"↕":b&UP?"↑":"↓";
        marks.push('<text class="stair" x="'+cx+'" y="'+(cy+0.5)+'">'+label+'</text>');
      }else marks.push('<circle class="stair-dot" cx="'+cx+'" cy="'+cy+'" r="2.3"/>');
    }
    if(x===maze.start.x&&y===maze.start.y&&floor===maze.start.z)
      marks.push('<circle class="start" cx="'+cx+'" cy="'+cy+'" r="6"/><text class="start-text" x="'+cx+'" y="'+(cy+0.5)+'">S</text>');
    if(x===maze.exit.x&&y===maze.exit.y&&floor===maze.exit.z)
      marks.push('<rect class="exit" x="'+(cx-6)+'" y="'+(cy-6)+'" width="12" height="12"/><text class="exit-text" x="'+cx+'" y="'+(cy+0.5)+'">E</text>');
  }
  const w=cols*unit+pad*2,h=rows*unit+pad*2;
  const svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 '+w+' '+h+'" role="img" aria-label="Exact floor '+(floor+1)+' maze passages">'
    +'<style>.floor{fill:#fff}.solid{fill:#dce2e8}.wall{fill:none;stroke:#1a2633;stroke-width:2.3;stroke-linejoin:miter;stroke-linecap:square}.edge{fill:none;stroke:#919aa4;stroke-width:1.1;stroke-dasharray:3 5}.stair{font:bold 12px sans-serif;fill:#465b77;text-anchor:middle;dominant-baseline:central}.stair-dot{fill:#465b77}.start{fill:#fff;stroke:#111e2b;stroke-width:2.7}.start-text,.exit-text{font:bold 8px sans-serif;text-anchor:middle;dominant-baseline:central}.exit{fill:#101e2c;stroke:#fff;stroke-width:1.5}.exit-text{fill:#fff}.start-text{fill:#101e2c}</style>'
    +'<rect class="floor" width="'+w+'" height="'+h+'"/>'
    +'<rect class="edge" x="'+pad+'" y="'+pad+'" width="'+(cols*unit)+'" height="'+(rows*unit)+'"/>'
    +mass.join("")+'<path class="wall" d="'+lines.join(" ")+'"/>'+marks.join("")+'</svg>';
  return {svg,stats,width:cols,height:rows};
}
