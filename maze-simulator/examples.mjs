// Original, exact mathematical floor plans from the production global maze.
import {mkdirSync,writeFileSync} from 'node:fs';
import {Maze,ENGINE_VERSION,STYLES} from './maze-core.mjs';
import {floorSvg} from './maze-map.mjs';
const out=process.argv[2]||'/tmp/maze-samples',seed='LOST-6549',width=64,height=64,levels=3;
mkdirSync(out,{recursive:true});
const metrics=[];
for(const style of Object.keys(STYLES)){
 const maze=new Maze({width,height,levels,seed,style});
 const item={version:ENGINE_VERSION,seed,style,dimensions:[width,height,levels],start:maze.start,exit:maze.exit,routeLength:maze.routeLength,metrics:maze.metrics};
 metrics.push(item);console.log('METRIC',JSON.stringify(item));
 for(const floor of style==='deep'?[0,1,2]:[0]){
  const name=style+'-floor-'+(floor+1)+'.svg';
  const {svg,stats}=floorSvg(maze,{floor,width,height});
  writeFileSync(out+'/'+name,svg);
  console.log('SVG',name,JSON.stringify(stats));
 }
}
writeFileSync(out+'/metrics.json',JSON.stringify(metrics,null,2)+'\n');
