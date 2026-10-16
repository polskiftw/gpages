// First-person exploration of the *exact* Connected Detours 3D graph.
// Collision and geometry both query the same links; there is no independent
// collision maze, and streamer chunks never affect seeded topology.
import {ConnectedDetours3D} from "../elevation/combined.mjs";
import {PITCH,STORY} from "../elevation/stairs.mjs";
import {initialPose,movePlayer,distanceToExit,EYE_HEIGHT} from "./navigation.mjs";
import {buildMesh} from "./mesh.mjs";
import {MazeWebGL} from "./webgl.mjs";
const $=id=>document.getElementById(id);
const clamp=(a,low,high)=>Math.max(low,Math.min(high,a));
const keys=new Set(),hold=new Set();
let maze=null,pose=null,renderer=null,won=false,distance=0,elapsed=0;
let meshKey="",meshStats=null,frames=0,lastTime=0,debugTick=0,drag=null,loopStarted=false;
function opts(){return {seed:$("seed").value.trim(),width:Number($("width").value),height:Number($("height").value),
  levels:Number($("levels").value),stairWeight:Number($("stairWeight").value),choices:Number($("choices").value)};}
function query(){
  const p=new URLSearchParams({s:maze.seed,w:maze.width,h:maze.height,l:maze.levels,stairs:maze.stairWeight,choices:maze.choices});
  history.replaceState(null,"",location.pathname+"?"+p);
  $("mapLink").href="../unified/?"+p;
}
function status(text,error=false){
  $("error").hidden=!error;
  if(error)$("error").textContent=text;
  else $("message").textContent=text;
}
function reset(){
  if(!maze)return;
  pose=initialPose(maze);distance=0;elapsed=0;won=false;meshKey="";
  keys.clear();hold.clear();$("win").hidden=true;
  status("Find the exit. Every passage is enclosed.");
  rebuild();
}
function generate(){
  try{
    if(!opts().seed)throw new Error("Please provide a seed.");
    const next=new ConnectedDetours3D(opts());
    // Do not discard the currently playable world unless generation succeeds.
    if(!renderer)renderer=new MazeWebGL($("view"));
    maze=next;$("error").hidden=true;
    const base=maze.choiceDiagnostics.reduce((sum,x)=>sum+x.before.junctions,0);
    const current=maze.choiceDiagnostics.reduce((sum,x)=>sum+x.after.junctions,0);
    const mode=maze.choices===0?"Original":maze.choices===1?"More":"Many";
    $("layoutStats").textContent=mode+" branching · "+current+" junctions"+
      (maze.choices?" (original: "+base+") · "+maze.extraChoices+" new long detours":" · original routes")+
      " · "+maze.stairShare+"% stairs";
    reset();query();
    $("stats").textContent="Seed "+maze.seed+" · "+maze.levels+" floors";
    if(!loopStarted){loopStarted=true;requestAnimationFrame(frame);}
  }catch(e){status(e.message,true);}
}
function rebuild(){
  if(!maze||!renderer)return;
  // Pure rendering cache keyed to 8x8 cell regions; the underlying global
  // topology never knows about these boundaries. Keep ample fog margin.
  const cx=Math.floor(pose.x/(PITCH*8)),cy=Math.floor(pose.z/(PITCH*8)),cz=Math.round(pose.feet/STORY);
  const key=cx+":"+cy+":"+cz;
  if(key===meshKey)return;
  const center={x:cx*8+4,y:cy*8+4,z:cz};
  const mesh=buildMesh(maze,{center,radius:13,floorRadius:2});
  renderer.setMesh(mesh);meshKey=key;meshStats=mesh.stats;
}
function active(code){return hold.has(code)||keys.has(code);}
function direction(){
  let forward=Number(active("w")||active("arrowup")||active("forward"))-
    Number(active("s")||active("arrowdown")||active("back"));
  let strafe=Number(active("d")||active("right"))-Number(active("a")||active("left"));
  const size=Math.hypot(forward,strafe)||1;forward/=size;strafe/=size;
  return {forward,strafe};
}
function frame(timestamp){
  requestAnimationFrame(frame);
  if(!maze||!pose||!renderer)return;
  const dt=lastTime?Math.min(0.045,Math.max(0,(timestamp-lastTime)/1000)):0;
  lastTime=timestamp;
  if(!document.hidden&&!won){
    elapsed+=dt;
    let turn=Number(active("arrowright")||active("turnRight"))-
      Number(active("arrowleft")||active("turnLeft"));
    pose.yaw+=turn*dt*1.75;
    const movement=direction(),speed=keys.has("shift")?4.9:2.7,
      cy=Math.cos(pose.yaw),sy=Math.sin(pose.yaw);
    const vx=(sy*movement.forward+cy*movement.strafe)*speed*dt,
      vz=(-cy*movement.forward+sy*movement.strafe)*speed*dt;
    if(vx||vz){
      const oldX=pose.x,oldZ=pose.z;
      pose=movePlayer(maze,pose,vx,vz);
      distance+=Math.hypot(pose.x-oldX,pose.z-oldZ);
      rebuild();
    }
    if(distanceToExit(maze,pose)<0.70){
      won=true;$("win").hidden=false;
      $("finish").textContent="Seed "+maze.seed+". You explored "+Math.round(distance)+" metres over "+
        Math.round(elapsed)+" seconds and found the correct exit.";
      status("Exit reached!");
      if(document.pointerLockElement===$("view"))document.exitPointerLock();
    }
  }
  renderer.draw(pose,EYE_HEIGHT);
  if(timestamp-debugTick>350){
    debugTick=timestamp;
    $("stats").textContent=$("coordinates").checked
      ?"X "+(pose.x/PITCH).toFixed(1)+"  Y "+(pose.z/PITCH).toFixed(1)+
        "  elevation "+(pose.feet/STORY).toFixed(2)+" · "+Math.round(distance)+" m · "+
        (meshStats?meshStats.triangles.toLocaleString():0)+" tris"
      :"Seed "+maze.seed+" · "+Math.round(distance)+" m walked";
  }
}
$("setup").addEventListener("submit",e=>{e.preventDefault();generate();});
$("restart").addEventListener("click",reset);
$("replay").addEventListener("click",()=>{
  const a=new Uint32Array(2);crypto.getRandomValues(a);
  $("seed").value=a[0].toString(36)+"-"+a[1].toString(36);generate();
});
$("fullscreen").addEventListener("click",async()=>{
  try{
    const game=$("game");
    if(document.fullscreenElement)await document.exitFullscreen();
    else if(game.requestFullscreen)await game.requestFullscreen();
    else status("Fullscreen isn't supported in this browser. The regular viewer still works.",true);
  }catch(e){status("Fullscreen: "+e.message,true);}
});
$("share").addEventListener("click",async()=>{
  try{await navigator.clipboard.writeText(location.href);$("share").textContent="Copied";
    setTimeout(()=>$("share").textContent="Copy seed link",1300);}
  catch{status("Copy the URL from your browser's address bar.",true);}
});
const typing=()=>{const el=document.activeElement;return ["TEXTAREA","SELECT"].includes(el?.tagName)||
  el?.tagName==="INPUT"&&el.type!=="checkbox"||el?.tagName==="BUTTON";};
window.addEventListener("keydown",e=>{
  if(typing(e))return;
  const k=e.key.toLowerCase();
  if(["w","a","s","d","arrowup","arrowdown","arrowleft","arrowright","shift"].includes(k)){
    keys.add(k);e.preventDefault();
  }
});
window.addEventListener("keyup",e=>keys.delete(e.key.toLowerCase()));
window.addEventListener("blur",()=>{keys.clear();hold.clear();});
document.addEventListener("visibilitychange",()=>{lastTime=0;});
const canvas=$("view");
canvas.addEventListener("click",e=>{
  if(e.pointerType==="touch"||e.sourceCapabilities?.firesTouchEvents)return;
  if(canvas.requestPointerLock&&document.pointerLockElement!==canvas)canvas.requestPointerLock();
});
document.addEventListener("mousemove",e=>{
  if(document.pointerLockElement===canvas&&pose&&!won){
    pose.yaw+=e.movementX*0.0025;
    pose.pitch=clamp(pose.pitch-e.movementY*0.0022,-1.20,1.20);
  }
});
canvas.addEventListener("pointerdown",e=>{
  if(e.pointerType==="mouse"||!pose)return;
  drag={id:e.pointerId,x:e.clientX,y:e.clientY};
  canvas.setPointerCapture(e.pointerId);
});
canvas.addEventListener("pointermove",e=>{
  if(!pose||!drag||drag.id!==e.pointerId)return;
  pose.yaw+=(e.clientX-drag.x)*0.005;
  pose.pitch=clamp(pose.pitch-(e.clientY-drag.y)*0.004,-1.20,1.20);
  drag.x=e.clientX;drag.y=e.clientY;
});
for(const name of ["pointerup","pointercancel"])canvas.addEventListener(name,e=>{
  if(drag?.id===e.pointerId)drag=null;
});
for(const button of document.querySelectorAll("[data-hold]")){
  const name=button.dataset.hold;
  button.addEventListener("pointerdown",e=>{
    e.preventDefault();button.setPointerCapture(e.pointerId);
    hold.add(name);button.classList.add("is-held");
  });
  for(const event of ["pointerup","pointercancel","lostpointercapture"])
    button.addEventListener(event,()=>{hold.delete(name);button.classList.remove("is-held");});
}
const params=new URLSearchParams(location.search);
for(const [q,id] of [["s","seed"],["w","width"],["h","height"],["l","levels"],["stairs","stairWeight"],["choices","choices"]])
  if(params.has(q))$(id).value=params.get(q);
// Existing old URLs without a choices parameter reproduce the old v1
// labyrinth exactly. A fresh visit starts with the denser new default.
if(params.has("s")&&!params.has("choices"))$("choices").value="0";
generate();
