import assert from "node:assert/strict";
import {ElevationMaze,vertical} from "../elevation/stairs.mjs";
import {ConnectedDetours3D} from "../elevation/combined.mjs";
import {floorPlanSvg} from "../elevation/view.mjs";

function fixture(edges) {
  const m=new ElevationMaze({width:8,height:8,levels:4,seed:"LANDING-RULE",generate:false});
  for(const [a,b,code] of edges){const i=m.index(...a),j=m.index(...b);m.used[i]=1;m.used[j]=1;m.join(i,j,code);}
  return m;
}
const p0=[1,2,0],p1=[2,2,1],p2=[3,2,2],south=[2,3,2],side=[2,3,1];
{
  const m=fixture([[p0,p1,5],[p1,p2,5]]),i=m.index(...p1);
  assert.equal(m.through(i),true,"two collinear climbs are one continuous flight");
  assert.equal(m.stairNodeKind(i),"continuous-flight","not a landing at a floor boundary");
  const surface=m.stairSurfaces();
  assert.deepEqual(surface.continuous,[{x:2,y:2,z:1}]);
  assert.ok(!surface.landings.some(x=>x.x===2&&x.y===2&&x.z===1));
  const view=floorPlanSvg(m,1);
  assert.equal(view.stats.through,1);
  assert.equal(view.stats.landings,0);
  assert.ok(!view.svg.includes('class="landing-marker"'),"no phantom platform drawn on stair through-flight");
  assert.equal(m.runs()[0].steps,2);
}
{
  const m=fixture([[p0,p1,5],[p1,south,6]]),i=m.index(...p1);
  assert.equal(m.through(i),false,"turn while climbing requires a real transition");
  assert.equal(m.stairNodeKind(i),"landing","a changed heading is a valid landing");
  assert.equal(floorPlanSvg(m,1).stats.landings,1);
}
{
  const m=fixture([[p0,p1,5],[p1,p2,5],[p1,side,2]]),i=m.index(...p1);
  assert.equal(m.through(i),false,"a real branch must not be mistaken for a through-flight");
  assert.equal(m.stairNodeKind(i),"landing","a meaningful level-floor choice is a valid landing");
  assert.equal(floorPlanSvg(m,1).stats.landings,1);
}
{
  const m=fixture([[p0,p1,5],[p1,[3,2,1],1]]);
  assert.equal(m.stairNodeKind(m.index(...p1)),"landing","stairs joining a flat corridor need a transition");
}
function redundantStraightFlatStep(m) {
  let count=0;
  for(let i=0;i<m.volume;i++){
    if(!m.used[i])continue;
    const links=m.links(i);
    if(links.length!==2)continue;
    const incline=links.find(e=>vertical(e.code)!==0),flat=links.find(e=>vertical(e.code)===0);
    if(!incline||!flat)continue;
    const other=m.links(flat.to);
    if(other.length!==2||!other.some(e=>e.to===i))continue;
    const nextIncline=other.find(e=>vertical(e.code)!==0);
    if(!nextIncline)continue;
    const [a,b,c,d]=[m.loc(incline.to),m.loc(i),m.loc(flat.to),m.loc(nextIncline.to)];
    const step=(x,y)=>[y.x-x.x,y.y-x.y];
    const [first,middle,last]=[step(a,b),step(b,c),step(c,d)];
    if(b.z-a.z===d.z-c.z&&b.z!==a.z&&first[0]===middle[0]&&middle[0]===last[0]&&
      first[1]===middle[1]&&middle[1]===last[1])count++;
  }
  return count;
}
{
  // A deliberately bad artificial example proves the detector isn't a tautology.
  const bad=fixture([[p0,p1,5],[p1,[3,2,1],1],[[3,2,1],[4,2,2],5]]);
  assert.equal(redundantStraightFlatStep(bad),1,"detect a pointless stair-flat-stair straight interruption");
}
for(const seed of ["LOST-6549","NO-PREFABS","CONNECTED-DETOURS","TURN-AT-A-LANDING","SOME-OTHER-PLACE"]){
  for(const [width,height,levels] of [[8,8,2],[20,24,4],[28,28,6]]){
    const m=new ConnectedDetours3D({width,height,levels,seed,stairWeight:2});
    assert.equal(redundantStraightFlatStep(m),0,
      "no straight stair-flat-stair pause that offers no choice or direction change");
    const surfaces=m.stairSurfaces();
    for(const p of surfaces.continuous){
      const i=m.index(p.x,p.y,p.z);
      assert.equal(m.stairNodeKind(i),"continuous-flight");
      assert.ok(m.through(i),"straight ramps must not create a landing");
      assert.ok(m.flightInterior[i],"uninterrupted stairs must be marked as protected through-flight");
    }
    for(const p of surfaces.landings)assert.equal(m.stairNodeKind(m.index(p.x,p.y,p.z)),"landing");
    console.log("PASS LANDINGS",seed,width+"x"+height+"x"+levels,
      "real landings",surfaces.landings.length,"uninterrupted",surfaces.continuous.length);
  }
}
console.log("Stair landing semantics validated: no pointless platforms on uninterrupted straight climbs.");
