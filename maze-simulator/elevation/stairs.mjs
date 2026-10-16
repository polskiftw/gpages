// Experimental elevation-aware maze math. No prefab stairs, no chunk topology.
// A path step changes (x,y,z) by (+/-1,0,0), (0,+/-1,0), or
// (+/-1,0,+/-1) / (0,+/-1,+/-1). Every step moves horizontally.
export const PITCH=7, STORY=3.5, WIDTH=1.5, HEADROOM=2.1, SLAB=0.25;
export const DIRECTIONS=[{dx:0,dy:-1,name:"N"},{dx:1,dy:0,name:"E"},{dx:0,dy:1,name:"S"},{dx:-1,dy:0,name:"W"}];
export const VERSION="elevation-lattice-2";
export const opposite=code=>{const d=(code+2)%4;return code<4?d:code<8?d+8:d+4;};
export const vertical=code=>code<4?0:code<8?1:-1;
export const direction=code=>code%4;
export const popcount=mask=>{let n=0;for(;mask;mask&=mask-1)n++;return n;};
function mix(x){x=(x^(x>>>16))>>>0;x=Math.imul(x,0x7feb352d);x=(x^(x>>>15))>>>0;x=Math.imul(x,0x846ca68b);return (x^(x>>>16))>>>0;}
function seedValue(seed){let h=2166136261;for(const ch of seed)h=Math.imul(h^ch.charCodeAt(0),16777619)>>>0;return mix(h);}
function makeRandom(seed){let x=seed>>>0;return max=>{x=(x+0x6d2b79f5)>>>0;let t=Math.imul(x^(x>>>15),1|x);t^=t+Math.imul(t^(t>>>7),61|t);return ((t^(t>>>14))>>>0)%max;};}

export class ElevationMaze{
  constructor({width=28,height=28,levels=6,seed="LOST-6549",stairWeight=2,generate=true}={}){
    for(const [name,value,min,max] of [["width",width,6,64],["height",height,6,64],["levels",levels,2,12],["stairWeight",stairWeight,1,8]])
      if(!Number.isSafeInteger(value)||value<min||value>max)throw new RangeError(name+" must be "+min+".."+max);
    this.width=width;this.height=height;this.levels=levels;this.area=width*height;this.volume=this.area*levels;
    this.seed=String(seed);this.stairWeight=stairWeight;
    this.salt=seedValue([seed,width,height,levels,stairWeight,VERSION].join("|"));
    this.bits=new Uint16Array(this.volume);this.used=new Uint8Array(this.volume);
    this.reservations=new Map();this.edges=0;this.stairs=0;this.loops=0;this.pruned=0;
    // Density is a hard MAXIMUM, not a relative RNG weight. Each adjacent
    // floor pair can carry at most 1..8% of one floor's cells as ramp edges.
    this.maxRampsPerBoundary=Math.max(1,Math.ceil(this.area*stairWeight/100));
    this.stairCountByBoundary=new Uint16Array(levels-1);
    if(generate)this.make();
  }
  hash(a,b=0,c=0){return mix(mix(mix(this.salt^mix(a))^mix(b))^mix(c));}
  index(x,y,z){return z*this.area+y*this.width+x;}
  loc(i){const xy=i%this.area;return {x:xy%this.width,y:Math.floor(xy/this.width),z:Math.floor(i/this.area)};}
  neighbor(i,code){
    const {x,y,z}=this.loc(i),d=DIRECTIONS[direction(code)],zz=z+vertical(code);
    const xx=x+d.dx,yy=y+d.dy;
    return xx>=0&&xx<this.width&&yy>=0&&yy<this.height&&zz>=0&&zz<this.levels?this.index(xx,yy,zz):-1;
  }
  links(i){
    const out=[],mask=this.bits[i];
    for(let code=0;code<12;code++)if(mask&(1<<code)){
      const j=this.neighbor(i,code);
      if(j<0)throw new Error("Passage escapes physical world");
      out.push({to:j,code});
    }
    return out;
  }
  // Each projected lattice edge owns one or more height intervals. At a
  // horizontal position t, the floor is STORY * lerp(z0,z1,t). Separate
  // corridors may share X/Y only if their *entire* occupied tubes are
  // separated by at least HEADROOM+SLAB, not merely at their midpoints.
  reservation(i,j){
    const a=this.loc(i),b=this.loc(j),p=a.y*this.width+a.x,q=b.y*this.width+b.x;
    const forward=p<q,key=forward?p*this.area+q:q*this.area+p;
    return {key,z0:forward?a.z:b.z,z1:forward?b.z:a.z};
  }
  clear(i,j){
    const r=this.reservation(i,j),existing=this.reservations.get(r.key)||[];
    for(const old of existing){
      const d0=(r.z0-old.z0)*STORY,d1=(r.z1-old.z1)*STORY;
      // Differences that change sign have a crossing. Equal or adjacent
      // levels must leave enough vertical player headroom at both ends.
      if(d0*d1<=0||Math.min(Math.abs(d0),Math.abs(d1))<HEADROOM+SLAB)return false;
    }
    return true;
  }
  // Each cardinal heading has at most one exit: the corridor floor either
  // stays level, climbs, or descends. A staircase is NEVER an optional
  // piece of furniture beside a same-direction flat corridor.
  headingOccupied(i,code){
    const d=direction(code);
    return !!(this.bits[i]&((1<<d)|(1<<(d+4))|(1<<(d+8))));
  }
  join(i,j,code){
    if(this.headingOccupied(i,code)||this.headingOccupied(j,opposite(code)))
      throw new Error("Only one enclosed passage is permitted per heading");
    if(!this.clear(i,j))throw new Error("Reserved passage-volume collision");
    if((this.bits[i]&(1<<code))||this.bits[j]&(1<<opposite(code)))throw new Error("Repeated edge");
    this.bits[i]|=1<<code;this.bits[j]|=1<<opposite(code);
    const r=this.reservation(i,j),existing=this.reservations.get(r.key)||[];
    existing.push(r);this.reservations.set(r.key,existing);
    this.edges++;
    if(vertical(code)!==0){
      const floor=Math.min(this.loc(i).z,this.loc(j).z);
      if(++this.stairCountByBoundary[floor]>this.maxRampsPerBoundary)
        throw new Error("Stair density budget exceeded");
      this.stairs++;
    }
  }
  // Find real stacked passages sharing the *same projected horizontal
  // corridor footprint*. There is no junction between them. The lower
  // passage has a continuous enclosed headroom below the upper route slab.
  // A staircase may thus pass above an ordinary corridor invisibly.
  overpasses(){
    const result=[],asPoint=p=>({x:p%this.width,y:Math.floor(p/this.width)});
    for(const [key,segments] of this.reservations){
      if(segments.length<2)continue;
      const a=asPoint(Math.floor(key/this.area)),b=asPoint(key%this.area);
      for(let i=0;i<segments.length;i++)for(let j=i+1;j<segments.length;j++){
        const first=segments[i],second=segments[j];
        if((first.z0===first.z1)===(second.z0===second.z1))continue;
        const d0=(first.z0-second.z0)*STORY,d1=(first.z1-second.z1)*STORY;
        if(d0*d1<=0)throw new Error("Overlapping projected passages cross vertically");
        const minimumHeadroomMeters=Math.min(Math.abs(d0),Math.abs(d1))-SLAB;
        if(minimumHeadroomMeters<HEADROOM)throw new Error("Not enough headroom beneath overhead passage");
        const lower=d0<0?first:second,upper=d0<0?second:first;
        const info=s=>({kind:s.z0===s.z1?"flat":"stairs",startLevel:s.z0,endLevel:s.z1});
        result.push({from:a,to:b,lower:info(lower),upper:info(upper),
          minimumHeadroomMeters,requiredHeadroomMeters:HEADROOM,connected:false});
      }
    }
    return result;
  }
  // True directed 3D route edges, using floor-level positions in real meters.
  geometry(){
    const out=[];
    for(let i=0;i<this.volume;i++)if(this.used[i])for(const {to,code} of this.links(i))if(i<to){
      const a=this.loc(i),b=this.loc(to);
      out.push({from:a,to:b,kind:vertical(code)===0?"flat":"stairs",
        a:[a.x*PITCH,a.y*PITCH,a.z*STORY],b:[b.x*PITCH,b.y*PITCH,b.z*STORY],
        pitchMeters:PITCH,riseMeters:(b.z-a.z)*STORY,widthMeters:WIDTH,
        headroomMeters:HEADROOM,slabMeters:SLAB,enclosure:"walls-floor-ceiling"});
    }
    return out;
  }
  through(i){
    const edges=this.links(i);if(edges.length!==2)return false;
    const a=edges[0],b=edges[1],da=DIRECTIONS[direction(a.code)],db=DIRECTIONS[direction(b.code)];
    return vertical(a.code)!==0&&vertical(b.code)!==0&&da.dx+db.dx===0&&da.dy+db.dy===0&&vertical(a.code)+vertical(b.code)===0;
  }
  // Renderer-facing surface classification. A straight two-link rising flight
  // crosses intermediate story heights WITHOUT a landing or level-floor pad.
  // Only a turn, branch, slope reversal, or transition onto level ground can
  // require a landing; this is derived entirely from actual graph edges.
  stairNodeKind(i){
    if(!this.used[i])return "solid";
    if(!this.links(i).some(link=>vertical(link.code)!==0))return "level";
    return this.through(i)?"continuous-flight":"landing";
  }
  stairSurfaces(){
    const landings=[],continuous=[];
    for(let i=0;i<this.volume;i++){
      const kind=this.stairNodeKind(i);
      if(kind==="landing")landings.push(this.loc(i));
      else if(kind==="continuous-flight")continuous.push(this.loc(i));
    }
    return {landings,continuous};
  }
  runs(){
    const results=[],done=new Set();
    for(let i=0;i<this.volume;i++)if(this.used[i]&&!this.through(i)){
      for(const link of this.links(i)){
        if(vertical(link.code)===0)continue;
        const initial=Math.min(i,link.to)+":"+Math.max(i,link.to);
        if(done.has(initial))continue;
        const chain=[i];let prev=i,current=link.to;
        while(true){
          done.add(Math.min(prev,current)+":"+Math.max(prev,current));
          chain.push(current);
          if(!this.through(current))break;
          const next=this.links(current).find(x=>x.to!==prev);
          if(!next)throw new Error("Broken through-stair node");
          prev=current;current=next.to;
          if(chain.length>this.volume)throw new Error("Run contained a cycle");
        }
        const a=this.loc(chain[0]),b=this.loc(chain[chain.length-1]);
        results.push({steps:chain.length-1,from:a,to:b,vertices:chain.map(j=>this.loc(j)),rise:b.z-a.z});
      }
    }
    return results.sort((a,b)=>b.steps-a.steps);
  }
  // Edges are never added by route templates. Branches, stair continuation,
  // landing turns and multistory climbs all follow the same seeded growth.
  make(){
    const random=makeRandom(this.hash(100)),heads=new Uint8Array(this.volume),active=[];
    heads.fill(255);const root=this.index(this.hash(101)%this.width,this.hash(102)%this.height,0);
    const byFloor=new Uint32Array(this.levels);
    this.used[root]=1;byFloor[0]++;active.push(root);let usedCount=1;
    const opts=[],weights=[];
    // Once a floor is reached, flat passages win overwhelmingly. An eligible
    // new climb needs BOTH a seeded sparse opening AND remaining floor-pair
    // budget. Consecutive climbing edges may bypass the sparse-entry gate,
    // allowing spontaneous multi-story flights without prefab stair shapes.
    // If an unusually small seed strands an entire floor, one seeded
    // geometric bridging edge restarts carving; it uses the same join/clear
    // geometry tests, not an arbitrary vertical teleport.
    while(true){
    while(active.length){
      const at=random(100)<68?active.length-1:random(active.length);
      let cursor=active[at],steps=5+random(11);
      while(steps--){
        opts.length=0;weights.length=0;
        const oldCode=heads[cursor],oldDirection=oldCode===255?-1:direction(oldCode);
        const oldRise=oldCode===255?0:vertical(oldCode);
        for(let code=0;code<12;code++){
          const j=this.neighbor(cursor,code);
          if(j<0||this.used[j]||this.headingOccupied(cursor,code)||this.headingOccupied(j,opposite(code))||!this.clear(cursor,j))continue;
          const rise=vertical(code),destLevel=this.loc(j).z;
          const continuation=!!rise && rise===oldRise && oldDirection===direction(code)
            && this.links(cursor).length===1;
          if(rise){
            const boundary=Math.min(this.loc(cursor).z,destLevel);
            if(this.stairCountByBoundary[boundary]>=this.maxRampsPerBoundary)continue;
            // Sparse candidate locations are attached to real world edges,
            // not 16-cell chunks or a prefabricated staircase footprint.
            if(!continuation&&this.hash(777,Math.min(cursor,j),Math.max(cursor,j))%10000>=this.stairWeight*80)continue;
          }
          let weight=rise===0?18:continuation?68:3;
          if(rise&&byFloor[destLevel]===0)weight*=5;
          else if(!rise&&oldDirection===direction(code))weight*=2;
          if(oldCode!==255&&!rise&&random(100)<13)weight+=7;
          opts.push({j,code});weights.push(weight);
        }
        if(!opts.length){
          const remove=cursor===active[at]?at:active.length-1;
          active[remove]=active[active.length-1];active.pop();
          break;
        }
        let total=weights.reduce((a,b)=>a+b,0),pick=random(total),which=0;
        while((pick-=weights[which])>=0)which++;
        const {j,code}=opts[which];
        this.join(cursor,j,code);this.used[j]=1;usedCount++;byFloor[this.loc(j).z]++;
        heads[j]=code;active.push(j);cursor=j;
      }
    }
    const missing=byFloor.findIndex(count=>count===0);
    if(missing<0)break;
    // Connect the next missing floor using a real, legal, naturally oriented
    // ramp from any already explored neighbor. This is rare for normal sizes.
    let bridge=null,choices=0;
    for(let i=0;i<this.volume;i++)if(this.used[i]){
      for(let code=4;code<12;code++){
        const j=this.neighbor(i,code);
        if(j<0||this.used[j]||this.loc(j).z!==missing||this.headingOccupied(i,code)||this.headingOccupied(j,opposite(code))||!this.clear(i,j))continue;
        const boundary=Math.min(this.loc(i).z,missing);
        if(this.stairCountByBoundary[boundary]>=this.maxRampsPerBoundary)continue;
        if(random(++choices)===0)bridge={i,j,code};
      }
    }
    if(!bridge)throw new Error("Unable to connect level "+missing+" with a physical slope");
    this.join(bridge.i,bridge.j,bridge.code);
    this.used[bridge.j]=1;usedCount++;byFloor[missing]++;
    heads[bridge.j]=bridge.code;active.push(bridge.j);
    }
    // A few long alternative routes produce Connected-Detours-like choices,
    // without prefab stairs or isolated loops of four cells.
    const stamps=new Uint32Array(this.volume),queue=new Uint32Array(this.volume),dist=new Uint16Array(this.volume);
    let serial=0;
    const closeBy=(source,target)=>{
      const mark=++serial;let head=0,tail=1;
      queue[0]=source;stamps[source]=mark;dist[source]=0;
      while(head<tail){
        const i=queue[head++];if(i===target)return true;
        if(dist[i]>=13)continue;
        for(const {to} of this.links(i))if(stamps[to]!==mark){stamps[to]=mark;dist[to]=dist[i]+1;queue[tail++]=to;}
      }
      return false;
    };
    for(let i=0;i<this.volume;i++)if(this.used[i]&&random(100)<13){
      // Detours reconnect level corridors; do not turn them into stair spam.
      const codes=[random(4),random(4),random(4)];
      for(const code of codes){
        const j=this.neighbor(i,code);
        if(j<0||!this.used[j]||i>=j||this.headingOccupied(i,code)||this.headingOccupied(j,opposite(code))||!this.clear(i,j))continue;
        if(closeBy(i,j))continue;
        this.join(i,j,code);this.loops++;break;
      }
    }
    // Short horizontal-only terminal stubs may become solid masonry.
    // A staircase never disappears during cleanup, and leaf-pruning cannot
    // separate the remaining graph.
    for(let pass=0;pass<4;pass++){
      let changed=0;
      for(let i=0;i<this.volume;i++)if(this.used[i]&&popcount(this.bits[i])===1){
        const chain=[i],first=this.links(i)[0];
        if(vertical(first.code)!==0)continue;
        let prev=i,current=first.to,anchor=-1;
        while(chain.length<=3&&this.used[current]){
          const links=this.links(current);
          if(links.some(link=>vertical(link.code)!==0))break;
          if(links.length>=3){anchor=current;break;}
          if(links.length!==2)break;
          chain.push(current);
          const next=links.find(link=>link.to!==prev);
          prev=current;current=next.to;
        }
        if(anchor<0||chain.length>3)continue;
        for(let k=0;k<chain.length;k++){
          const node=chain[k],next=k+1<chain.length?chain[k+1]:anchor;
          const code=this.links(node).find(link=>link.to===next)?.code;
          if(code===undefined)throw new Error("Cannot prune missing terminal link");
          this.bits[next]&=~(1<<opposite(code));this.bits[node]=0;this.used[node]=0;changed++;this.pruned++;
        }
      }
      if(!changed)break;
    }
    this.usedCount=usedCount-this.pruned;
    this.levelCounts=Array.from({length:this.levels},(_,z)=>{
      let sum=0;for(let i=z*this.area;i<(z+1)*this.area;i++)if(this.used[i])sum++;
      return sum;
    });
    if(this.levelCounts.some(v=>v===0))throw new Error("Generated maze failed to reach a requested level");
    // Select seeded start and graph-distant exit, prioritizing different z.
    const entries=[];for(let i=0;i<this.volume;i++)if(this.used[i])entries.push(i);
    const startIndex=entries[this.hash(401)%entries.length];
    this.start=this.loc(startIndex);
    const distances=new Int32Array(this.volume);distances.fill(-1);
    let head=0,tail=1;queue[0]=startIndex;distances[startIndex]=0;
    while(head<tail){
      const i=queue[head++];for(const {to} of this.links(i))if(distances[to]===-1){
        distances[to]=distances[i]+1;queue[tail++]=to;
      }
    }
    if(tail!==this.usedCount)throw new Error("A navigation cell is disconnected");
    let far=0;for(const i of entries)if(this.loc(i).z!==this.start.z)far=Math.max(far,distances[i]);
    const threshold=Math.max(1,Math.ceil(far*0.8)),pick=makeRandom(this.hash(402));
    let count=0,exitIndex=-1;
    for(const i of entries)if(this.loc(i).z!==this.start.z&&distances[i]>=threshold)
      if(pick(++count)===0)exitIndex=i;
    if(exitIndex<0)throw new Error("Cannot choose a distant exit");
    this.exit=this.loc(exitIndex);this.routeLength=distances[exitIndex];
    this.longest=this.runs()[0]||null;
  }
}
