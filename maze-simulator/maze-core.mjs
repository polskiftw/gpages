// Maze Lab v2. Mathematical topology is independent of any renderer.
import {carveChunk} from "./maze-carve.mjs";
export const N=1,E=2,S=4,W=8,UP=16,DOWN=32,CHUNK=16;
export const STYLES=Object.freeze({
  winding:  {name:"Long corridors",mode:"dfs",straight:82,recent:100,loops:2,cross:8,vertical:1},
  balanced: {name:"Mixed labyrinth",mode:"growing",straight:43,recent:76,loops:5,cross:15,vertical:3},
  branching:{name:"Many branches",mode:"prim",straight:0,recent:0,loops:3,cross:12,vertical:3}
});
export const TAG=Object.freeze({AXIS:10,CUT:11,PORT0:12,PORT1:13,CHUNK:20,LOOP:21,EDGE:22,OFFSET:23,VX:24,VY:25,STAIR:26,START:30,END:31});
function mix(x){x=(x^(x>>>16))>>>0;x=Math.imul(x,0x7feb352d);x=(x^(x>>>15))>>>0;x=Math.imul(x,0x846ca68b);return (x^(x>>>16))>>>0;}
function seedNumber(s){let h=2166136261;for(let i=0;i<s.length;i++)h=Math.imul(h^s.charCodeAt(i),16777619)>>>0;return mix(h);}
function ordered(a,b){return a[2]!==b[2]?(a[2]<b[2]?[a,b]:[b,a]):a[1]!==b[1]?(a[1]<b[1]?[a,b]:[b,a]):a[0]<b[0]?[a,b]:[b,a];}
export class Maze {
  constructor({width=64,height=64,levels=3,seed="LOST-6549",style="balanced",cacheLimit=96}={}){
    for(const [k,n,limit] of [["width",width,4096],["height",height,4096],["levels",levels,64],["cacheLimit",cacheLimit,512]])
      if(!Number.isSafeInteger(n)||n<1||n>limit)throw new RangeError(k+" must be 1.."+limit);
    if(width*height*levels<2)throw new RangeError("at least two cells needed");
    if(!Object.hasOwn(STYLES,style))throw new RangeError("unknown maze style");
    this.width=width;this.height=height;this.levels=levels;this.seed=String(seed);this.style=style;
    this.profile=STYLES[style];this.cacheLimit=cacheLimit;
    this.counts=[Math.ceil(width/CHUNK),Math.ceil(height/CHUNK),levels];
    this.salt=seedNumber(this.seed+"|"+width+"|"+height+"|"+levels+"|MAZE-V2");
    this.cache=new Map();this.start=this.endpoint(TAG.START);this.exit=this.endpoint(TAG.END);
  }
  hash(tag,...items){let h=mix(this.salt^tag);for(const i of items)h=mix(h^mix(i>>>0));return h>>>0;}
  boxHash(b,tag){return this.hash(tag,...b.lo,...b.hi);}
  edgeHash(a,b,tag){const [lo,hi]=ordered(a,b);return this.hash(tag,...lo,...hi);}
  endpoint(tag){
    const corner=this.hash(TAG.START,0)%4, far=tag===TAG.END, near=Math.max(1,Math.floor(Math.min(this.width,this.height)/5));
    const hx=this.hash(tag,1)%Math.min(this.width,near),hy=this.hash(tag,2)%Math.min(this.height,near);
    const x=((corner&1)!==(far?1:0))?this.width-1-hx:hx;
    const y=((corner&2)!==(far?2:0))?this.height-1-hy:hy;
    const z=this.levels===1?0:(far?this.levels-1-this.hash(TAG.END,3)%Math.floor(this.levels/2):this.hash(TAG.START,3)%Math.floor(this.levels/2));
    return {x,y,z:this.levels>1&&(this.hash(TAG.START,4)&1)?this.levels-1-z:z};
  }
  split(b){
    let best=1,axes=[];
    for(let a=0;a<3;a++){const n=b.hi[a]-b.lo[a];if(n>best){best=n;axes=[a];}else if(n===best&&n>1)axes.push(a);}
    if(!axes.length)throw new Error("cannot split a single chunk");
    const axis=axes[this.boxHash(b,TAG.AXIS)%axes.length],size=b.hi[axis]-b.lo[axis];
    const low=Math.max(1,Math.floor(size/3)),high=size-low;
    return {axis,cut:b.lo[axis]+low+this.boxHash(b,TAG.CUT)%(high-low+1)};
  }
  // Recursively divide the chunk prism. One edge joins each sibling pair.
  // Therefore its chunk graph is a spanning tree without precomputing the world.
  baseEdge(a,b){
    let moving=-1;
    for(let i=0;i<3;i++)if(a[i]!==b[i]){if(moving!==-1||Math.abs(a[i]-b[i])!==1)return false;moving=i;}
    if(moving===-1)return false;
    const box={lo:[0,0,0],hi:this.counts.slice()};
    for(let depth=0;depth<128;depth++){
      const {axis,cut}=this.split(box),la=a[axis]<cut,lb=b[axis]<cut;
      if(la!==lb){
        if(axis!==moving)return false;
        const lower=la?a:b;
        if(lower[moving]!==cut-1)return false;
        let which=0;
        for(let i=0;i<3;i++)if(i!==moving){
          const loc=box.lo[i]+this.boxHash(box,which++===0?TAG.PORT0:TAG.PORT1)%(box.hi[i]-box.lo[i]);
          if(lower[i]!==loc)return false;
        }
        return true;
      }
      if(la)box.hi[axis]=cut;else box.lo[axis]=cut;
    }
    throw new Error("macro descent limit exceeded");
  }
  macroEdge(a,b){
    if(this.baseEdge(a,b))return true;
    const isVertical=a[2]!==b[2],rate=isVertical?this.profile.vertical:this.profile.cross;
    return rate>0&&this.edgeHash(a,b,isVertical?TAG.STAIR:TAG.EDGE)%100<rate;
  }
  generateChunk(cx,cy,z){return carveChunk(this,cx,cy,z);}
  chunk(cx,cy,z){
    const key=cx+","+cy+","+z;
    if(this.cache.has(key)){const c=this.cache.get(key);this.cache.delete(key);this.cache.set(key,c);return c;}
    const c=this.generateChunk(cx,cy,z);
    if(this.cache.size>=this.cacheLimit)this.cache.delete(this.cache.keys().next().value);
    this.cache.set(key,c);return c;
  }
  passages(x,y,z){
    if(![x,y,z].every((v,i)=>Number.isInteger(v)&&v>=0&&v<[this.width,this.height,this.levels][i]))
      throw new RangeError("cell outside maze");
    const c=this.chunk(Math.floor(x/CHUNK),Math.floor(y/CHUNK),z);
    return c.cells[(y-c.oy)*c.w+x-c.ox];
  }
  neighbors(x,y,z){
    const bits=this.passages(x,y,z),out=[];
    if(bits&N)out.push({x,y:y-1,z});
    if(bits&E)out.push({x:x+1,y,z});
    if(bits&S)out.push({x,y:y+1,z});
    if(bits&W)out.push({x:x-1,y,z});
    if(bits&UP)out.push({x,y,z:z+1});
    if(bits&DOWN)out.push({x,y,z:z-1});
    return out;
  }
}
