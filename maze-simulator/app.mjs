import {Maze,STYLES} from "./maze-core.mjs";
import {floorSvg} from "./maze-map.mjs";
const $=id=>document.getElementById(id),clamp=(v,min,max)=>Math.max(min,Math.min(max,v));
let maze,level=0,x0=0,y0=0,view=32,lastSvg="";
function inputs(){
  return {width:Number($("width").value),height:Number($("height").value),
    levels:Number($("levels").value),seed:$("seed").value.trim(),style:$("style").value};
}
function clampOrigin(){
  x0=clamp(x0,0,Math.max(0,maze.width-Math.min(view,maze.width)));
  y0=clamp(y0,0,Math.max(0,maze.height-Math.min(view,maze.height)));
}
function center(loc){
  x0=loc.x-Math.floor(Math.min(view,maze.width)/2);
  y0=loc.y-Math.floor(Math.min(view,maze.height)/2);
  clampOrigin();render();
}
function writeUrl(){
  const p=new URLSearchParams();
  p.set("w",maze.width);p.set("h",maze.height);p.set("floors",maze.levels);
  p.set("seed",maze.seed);p.set("style",maze.style);p.set("floor",level+1);
  history.replaceState(null,"",location.pathname+"?"+p.toString());
}
function render(){
  clampOrigin();
  const data=floorSvg(maze,{floor:level,x0,y0,width:Math.min(view,maze.width),height:Math.min(view,maze.height)});
  lastSvg=data.svg;$("map-wrap").innerHTML=lastSvg;
  $("floor-label").textContent=(level+1)+" / "+maze.levels;
  $("floor-back").disabled=level<=0;$("floor-next").disabled=level>=maze.levels-1;
  $("span-value").textContent=String(view);
  $("fit").disabled=Math.max(maze.width,maze.height)>96;
  $("fit").title=$("fit").disabled?"For huge mazes, use the view-width slider to stream sections.":"Show the entire floor.";
  const s=data.stats;
  $("stats").textContent="Visible "+data.width+" × "+data.height+" cells · "+s.dead+" dead ends · "
    +s.turns+" turns · "+s.straight+" straight passages · "+s.junctions+" junctions · "+s.vertical+" stair connections";
  $("size-label").textContent=maze.width+" × "+maze.height+" × "+maze.levels;
  writeUrl();
}
function compare(){
  const root=$("examples");root.replaceChildren();
  for(const [key,profile] of Object.entries(STYLES)){
    const test=new Maze({...inputs(),style:key,cacheLimit:24});
    const size=24,ww=Math.min(test.width,size),hh=Math.min(test.height,size);
    const bx=clamp(test.start.x-Math.floor(ww/2),0,test.width-ww);
    const by=clamp(test.start.y-Math.floor(hh/2),0,test.height-hh);
    const map=floorSvg(test,{floor:test.start.z,x0:bx,y0:by,width:ww,height:hh,small:true});
    const button=document.createElement("button");button.type="button";button.className="example";
    if(maze.style===key)button.classList.add("active");
    const title=document.createElement("strong");title.textContent=profile.name;
    const picture=document.createElement("div");picture.className="example-map";picture.innerHTML=map.svg;
    const detail=document.createElement("span");
    detail.textContent=map.stats.dead+" dead ends · "+map.stats.junctions+" junctions in this sample";
    button.append(title,picture,detail);
    button.addEventListener("click",()=>{$("style").value=key;generate(false);});
    root.append(button);
  }
}
function generate(resetFloor=true){
  try{
    const opts=inputs();if(!opts.seed)throw new Error("Enter a seed");
    maze=new Maze(opts);$("error").hidden=true;
    level=resetFloor?maze.start.z:clamp(level,0,maze.levels-1);
    view=Number($("span").value);x0=maze.start.x-Math.floor(view/2);y0=maze.start.y-Math.floor(view/2);
    render();compare();
  }catch(e){$("error").textContent=e.message;$("error").hidden=false;}
}
$("settings").addEventListener("submit",e=>{e.preventDefault();generate();});
$("reroll").addEventListener("click",()=>{
  const b=new Uint32Array(2);crypto.getRandomValues(b);
  $("seed").value=b[0].toString(36)+"-"+b[1].toString(36);generate();
});
$("floor-back").addEventListener("click",()=>{level=clamp(level-1,0,maze.levels-1);render();});
$("floor-next").addEventListener("click",()=>{level=clamp(level+1,0,maze.levels-1);render();});
$("home").addEventListener("click",()=>{level=maze.start.z;center(maze.start);});
$("goal").addEventListener("click",()=>{level=maze.exit.z;center(maze.exit);});
$("span").addEventListener("change",()=>{
  const cx=x0+view/2,cy=y0+view/2;view=Number($("span").value);
  x0=Math.floor(cx-view/2);y0=Math.floor(cy-view/2);render();
});
$("fit").addEventListener("click",()=>{
  view=clamp(Math.max(maze.width,maze.height),16,96);
  $("span").value=String(view);
  x0=0;y0=0;render();
});
$("export").addEventListener("click",()=>{
  const blob=new Blob([lastSvg],{type:"image/svg+xml;charset=utf-8"}),url=URL.createObjectURL(blob);
  const a=document.createElement("a");a.href=url;a.download="maze-"+maze.seed.replace(/[^a-z0-9_-]/gi,"_").slice(0,32)+"-floor-"+(level+1)+".svg";
  document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),2000);
});
$("share").addEventListener("click",async()=>{
  try{await navigator.clipboard.writeText(location.href);$("share").textContent="Copied!";
    setTimeout(()=>$("share").textContent="Copy link",1400);
  }catch{$("error").textContent="Copy the URL from your address bar.";$("error").hidden=false;}
});
const panel=$("map-wrap");
let pointer=null;
panel.addEventListener("pointerdown",e=>{
  pointer={x:e.clientX,y:e.clientY};
  panel.setPointerCapture(e.pointerId);
});
panel.addEventListener("pointerup",e=>{
  if(!pointer)return;
  const rect=panel.getBoundingClientRect(),cells=Math.min(view,maze.width);
  const px=Math.max(1,rect.width/cells);
  const dx=Math.round((pointer.x-e.clientX)/px),dy=Math.round((pointer.y-e.clientY)/px);
  pointer=null;if(dx||dy){x0+=dx;y0+=dy;render();}
});
panel.addEventListener("pointercancel",()=>{pointer=null;});
panel.addEventListener("keydown",e=>{
  const steps={ArrowLeft:[-5,0],ArrowRight:[5,0],ArrowUp:[0,-5],ArrowDown:[0,5]};
  if(!Object.hasOwn(steps,e.key))return;
  e.preventDefault();x0+=steps[e.key][0];y0+=steps[e.key][1];render();
});
const params=new URLSearchParams(location.search);
for(const [key,id] of [["w","width"],["h","height"],["floors","levels"],["seed","seed"],["style","style"]]){
  if(params.has(key))$(id).value=params.get(key);
}
generate();
if(maze&&params.has("floor")){
  level=clamp(Number(params.get("floor"))-1||0,0,maze.levels-1);render();
}
