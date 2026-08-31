(() => {
'use strict';
const W='https://terraria.wiki.gg/wiki/';
const D=window.TERRARIA_RECIPE_DATA||{recipes:[],nodes:[],recipeCount:0,craftableCount:0,endpointCount:0};
const E=window.TERRARIA_ENRICHMENT||{items:{},availability:{},projects:[]};
const items=E.items||{}, availability=E.availability||{}, curatedProjects=E.projects||[];
const generatedAvailability=window.TERRARIA_GENERATED_AVAILABILITY||{};
const generatedFishing=window.TERRARIA_GENERATED_FISHING||{};
const $=s=>document.querySelector(s), $$=s=>[...document.querySelectorAll(s)];
const search=$('#search'), sort=$('#sort'), missingOnly=$('#missingOnly'), fishMode=$('#fishMode'), content=$('#content'), stats=$('#stats'), status=$('#status'), clearSearch=$('#clearSearch');
const PAGE=80;
let view='projects', shown=PAGE;

const recipes=Array.isArray(D.recipes)?D.recipes:[];
const nodes=Array.isArray(D.nodes)?D.nodes:[];
const graphReady=recipes.length>0&&nodes.length>0;
const byResult=new Map();
for(const r of recipes){if(!byResult.has(r.r))byResult.set(r.r,[]);byResult.get(r.r).push(r)}
const ingredientUseCount=new Map();
for(const r of recipes)for(const pair of r.i||[]){const name=String(pair[0]);ingredientUseCount.set(name,(ingredientUseCount.get(name)||0)+1)}
const reciprocalRoleCache=new WeakMap();
const processedBuildForm=name=>/\b(?:Platform|Wall)$/.test(String(name));
function reciprocalIngredient(recipe){
  const input=recipe.i||[];
  if(input.length!==1)return'';
  const ingredient=String(input[0][0]), ingredientQty=Math.max(1,Number(input[0][1])||1), resultQty=Math.max(1,Number(recipe.a)||1), result=String(recipe.r);
  const reverse=(byResult.get(ingredient)||[]).some(r=>{const ri=r.i||[];return ri.length===1&&String(ri[0][0])===result&&Math.max(1,Number(ri[0][1])||1)===resultQty&&Math.max(1,Number(r.a)||1)===ingredientQty});
  return reverse?ingredient:'';
}
function reciprocalRecipeRole(recipe){
  if(reciprocalRoleCache.has(recipe))return reciprocalRoleCache.get(recipe);
  const ingredient=reciprocalIngredient(recipe);
  if(!ingredient){reciprocalRoleCache.set(recipe,'');return''}
  const result=String(recipe.r), resultProcessed=processedBuildForm(result), ingredientProcessed=processedBuildForm(ingredient);
  let role='ambiguous';
  if(resultProcessed!==ingredientProcessed)role=resultProcessed?'forward':'inverse';
  else{
    const resultUses=ingredientUseCount.get(result)||0, ingredientUses=ingredientUseCount.get(ingredient)||0;
    if(resultUses!==ingredientUses)role=ingredientUses>resultUses?'forward':'inverse';
  }
  reciprocalRoleCache.set(recipe,role);return role;
}
const planningRecipes=name=>(byResult.get(name)||[]).filter(recipe=>{const role=reciprocalRecipeRole(recipe);return role!=='inverse'&&role!=='ambiguous'});
const displayRecipes=name=>(byResult.get(name)||[]).filter(recipe=>reciprocalRecipeRole(recipe)!=='inverse');
const nodeMap=new Map(nodes.map(n=>[n[0],{name:n[0],uniqueItems:Number(n[1])||0,endpoint:!!n[2]}]));
const curatedByName=new Map(curatedProjects.map(p=>[p.name,p]));
const legacyNameIds=new Map();
for(const [id,i] of Object.entries(items)){const name=i&&i.name;if(!name)continue;if(!legacyNameIds.has(name))legacyNameIds.set(name,[]);legacyNameIds.get(name).push(id)}
const legacyExact=name=>{const ids=legacyNameIds.get(name)||[];return ids.length===1?ids[0]:null};
const fallbackNodes=curatedProjects.map(p=>({name:p.name,uniqueItems:p.items.length,endpoint:true,fallback:true}));
const projectNodes=graphReady?[...nodeMap.values()].filter(n=>n.endpoint):fallbackNodes;
const componentNodes=graphReady?[...nodeMap.values()]:fallbackNodes;
const shoppingCache=new Map();

let acquired={};
try{acquired=JSON.parse(localStorage.getItem('terraria-shopping-progress-v2')||'{}')||{}}catch(_){acquired={}}
if(!Object.keys(acquired).length){
  try{const old=JSON.parse(localStorage.getItem('terraria-shopping-progress-v1')||'{}')||{};for(const [id,v] of Object.entries(old))if(v)acquired['legacy:'+id]=true}catch(_){}
}
if(fishMode){try{fishMode.checked=localStorage.getItem('terraria-shopping-fish-mode-v1')==='1'}catch(_){}}
let fishModeProgress=fishMode?.checked?{...acquired}:null;
const fishModeEligibility=new Map();
const save=()=>localStorage.setItem('terraria-shopping-progress-v2',JSON.stringify(acquired));
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));
const wikiName=name=>W+encodeURIComponent(String(name).replaceAll(' ','_'));
const wikiItem=id=>W+(items[id]?.wiki||encodeURIComponent(String(items[id]?.name||id).replaceAll(' ','_')));
const avObject=tuple=>{
  const a=tuple||['Unknown',[],'',999];
  if(a.length>=4)return{mode:a[0],when:a[1],source:String(a[2]||''),rank:Number(a[3])||999};
  return{mode:a[0],when:a[1],source:'',rank:Number(a[2])||999};
};
const av=id=>avObject(availability[id]);
const modeClass=mode=>mode==='Hardmode'?'hard':'pre';
const whenParts=when=>(Array.isArray(when)?when:[when]).map(v=>String(v||'').trim()).filter(Boolean);
const cleanWhenPart=when=>String(when||'').replace(/^Available immediately(?:\s*[•/]\s*)?/,'').replace(/^After Wall of Flesh(?:\s*•\s*)?/,'').trim();
const cleanWhen=when=>whenParts(when).map(cleanWhenPart).filter(Boolean).join(' ');
const whenPill=when=>whenParts(when).map(cleanWhenPart).filter(Boolean).map(w=>`<span class="pill availability">${esc(w)}</span>`).join('');
const sourcePill=source=>source?`<span class="pill sourcepill">${esc(source)}</span>`:'';
const avObjectPills=a=>{const source=sourcePill(a.source);return a.mode==='Unknown'?source:`<span class="pill availability ${modeClass(a.mode)}">${esc(a.mode.toUpperCase())}</span>${whenPill(a.when)}${source}`};
const fishingRoute=name=>String(generatedFishing[name]||'');
const fishingRouteFor=(name,info=null)=>fishingRoute(name)||String(info?.fishSource||'');
const avPills=(id,preferFishing=false)=>{
  const a=av(id), i=items[id];
  if(preferFishing){const route=fishingRouteFor(i?.name||'',i);if(route)a.source=route}
  return avObjectPills(a);
};
const hasFishingRoute=(name,info=null)=>!!info?.fish||!!fishingRouteFor(name,info);
const keyFor=(name,explicitId=null)=>explicitId?'legacy:'+explicitId:(legacyExact(name)?'legacy:'+legacyExact(name):'gen:'+name);
const isMissing=key=>!acquired[key];
const checkedCount=()=>Object.values(acquired).filter(Boolean).length;
function setAcquired(key,v){acquired[key]=!!v;if(!v)delete acquired[key];save();const open=new Set($$('details.project[open]').map(el=>el.dataset.name));render(open)}
function checkbox(key,label){return `<input class="check" type="checkbox" data-key="${esc(key)}" ${acquired[key]?'checked':''} aria-label="Mark ${esc(label)} acquired">`}
function addQty(map,name,qty){map.set(name,(map.get(name)||0)+qty)}
function mergeLeaves(into,from){for(const [name,qty] of from)addQty(into,name,qty)}
function totalQty(map){let n=0;for(const q of map.values())n+=q;return n}

function planItem(name,qty=1,stack=new Set(),depth=0){
  const options=planningRecipes(name);
  if(!options.length||depth>48)return{leaves:new Map([[name,qty]]),cycles:0,steps:0};
  if(stack.has(name))return{leaves:new Map(),cycles:1,steps:0,cycleTo:name};
  const next=new Set(stack);next.add(name);
  const candidates=[];let propagatedCycle='',cycleClosedHere=false;
  for(const recipe of options){
    const batches=Math.ceil(qty/Math.max(1,Number(recipe.a)||1));
    const leaves=new Map();let steps=1,cycleTo='';
    for(const pair of recipe.i||[]){
      const childName=String(pair[0]), childQty=Math.max(1,Number(pair[1])||1)*batches;
      const child=planItem(childName,childQty,next,depth+1);
      if(child.cycleTo){cycleTo=child.cycleTo;break}
      mergeLeaves(leaves,child.leaves);steps+=child.steps;
    }
    if(cycleTo){if(cycleTo===name)cycleClosedHere=true;else propagatedCycle=propagatedCycle||cycleTo;continue}
    candidates.push({leaves,cycles:0,steps,recipe});
  }
  if(!candidates.length){
    if(propagatedCycle&&!cycleClosedHere)return{leaves:new Map(),cycles:1,steps:0,cycleTo:propagatedCycle};
    return{leaves:new Map([[name,qty]]),cycles:0,steps:0};
  }
  candidates.sort((a,b)=>a.leaves.size-b.leaves.size||totalQty(a.leaves)-totalQty(b.leaves)||a.steps-b.steps||String(a.recipe.s).localeCompare(String(b.recipe.s))||JSON.stringify(a.recipe.i).localeCompare(JSON.stringify(b.recipe.i)));
  return candidates[0];
}
function shoppingEntries(name){
  if(shoppingCache.has(name))return shoppingCache.get(name);
  const cp=curatedByName.get(name);let entries=[];
  if(cp){
    entries=cp.items.map(([id,qty])=>{const i=items[id]||{name:id},itemName=i.name||id;return{id,name:itemName,qty:Number(qty)||1,key:keyFor(itemName,id),fishable:hasFishingRoute(itemName,i),info:i}});
  }else{
    const plan=planItem(name,1);
    entries=[...plan.leaves.entries()].map(([leaf,qty])=>{const id=legacyExact(leaf),i=id?items[id]:null;return{id,name:leaf,qty,key:keyFor(leaf,id),fishable:hasFishingRoute(leaf,i),info:i}}).sort((a,b)=>a.name.localeCompare(b.name));
  }
  shoppingCache.set(name,entries);
  return entries;
}
function hasMissingFishAtSnapshot(name){
  if(!fishModeProgress)return true;
  if(fishModeEligibility.has(name))return fishModeEligibility.get(name);
  const eligible=shoppingEntries(name).some(entry=>entry.fishable&&!fishModeProgress[entry.key]);
  fishModeEligibility.set(name,eligible);
  return eligible;
}
function resetFishModeSnapshot(){
  fishModeProgress=fishMode?.checked?{...acquired}:null;
  fishModeEligibility.clear();
}
function shoppingSummary(name){
  const entries=shoppingEntries(name), total=entries.length;
  const fishableEntries=entries.filter(e=>e.fishable), remainderEntries=entries.filter(e=>!e.fishable);
  const fishable=fishableEntries.length, remainders=remainderEntries.length;
  const fishableObtained=fishableEntries.filter(e=>acquired[e.key]).length, remainderObtained=remainderEntries.filter(e=>acquired[e.key]).length;
  return{entries,total,fishable,remainders,fishableObtained,remainderObtained};
}
function uniqueNeeded(node){const cp=curatedByName.get(node.name);return cp?cp.items.length:node.uniqueItems}

const EVIL_GROUPS=[
  ['Demonite Bar','Crimtane Bar'],
  ['Demonite Ore','Crimtane Ore'],
  ['Shadow Scale','Tissue Sample'],
  ['Rotten Chunk','Vertebra'],
  ['Vile Mushroom','Vicious Mushroom'],
  ['Vile Powder','Vicious Powder'],
  ['Corrupt Seeds','Crimson Seeds'],
  ['Ebonstone Block','Crimstone Block'],
  ['Cursed Flame','Ichor'],
  ['Purple Solution','Red Solution']
];
const evilMap=new Map();
EVIL_GROUPS.forEach((names,index)=>names.forEach(name=>evilMap.set(name,{token:`@evil${index}`,names})));
const stationGroup=new Map([['Demon Altar',{token:'@evil-altar',names:['Demon Altar','Crimson Altar']}],['Crimson Altar',{token:'@evil-altar',names:['Demon Altar','Crimson Altar']}] ]);
function normalizedRecipeKey(r){
  const station=stationGroup.get(String(r.s||''))?.token||String(r.s||'By hand');
  const ingredients=(r.i||[]).map(([name,qty])=>[evilMap.get(String(name))?.token||String(name),Number(qty)||1]).sort((a,b)=>a[0].localeCompare(b[0])||a[1]-b[1]);
  return JSON.stringify([Number(r.a)||1,station,ingredients]);
}
function groupedRecipes(name,planning=false){
  const groups=new Map(), source=planning?planningRecipes(name):displayRecipes(name);
  for(const r of source){const key=normalizedRecipeKey(r);if(!groups.has(key))groups.set(key,[]);groups.get(key).push(r)}
  return [...groups.values()];
}
function displayStation(variants){
  const values=[...new Set(variants.map(r=>String(r.s||'By hand')))];
  if(values.length===1)return values[0];
  const mapped=values.map(v=>stationGroup.get(v));
  if(mapped.every(Boolean)&&new Set(mapped.map(x=>x.token)).size===1)return mapped[0].names.join(' / ');
  return values.join(' / ');
}
function displayIngredient(basePair,variants){
  const [rawName,rawQty]=basePair,name=String(rawName),qty=Number(rawQty)||1,group=evilMap.get(name);
  if(!group)return{name,qty};
  const seen=new Set();
  for(const r of variants)for(const [variantName,variantQty] of r.i||[]){const vg=evilMap.get(String(variantName));if(vg?.token===group.token&&(Number(variantQty)||1)===qty)seen.add(String(variantName))}
  const names=group.names.filter(n=>seen.has(n));
  return{name:(names.length?names:[name]).join(' / '),qty};
}
function recipeOptionsHtml(name){
  const groups=groupedRecipes(name);
  if(!groups.length)return'';
  const label=groups.length===1?'Recipe':'Recipes';
  return`<div class="bodyhead">${label}</div><div class="recipebox">${groups.map(variants=>{const r=variants[0];return`<div class="recipeopt"><strong>${esc(name)}${Number(r.a)>1?` ×${Number(r.a)}`:''}</strong> <span class="pill">${esc(displayStation(variants))}</span><div class="recipeingredients">${(r.i||[]).map(pair=>{const x=displayIngredient(pair,variants);return`${esc(x.name)}${x.qty>1?` ×${x.qty}`:''}`}).join(' • ')}</div></div>`}).join('')}</div>`;
}
function fallbackSourceForName(name){
  const groups=groupedRecipes(name,true);
  if(!groups.length)return'';
  const stations=[...new Set(groups.map(displayStation).filter(Boolean))];
  if(!stations.length)return'';
  return `${stations[0]} (craft)`;
}
function entryAvPills(entry){
  const tuple=(entry.id&&availability[entry.id])||generatedAvailability[entry.name];
  const a=avObject(tuple), route=entry.fishable?fishingRouteFor(entry.name,entry.info):'';
  if(route)a.source=route;else if(!a.source)a.source=fallbackSourceForName(entry.name);
  return avObjectPills(a);
}
function shoppingRow(entry){
  if(fishMode?.checked&&!entry.fishable)return'';
  if(missingOnly.checked&&!isMissing(entry.key))return'';
  const meta=entryAvPills(entry);
  return`<div class="row">${checkbox(entry.key,entry.name)}<div><div class="itemname">${esc(entry.name)} ${entry.qty>1?`<span class="qty">×${entry.qty}</span>`:''}</div>${meta?`<div class="itemmeta">${meta}</div>`:''}</div><a class="wiki" href="${entry.id?wikiItem(entry.id):wikiName(entry.name)}" target="_blank" rel="noopener">Wiki ↗</a></div>`;
}
function buildProjectBody(name){
  const summary=shoppingSummary(name), entries=fishMode?.checked?summary.entries.filter(e=>e.fishable):summary.entries;let rows='';
  for(const entry of entries)rows+=shoppingRow(entry);
  if(!rows)rows=`<div class="empty">${fishMode?.checked?'All fishing-route items are checked.':'Everything in this shopping list is checked.'}</div>`;
  const label=fishMode?.checked?'Fishing list':'Shopping list';
  return`<div class="bodyhead">${label} • ${entries.length} unique item${entries.length===1?'':'s'}</div><div class="rows">${rows}</div>`+recipeOptionsHtml(name);
}
function projectSearchBlob(name){
  const cp=curatedByName.get(name), rs=displayRecipes(name);const immediate=rs.flatMap(r=>r.i||[]).map(x=>x[0]);const stations=rs.map(r=>r.s||'');
  let enrich='';if(cp)enrich=[cp.group,cp.mode,cp.when,cp.note,...cp.items.flatMap(x=>{const i=items[x[0]]||{};return[i.name,i.source,i.fishSource,fishingRoute(i.name),i.type,i.note]})].join(' ');
  return[name,...immediate,...stations,enrich].join(' ').toLowerCase();
}
function nodeSort(a,b){
  if(sort.value==='name')return a.name.localeCompare(b.name);
  return uniqueNeeded(b)-uniqueNeeded(a)||a.name.localeCompare(b.name);
}
function projectCard(n){
  const s=shoppingSummary(n.name);
  const fish=s.fishable?`<span class="pill fish">${s.fishableObtained}/${s.fishable} FISHABLE</span>`:'<span class="pill hard">LANDLOCKED</span>';
  const remainders=fishMode?.checked?'':`<span class="pill ${s.remainderObtained===s.remainders&&s.remainders?'pre':''}">${s.remainderObtained}/${s.remainders} REMAINDERS</span>`;
  return`<details class="card project" data-name="${esc(n.name)}"><summary class="projectsummary"><div class="projecttop"><div><div class="projecttitle">${esc(n.name)}</div><div class="projectmeta">${fish}${remainders}</div></div><span class="chev">›</span></div></summary><div class="projectbody" data-loaded="0"><div class="loading">Opening shopping list…</div></div></details>`;
}
function renderCatalog(openNames=new Set()){
  const base=view==='projects'?projectNodes:componentNodes;const q=search.value.trim().toLowerCase();let list=base;
  if(q)list=list.filter(n=>projectSearchBlob(n.name).includes(q));if(fishMode?.checked)list=list.filter(n=>hasMissingFishAtSnapshot(n.name));list=[...list].sort(nodeSort);
  const visible=list.slice(0,shown),label=view==='projects'?'Projects':'All craftable items';
  let html=`<div class="sectionhead"><h2>${label}</h2><span>${list.length} matches</span></div><div class="projectgrid">${visible.map(projectCard).join('')}</div>`;
  if(list.length>visible.length)html+=`<div class="morewrap"><button class="more" id="showMore">Show ${Math.min(PAGE,list.length-visible.length)} more • ${list.length-visible.length} remaining</button></div>`;
  if(!list.length)html=`<div class="card empty">${fishMode?.checked?'No craftables have unfinished fishing-route items in this Fish Mode snapshot.':'No craftables match the current search.'}</div>`;
  content.innerHTML=html;for(const d of $$('details.project'))if(openNames.has(d.dataset.name)){d.open=true;loadBody(d)}
  $('#showMore')?.addEventListener('click',()=>{const keepOpen=new Set($$('details.project[open]').map(el=>el.dataset.name));shown+=PAGE;render(keepOpen)});renderStats(list.length);
}
function sourceSearchText(id){const i=items[id],a=av(id),ps=curatedProjects.filter(p=>p.items.some(x=>x[0]===id)).map(p=>p.name).join(' ');return[i.name,i.fishSource||'',fishingRoute(i.name),i.source||'',i.type,a.mode,cleanWhen(a.when),a.source||'',i.note||'',ps].join(' ').toLowerCase()}
function renderSources(wantFish){
  const q=search.value.trim().toLowerCase();let ids=Object.keys(items).filter(id=>hasFishingRoute(items[id].name,items[id])===wantFish);if(q)ids=ids.filter(id=>sourceSearchText(id).includes(q));if(missingOnly.checked)ids=ids.filter(id=>isMissing(keyFor(items[id].name,id)));
  ids.sort((a,b)=>sort.value==='name'?items[a].name.localeCompare(items[b].name):av(a).rank-av(b).rank||items[a].name.localeCompare(items[b].name));
  const title=wantFish?'Ingredients with a fishing route':'Ingredients still needed on land';let html=`<div class="sectionhead"><h2>${title}</h2><span>${ids.length} items</span></div><div class="ingredientgrid">`;
  for(const id of ids){const i=items[id],key=keyFor(i.name,id),ps=curatedProjects.filter(p=>p.items.some(x=>x[0]===id));const src=wantFish?(fishingRouteFor(i.name,i)||i.source):i.source;html+=`<article class="card ingredient"><div class="row">${checkbox(key,i.name)}<div><div class="itemname">${esc(i.name)}</div><div class="itemmeta"><span class="pill">${esc(i.type)}</span>${avPills(id,wantFish)}</div><div class="source">${esc(src)}</div>${i.note?`<div class="note">${esc(i.note)}</div>`:''}<div class="tags">${ps.map(p=>`<span class="tag">${esc(p.name)}</span>`).join('')}</div></div><a class="wiki" href="${wikiItem(id)}" target="_blank" rel="noopener">Wiki ↗</a></div></article>`}
  html+='</div>';if(!ids.length)html=`<div class="card empty">No ${wantFish?'fishable':'landlocked'} ingredients match.</div>`;content.innerHTML=html;renderStats(ids.length)
}
function renderStats(visible){
  const endpoints=graphReady?Number(D.endpointCount)||projectNodes.length:projectNodes.length, craftables=graphReady?Number(D.craftableCount)||componentNodes.length:componentNodes.length;
  stats.innerHTML=`<span class="stat"><strong>${visible}</strong> shown/matched</span><span class="stat"><strong>${endpoints.toLocaleString()}</strong> projects</span><span class="stat"><strong>${craftables.toLocaleString()}</strong> craftables</span><span class="stat"><strong>${checkedCount()}</strong> checked</span>`;
}
function loadBody(details){const body=details.querySelector('.projectbody');if(!body||body.dataset.loaded==='1')return;body.innerHTML=buildProjectBody(details.dataset.name);body.dataset.loaded='1'}
function render(openNames=new Set()){
  clearSearch.style.display=search.value?'block':'none';$$('.segments button').forEach(b=>b.classList.toggle('active',b.dataset.view===view));
  status.innerHTML=graphReady?'':`<div class="status warn">Full recipe data is not available yet. Showing the smaller built-in project list.</div>`;
  if(view==='projects'||view==='components')renderCatalog(openNames);else renderSources(view==='fishable');
}
content.addEventListener('toggle',e=>{const d=e.target;if(d.matches?.('details.project')&&d.open)loadBody(d)},true);
content.addEventListener('change',e=>{if(e.target.matches('.check'))setAcquired(e.target.dataset.key,e.target.checked)});
$$('.segments button').forEach(b=>b.addEventListener('click',()=>{view=b.dataset.view;shown=PAGE;render();scrollTo({top:0,behavior:'smooth'})}));
search.addEventListener('input',()=>{shown=PAGE;render()});sort.addEventListener('change',()=>render());missingOnly.addEventListener('change',()=>render(new Set($$('details.project[open]').map(el=>el.dataset.name))));
fishMode?.addEventListener('change',()=>{try{localStorage.setItem('terraria-shopping-fish-mode-v1',fishMode.checked?'1':'0')}catch(_){}resetFishModeSnapshot();shown=PAGE;render(new Set($$('details.project[open]').map(el=>el.dataset.name)))});
clearSearch.addEventListener('click',()=>{search.value='';search.focus();shown=PAGE;render()});
$('#resetChecks').addEventListener('click',()=>{if(confirm('Clear every acquired checkmark?')){acquired={};save();resetFishModeSnapshot();render()}});
const stamp=D.generatedAt?new Date(D.generatedAt).toLocaleString():null;$('#dataLine').textContent=graphReady?`Official Wiki recipe data • updated ${stamp}`:`Source data checked ${E.checked||'recently'} • full recipe data pending`;
render();
})();