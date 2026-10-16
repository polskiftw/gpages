// Maze Lab v4: one global graph; generator contains no render chunks.
import {carveWorld} from './maze-grow.mjs';
export const N=1,E=2,S=4,W=8,UP=16,DOWN=32,SOLID=64;
export const ENGINE_VERSION='global-v4';
export const MAX_CELLS=4194304;
export const STYLES=Object.freeze({
  deep:{name:'Deep branches',mode:'strokes',minRun:9,maxRun:24,straight:24,lookahead:1,pruneDepth:5,repairDepth:7,cycles:1,cluster:20,extraStairs:12},
  interconnected:{name:'Connected detours',mode:'strokes',minRun:7,maxRun:17,straight:16,lookahead:1,pruneDepth:4,repairDepth:5,cycles:2,cluster:15,extraStairs:26},
  roaming:{name:'Wandering branches',mode:'growing',latest:93,straight:16,pruneDepth:6,repairDepth:6,cycles:1,cluster:30,extraStairs:16}
});
const TAG={STAIR:21,ANCHOR:22,ROOT:23,RNG:24,LOOP:25,STAIRLOOP:26,START:27,END:28,REPAIR:29};
function mix(x){x=(x^(x>>>16))>>>0;x=Math.imul(x,0x7feb352d);x=(x^(x>>>15))>>>0;x=Math.imul(x,0x846ca68b);return (x^(x>>>16))>>>0;}
function seedNumber(s){let h=2166136261;for(let i=0;i<s.length;i++)h=Math.imul(h^s.charCodeAt(i),16777619)>>>0;return mix(h);}
export function randomNumber(seed){let x=seed>>>0;return max=>{x=(x+0x6d2b79f5)>>>0;let t=Math.imul(x^(x>>>15),1|x);t^=t+Math.imul(t^(t>>>7),61|t);return ((t^(t>>>14))>>>0)%max;};}
export class Maze{
  constructor({width=64,height=64,levels=3,seed='LOST-6549',style='deep'}={}){
    for(const [name,n,limit] of [['width',width,4096],['height',height,4096],['levels',levels,64]])
      if(!Number.isSafeInteger(n)||n<1||n>limit)throw new RangeError(name+' must be an integer from 1 to '+limit);
    const volume=width*height*levels;
    if(volume<2)throw new RangeError('At least two cells are needed for distinct start and exit.');
    if(volume>MAX_CELLS)throw new RangeError('Limit '+MAX_CELLS+' cells; reduce dimensions.');
    if(!Object.hasOwn(STYLES,style))throw new RangeError('Unknown passage style');
    if(!String(seed))throw new RangeError('Seed is required');
    this.width=width;this.height=height;this.levels=levels;this.volume=volume;this.area=width*height;
    this.seed=String(seed);this.style=style;this.profile=STYLES[style];this.version=ENGINE_VERSION;
    this.salt=seedNumber([this.seed,width,height,levels,style,ENGINE_VERSION].join('|'));
    this.anchors=new Uint32Array(Math.max(0,levels-1));
    for(let z=0;z<levels-1;z++)this.anchors[z]=this.hash(TAG.ANCHOR,z)%this.area;
    const result=carveWorld(this,TAG);
    this.cells=result.cells;this.start=result.start;this.exit=result.exit;
    this.routeLength=result.routeLength;this.metrics=result.metrics;
  }
  hash(tag,a=0,b=0,c=0){let h=mix(this.salt^tag);h=mix(h^mix(a));h=mix(h^mix(b));return mix(h^mix(c));}
  stairCandidate(xy,lowerZ){return xy===this.anchors[lowerZ]||this.hash(TAG.STAIR,xy,lowerZ)%512===0;}
  index(x,y,z){return z*this.area+y*this.width+x;}
  location(i){const xy=i%this.area;return {x:xy%this.width,y:Math.floor(xy/this.width),z:Math.floor(i/this.area)};}
  passages(x,y,z){if(!Number.isInteger(x)||!Number.isInteger(y)||!Number.isInteger(z)||x<0||y<0||z<0||x>=this.width||y>=this.height||z>=this.levels)throw new RangeError('Cell outside maze');return this.cells[this.index(x,y,z)];}
  neighbors(x,y,z){const p=this.passages(x,y,z),out=[];if(p&N)out.push({x,y:y-1,z});if(p&E)out.push({x:x+1,y,z});if(p&S)out.push({x,y:y+1,z});if(p&W)out.push({x:x-1,y,z});if(p&UP)out.push({x,y,z:z+1});if(p&DOWN)out.push({x,y,z:z-1});return out;}
}
