import {ElevationMaze,VERSION,PITCH,STORY,WIDTH,HEADROOM,SLAB} from "./stairs.mjs";
import {floorPlanSvg,runProfileSvg} from "./view.mjs";
const $=id=>document.getElementById(id),clamp=(v,low,high)=>Math.max(low,Math.min(high,v));
let maze=null,level=0,runs=[],selected=0,lastSvg="";
function inputs(){return {width:Number($("width").value),height:Number($("height").value),levels:Number($("levels").value),stairWeight:Number($("stairWeight").value),seed:$("seed").value.trim()};}
function url(){
  if(!maze)return;
  const p=new URLSearchParams({v:VERSION,w:maze.width,h:maze.height,l:maze.levels,s:maze.seed,stairs:maze.stairWeight,floor:level.toString()});
  history.replaceState(null,"",location.pathname+"?"+p);
}
function showRun(){
  selected=clamp(Number($("runs").value)||0,0,Math.max(0,runs.length-1));
  const run=runs[selected]||null;
  $("profile").innerHTML=runProfileSvg(run);
  $("runDetail").textContent=run?
    "Generated continuous flight: "+run.steps+" stair segments, from ("+[run.from.x,run.from.y,run.from.z].join(",")+") to ("+
    [run.to.x,run.to.y,run.to.z].join(",")+"). Horizontal travel "+(run.steps*PITCH)+" m; elevation "+(run.rise*STORY)+
    " m. Every interior point is an uninterrupted stair tread, NOT a compulsory landing."
    :"No long uninterrupted stair flight was produced for this seed.";
}
function draw(){
  if(!maze)return;
  const data=floorPlanSvg(maze,level,{highlight:runs[selected]||null});
  lastSvg=data.svg;$("map").innerHTML=data.svg;
  $("floor").textContent="Level "+level+" / "+(maze.levels-1);
  $("previous").disabled=level===0;$("next").disabled=level===maze.levels-1;
  $("counts").textContent="On this level: "+data.stats.flats+" horizontal connections, "+data.stats.up+
    " ascending entrances, "+data.stats.down+" descending entrances, "+data.stats.landings+
    " landing/junction cells, "+data.stats.through+" uninterrupted stair positions.";
  url();
}
function setupRuns(){
  const select=$("runs");select.replaceChildren();
  for(let i=0;i<Math.min(50,runs.length);i++){
    const run=runs[i],o=document.createElement("option");
    o.value=String(i);
    o.textContent=(i+1)+". "+run.steps+" climbs · z"+run.from.z+" → z"+run.to.z+
      " · ("+run.from.x+","+run.from.y+")";
    select.append(o);
  }
  selected=0;select.value="0";showRun();
}
function generate(){
  try{
    const opts=inputs();if(!opts.seed)throw new Error("A nonempty seed is required.");
    const next=new ElevationMaze(opts);
    maze=next;runs=maze.runs();level=maze.start.z;
    $("error").hidden=true;setupRuns();draw();
    const long=runs.filter(x=>x.steps>=3).length;
    const junctions=Array.from(maze.used).reduce((s,u,i)=>s+(u&&maze.links(i).length>=3?1:0),0);
    $("summary").textContent=maze.usedCount.toLocaleString()+" walkable nodes · "+maze.stairs+
      " climbing/descending segments";
    $("runDetail").textContent+=" Across this world: "+long+
      " uninterrupted flights span at least three levels. There are "+junctions+
      " junctions and "+maze.loops+" extra long detours.";
  }catch(error){$("error").hidden=false;$("error").textContent=error.message;}
}
function download(text,name,type){
  const url=URL.createObjectURL(new Blob([text],{type})),a=document.createElement("a");
  a.href=url;a.download=name;document.body.append(a);a.click();a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1500);
}
$("setup").addEventListener("submit",e=>{e.preventDefault();generate();});
$("reroll").addEventListener("click",()=>{
  const v=new Uint32Array(2);crypto.getRandomValues(v);
  $("seed").value=v[0].toString(36)+"-"+v[1].toString(36);generate();
});
$("previous").addEventListener("click",()=>{level=Math.max(0,level-1);draw();});
$("next").addEventListener("click",()=>{level=Math.min(maze.levels-1,level+1);draw();});
$("atStart").addEventListener("click",()=>{level=maze.start.z;draw();});
$("atExit").addEventListener("click",()=>{level=maze.exit.z;draw();});
$("runs").addEventListener("change",()=>{showRun();draw();});
$("focus").addEventListener("click",()=>{if(runs[selected]){level=runs[selected].from.z;draw();}});
$("save").addEventListener("click",()=>download(lastSvg,"natural-stairs-"+maze.seed.replace(/[^a-z0-9_-]/gi,"_")+"-z"+level+".svg","image/svg+xml"));
$("geometry").addEventListener("click",()=>{
  const info={version:VERSION,seed:maze.seed,dimensions:[maze.width,maze.height,maze.levels],
    scale:{horizontalStepMeters:PITCH,levelHeightMeters:STORY,corridorWidthMeters:WIDTH,headroomMeters:HEADROOM,slabMeters:SLAB},
    start:maze.start,exit:maze.exit,routeLength:maze.routeLength,
    navigableCells:maze.usedCount,edges:maze.geometry(),unbrokenStairRuns:runs};
  download(JSON.stringify(info),"natural-stairs-"+maze.seed.replace(/[^a-z0-9_-]/gi,"_")+".json","application/json");
});
$("share").addEventListener("click",async()=>{
  try{await navigator.clipboard.writeText(location.href);$("share").textContent="Copied";
    setTimeout(()=>$("share").textContent="Copy seed link",1300);
  }catch{$("error").hidden=false;$("error").textContent="Copy the page URL from your browser's address bar.";}
});
const params=new URLSearchParams(location.search);
for(const [field,id] of [["w","width"],["h","height"],["l","levels"],["s","seed"],["stairs","stairWeight"]])
  if(params.has(field))$(id).value=params.get(field);
generate();
if(maze&&params.has("floor")){level=clamp(Number(params.get("floor"))||0,0,maze.levels-1);draw();}
