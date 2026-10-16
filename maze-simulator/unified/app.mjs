import {ConnectedDetours3D,COMBINED_VERSION} from "../elevation/combined.mjs";
import {PITCH,STORY,WIDTH,HEADROOM,SLAB} from "../elevation/stairs.mjs";
import {floorPlanSvg,runProfileSvg} from "../elevation/view.mjs";
const $=id=>document.getElementById(id),clamp=(x,lo,hi)=>Math.max(lo,Math.min(hi,x));
let maze=null,floor=0,zoom=32,x0=0,y0=0,runs=[],selected=0,lastSvg="",pointer=null;
function options(){return {width:Number($("width").value),height:Number($("height").value),levels:Number($("levels").value),seed:$("seed").value.trim(),stairWeight:Number($("stairWeight").value),choices:Number($("choices").value)};}
function updateUrl(){
  if(!maze)return;
  const p=new URLSearchParams({v:COMBINED_VERSION,w:maze.width,h:maze.height,l:maze.levels,
    s:maze.seed,stairs:maze.stairWeight,choices:maze.choices,floor});
  history.replaceState(null,"",location.pathname+"?"+p);
  $("walkLink").href="../walk/?"+p;
}
function bounds(){
  x0=clamp(x0,0,Math.max(0,maze.width-Math.min(zoom,maze.width)));
  y0=clamp(y0,0,Math.max(0,maze.height-Math.min(zoom,maze.height)));
}
function center(p){
  x0=Math.floor(p.x-zoom/2);y0=Math.floor(p.y-zoom/2);
  bounds();render();
}
function render(){
  if(!maze)return;
  bounds();
  const data=floorPlanSvg(maze,floor,{x0,y0,width:Math.min(zoom,maze.width),height:Math.min(zoom,maze.height),
    highlight:runs[selected]||null,showLabels:$("labels").checked});
  lastSvg=data.svg;$("map").innerHTML=data.svg;
  $("floor").textContent="Level "+floor+" / "+(maze.levels-1);
  $("previous").disabled=floor===0;$("next").disabled=floor===maze.levels-1;
  $("zoomValue").textContent=String(zoom);
  const s=data.stats,f=maze.floorMetrics[floor];
  $("counts").textContent="Visible: "+s.flats+" flat corridor edges · "+s.up+" uphill entrances · "+s.down+
    " downhill entrances. This floor: "+maze.choiceDiagnostics[floor].after.junctions+
    " planar junctions (original: "+maze.choiceDiagnostics[floor].before.junctions+"), "+f.median+
    "-cell median dead-end branch. Full 3D graph: "+maze.stairShare+"% stair edges, "+maze.stairs+" stair segments, "+
    maze.overpassCount+" hidden passages passing over/under stairs, "+maze.routeLength+" minimum steps to the exit.";
  $("map").setAttribute("aria-label","Floor "+floor+": "+s.flats+" level connections, "+s.up+
    " ascending and "+s.down+" descending stairs; drag to pan.");
  updateUrl();
}
function showRun(){
  selected=clamp(Number($("runs").value)||0,0,Math.max(0,runs.length-1));
  const run=runs[selected]||null;
  $("profile").innerHTML=runProfileSvg(run);
  $("runDetail").textContent=run?"Actual "+run.steps+"-step uninterrupted climb from ("+
    [run.from.x,run.from.y,run.from.z].join(",")+") to ("+[run.to.x,run.to.y,run.to.z].join(",")+
    "). Horizontal distance "+(run.steps*PITCH)+" m; elevation "+(run.rise*STORY)+
    " m. There are no mandatory landings on intermediate floors.":"No flights in this world.";
}
function generate(){
  try{
    const opts=options();
    if(!opts.seed)throw new Error("Seed is required");
    const next=new ConnectedDetours3D(opts);
    maze=next;runs=next.runs();floor=next.start.z;
    zoom=Number($("zoom").value);x0=next.start.x-Math.floor(zoom/2);y0=next.start.y-Math.floor(zoom/2);
    $("error").hidden=true;
    $("runs").replaceChildren();
    for(let i=0;i<Math.min(60,runs.length);i++){
      const run=runs[i],opt=document.createElement("option");
      opt.value=String(i);opt.textContent=(i+1)+". "+run.steps+" climbs · z"+run.from.z+
        " → z"+run.to.z+" · ("+run.from.x+","+run.from.y+")";
      $("runs").append(opt);
    }
    selected=0;$("runs").value="0";showRun();
    $("summary").textContent=maze.usedCount.toLocaleString()+" walkable positions · "+
      maze.junctions+" branching junctions · "+maze.stairShare+"% stairs";
    $("runDetail").textContent+=" Across the world: "+runs.filter(r=>r.steps>=3).length+
      " uninterrupted climbs of at least three floors; original median dead-end depth per floor "+
      maze.floorMetrics.map(x=>x.median).join(" / ")+".";
    render();
  }catch(error){$("error").hidden=false;$("error").textContent=error.message;}
}
function download(contents,filename,type){
  const url=URL.createObjectURL(new Blob([contents],{type})),a=document.createElement("a");
  a.href=url;a.download=filename;document.body.append(a);a.click();a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1500);
}
$("setup").addEventListener("submit",e=>{e.preventDefault();generate();});
$("reroll").addEventListener("click",()=>{
  const words=new Uint32Array(2);crypto.getRandomValues(words);
  $("seed").value=words[0].toString(36)+"-"+words[1].toString(36);generate();
});
$("previous").addEventListener("click",()=>{floor=clamp(floor-1,0,maze.levels-1);render();});
$("next").addEventListener("click",()=>{floor=clamp(floor+1,0,maze.levels-1);render();});
$("atStart").addEventListener("click",()=>{floor=maze.start.z;center(maze.start);});
$("atExit").addEventListener("click",()=>{floor=maze.exit.z;center(maze.exit);});
$("zoom").addEventListener("change",()=>{
  const cx=x0+zoom/2,cy=y0+zoom/2;
  zoom=Number($("zoom").value);x0=Math.floor(cx-zoom/2);y0=Math.floor(cy-zoom/2);render();
});
$("fit").addEventListener("click",()=>{
  zoom=clamp(8*Math.ceil(Math.max(maze.width,maze.height)/8),16,64);
  $("zoom").value=String(zoom);x0=0;y0=0;render();
});
$("labels").addEventListener("change",()=>render());
$("runs").addEventListener("change",()=>{showRun();render();});
$("focus").addEventListener("click",()=>{
  const r=runs[selected];if(r){floor=r.from.z;center(r.from);}
});
$("save").addEventListener("click",()=>download(
  floorPlanSvg(maze,floor,{width:maze.width,height:maze.height,highlight:runs[selected]||null,showLabels:$("labels").checked}).svg,
  "detours-3d-"+maze.seed.replace(/[^a-z0-9_-]/gi,"_").slice(0,48)+"-z"+floor+".svg","image/svg+xml"
));
$("geometry").addEventListener("click",()=>{
  const meta={version:COMBINED_VERSION,seed:maze.seed,size:[maze.width,maze.height,maze.levels],
    stairFrequency:maze.stairWeight,start:maze.start,exit:maze.exit,shortestPath:maze.routeLength,
    scale:{pitch:PITCH,story:STORY,width:WIDTH,headroom:HEADROOM,slab:SLAB},
    originalFloorMetrics:maze.floorMetrics,stairEdgePercentage:maze.stairShare,edges:maze.geometry(),stairNodes:maze.stairSurfaces(),overpasses:maze.overpasses(),
    naturalMultistoryStairs:runs.filter(r=>r.steps>=3)};
  download(JSON.stringify(meta),"connected-detours-3d-"+maze.seed.replace(/[^a-z0-9_-]/gi,"_")+".json","application/json");
});
$("share").addEventListener("click",async()=>{
  try{await navigator.clipboard.writeText(location.href);$("share").textContent="Copied!";
    setTimeout(()=>$("share").textContent="Copy seed link",1600);}
  catch{$("error").hidden=false;$("error").textContent="Copy the current URL from the address bar.";}
});
const map=$("map");
map.addEventListener("pointerdown",e=>{if(!maze)return;pointer={x:e.clientX,y:e.clientY};map.setPointerCapture(e.pointerId);});
map.addEventListener("pointerup",e=>{
  if(!pointer||!maze)return;
  const cols=Math.min(zoom,maze.width),rect=map.getBoundingClientRect();
  const px=Math.max(1,rect.width/cols);
  x0+=Math.round((pointer.x-e.clientX)/px);y0+=Math.round((pointer.y-e.clientY)/px);
  pointer=null;render();
});
map.addEventListener("pointercancel",()=>{pointer=null;});
map.addEventListener("keydown",e=>{
  const offsets={ArrowLeft:[-4,0],ArrowRight:[4,0],ArrowUp:[0,-4],ArrowDown:[0,4]};
  if(!Object.hasOwn(offsets,e.key))return;
  e.preventDefault();x0+=offsets[e.key][0];y0+=offsets[e.key][1];render();
});
const p=new URLSearchParams(location.search);
for(const [key,id] of [["w","width"],["h","height"],["l","levels"],["s","seed"],["stairs","stairWeight"],["choices","choices"]])
  if(p.has(key))$(id).value=id==="stairWeight"?String(clamp(Number(p.get(key))||2,1,8)):p.get(key);
if((p.has("s")||p.get("v")==="detours-elevation-v1")&&!p.has("choices"))$("choices").value="0";
generate();
if(maze&&p.has("floor")){floor=clamp(Number(p.get("floor"))||0,0,maze.levels-1);render();}
