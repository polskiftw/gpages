// Precise SVGs from the same directed navigation edges used by the geometry
// exporter. There is no independently invented staircase shape.
import {PITCH,STORY,vertical,direction,DIRECTIONS,popcount} from "./stairs.mjs";
const CELL=19,PAD=18,ROUND=n=>Number(n.toFixed(2));
const escapeXml=s=>String(s).replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
export function floorPlanSvg(m,z,{x0=0,y0=0,width=m.width,height=m.height,highlight=null}={}){
  if(!Number.isInteger(z)||z<0||z>=m.levels)throw new RangeError("Invalid floor");
  const w=Math.min(width,m.width-x0),h=Math.min(height,m.height-y0);
  if(w<1||h<1)throw new RangeError("Map window is outside labyrinth");
  const at=(x,y)=>[PAD+(x-x0+.5)*CELL,PAD+(y-y0+.5)*CELL];
  const path=[],pads=[],signs=[],markers=[];
  let flats=0,up=0,down=0,landings=0,through=0;
  const inside=(x,y)=>x>=x0&&y>=y0&&x<x0+w&&y<y0+h;
  for(let y=y0;y<y0+h;y++)for(let x=x0;x<x0+w;x++){
    const i=m.index(x,y,z);if(!m.used[i])continue;
    const [cx,cy]=at(x,y),links=m.links(i);
    const isThrough=m.through(i);
    if(isThrough)through++;else landings++;
    pads.push('<rect x="'+(cx-3.3)+'" y="'+(cy-3.3)+'" width="6.6" height="6.6" fill="#fff"/>');
    if(!isThrough&&links.length>=3)
      markers.push('<circle cx="'+cx+'" cy="'+cy+'" r="2.4" fill="#1d3145"/>');
    for(const {to,code} of links){
      const p=m.loc(to),[tx,ty]=at(p.x,p.y),v=vertical(code);
      if(v===0){
        if(i<to&&inside(p.x,p.y)){path.push('<path d="M'+cx+' '+cy+'L'+tx+' '+ty+'" class="flat"/>');flats++;}
      }else{
        const d=DIRECTIONS[direction(code)];
        const x2=cx+d.dx*CELL*.39,y2=cy+d.dy*CELL*.39;
        path.push('<path d="M'+cx+' '+cy+'L'+x2+' '+y2+'" class="ramp"/>');
        if(v>0)up++;else down++;
        // Text, not color alone, distinguishes up from down.
        signs.push('<text x="'+(cx+d.dx*CELL*.43)+'" y="'+(cy+d.dy*CELL*.43)+'" class="rise">'+(v>0?'U':'D')+'</text>');
      }
    }
    if(x===m.start.x&&y===m.start.y&&z===m.start.z)
      markers.push('<circle cx="'+cx+'" cy="'+cy+'" r="5" fill="#fff" stroke="#0d263b" stroke-width="2.3"/><text x="'+cx+'" y="'+(cy+.3)+'" class="endpoint">S</text>');
    if(x===m.exit.x&&y===m.exit.y&&z===m.exit.z)
      markers.push('<rect x="'+(cx-5)+'" y="'+(cy-5)+'" width="10" height="10" fill="#102a40"/><text x="'+cx+'" y="'+(cy+.3)+'" class="endpoint" fill="#fff">E</text>');
  }
  if(highlight)for(const p of highlight.vertices)if(p.z===z&&inside(p.x,p.y)){
    const [cx,cy]=at(p.x,p.y);
    markers.push('<circle cx="'+cx+'" cy="'+cy+'" r="6" class="selected"/>');
  }
  const sw=w*CELL+PAD*2,sh=h*CELL+PAD*2;
  const svg='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 '+sw+' '+sh+'" role="img" aria-label="Exact navigable network at elevation '+z+'">'
    +'<style>.back{fill:#d3dbe2}.flat{fill:none;stroke:#fff;stroke-width:7;stroke-linecap:square}.ramp{fill:none;stroke:#fff;stroke-width:7;stroke-linecap:round}.rise{fill:#071d31;font:900 10px monospace;text-anchor:middle;dominant-baseline:central}.endpoint{font:bold 8px sans-serif;text-anchor:middle;dominant-baseline:central;fill:#0d263b}.selected{fill:none;stroke:#99502a;stroke-width:2.4}</style>'
    +'<rect class="back" width="'+sw+'" height="'+sh+'"/>'+path.join('')+pads.join('')+signs.join('')+markers.join('')+'</svg>';
  return {svg,stats:{flats,up,down,landings,through}};
}
export function runProfileSvg(run){
  if(!run)return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 160"><text x="25" y="60">No continuous stairs generated</text></svg>';
  const points=run.vertices;
  const lo=Math.min(...points.map(p=>p.z)),hi=Math.max(...points.map(p=>p.z));
  const width=660,height=230,padding=42;
  const dx=(width-2*padding)/Math.max(1,points.length-1);
  const dy=(height-2*padding)/Math.max(1,hi-lo);
  const at=i=>[ROUND(padding+i*dx),ROUND(height-padding-(points[i].z-lo)*dy)];
  const path=points.map((_,i)=>(i?'L':'M')+at(i).join(' ')).join(' ');
  const lines=Array.from({length:hi-lo+1},(_,k)=>{
    const z=lo+k,y=ROUND(height-padding-k*dy);
    return '<path d="M'+padding+' '+y+'H'+(width-padding)+'" stroke="#bac5d1" stroke-dasharray="4 5"/>'
      +'<text x="'+(padding-10)+'" y="'+(y+4)+'" font-family="monospace" font-size="12" text-anchor="end" fill="#263b50">z'+z+'</text>';
  }).join('');
  const labels=points.map((p,i)=>{
    const [x,y]=at(i);
    return '<circle cx="'+x+'" cy="'+y+'" r="4" fill="#21394e"/><text x="'+x+'" y="'+(y-11)+'" text-anchor="middle" font-size="11" font-family="monospace" fill="#17293b">'+escapeXml('('+p.x+','+p.y+','+p.z+')')+'</text>';
  }).join('');
  return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 '+width+' '+height+'" role="img" aria-label="Actual continuous staircase rising '+run.rise+' levels in '+run.steps+' horizontal steps">'
    +'<rect width="'+width+'" height="'+height+'" fill="#fff"/>'+lines
    +'<path d="'+path+'" fill="none" stroke="#506276" stroke-width="9" stroke-linejoin="round" stroke-linecap="round"/>'
    +'<path d="'+path+'" fill="none" stroke="#dfd1b6" stroke-width="5" stroke-linejoin="round"/>'
    +labels+'<text x="'+(width/2)+'" y="218" text-anchor="middle" font-size="12" font-family="sans-serif" fill="#263b50">'
    +Math.abs(run.rise)*STORY+' m vertical change over '+run.steps*PITCH+' m of horizontal travel</text></svg>';
}
