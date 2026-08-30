(() => {
'use strict';
const W='https://terraria.wiki.gg/wiki/';
const D=window.TERRARIA_RECIPE_DATA||{recipes:[],nodes:[],recipeCount:0,craftableCount:0,endpointCount:0};
const E=window.TERRARIA_ENRICHMENT||{items:{},availability:{},projects:[]};
const items=E.items||{}, availability=E.availability||{}, curatedProjects=E.projects||[];
const $=s=>document.querySelector(s), $$=s=>[...document.querySelectorAll(s)];
const search=$('#search'), sort=$('#sort'), missingOnly=$('#missingOnly'), content=$('#content'), stats=$('#stats'), status=$('#status'), clearSearch=$('#clearSearch');
const PAGE=80;
let view='projects', shown=PAGE;

const recipes=Array.isArray(D.recipes)?D.recipes:[];
const nodes=Array.isArray(D.nodes)?D.nodes:[];
const graphReady=recipes.length>0&&nodes.length>0;
const byResult=new Map();
for(const r of recipes){if(!byResult.has(r.r))byResult.set(r.r,[]);byResult.get(r.r).push(r)}
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
const save=()=>localStorage.setItem('terraria-shopping-progress-v2',JSON.stringify(acquired));
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));
const wikiName=name=>W+encodeURIComponent(String(name).replaceAll(' ','_'));
const wikiItem=id=>W+(items[id]?.wiki||encodeURIComponent(String(items[id]?.name||id).replaceAll(' ','_')));
const av=id=>{const a=availability[id]||['Unknown','',999];return{mode:a[0],when:a[1],rank:a[2]}};
const modeClass=mode=>mode==='Hardmode'?'hard':'pre';
const cleanWhen=when=>String(when||'').replace(/^Available immediately(?:\s*[•/]\s*)?/,'').replace(/^After Wall of Flesh(?:\s*•\s*)?/,'').trim();
const whenPill=when=>{const w=cleanWhen(when);return w?`<span class="pill">${esc(w)}</span>`:''};
const avPills=id=>{const a=av(id);return a.mode==='Unknown'?'':`<span class="pill ${modeClass(a.mode)}">${esc(a.mode.toUpperCase())}</span>${whenPill(a.when)}`};
const keyFor=(name,explicitId=null)=>explicitId?'legacy:'+explicitId:(legacyExact(name)?'legacy:'+legacyExact(name):'gen:'+name);
const isMissing=key=>!acquired[key];
const checkedCount=()=>Object.values(acquired).filter(Boolean).length;
function setAcquired(key,v){acquired[key]=!!v;if(!v)delete acquired[key];save();const open=new Set($$('details.project[open]').map(el=>el.dataset.name));render(open)}
function checkbox(key,label){return `<input class="check" type="checkbox" data-key="${esc(key)}" ${acquired[key]?'checked':''} aria-label="Mark ${esc(label)} acquired">`}
function addQty(map,name,qty){map.set(name,(map.get(name)||0)+qty)}
function mergeLeaves(into,from){for(const [name,qty] of from)addQty(into,name,qty)}
function totalQty(map){let n=0;for(const q of map.values())n+=q;return n}

function planItem(name,qty=1,stack=new Set(),depth=0){
  const options=byResult.get(name)||[];
  if(!options.length||depth>48)return{leaves:new Map([[name,qty]]),cycles:0,steps:0};
  if(stack.has(name))return{leaves:new Map([[name,qty]]),cycles:1,steps:0};
  const next=new Set(stack);next.add(name);
  const candidates=[];
  for(const recipe of options){
    const batches=Math.ceil(qty/Math.max(1,Number(recipe.a)||1));
    const leaves=new Map();let cycles=0,steps=1;
    for(const pair of recipe.i||[]){
      const childName=String(pair[0]), childQty=Math.max(1,Number(pair[1])||1)*batches;
      const child=planItem(childName,childQty,next,depth+1);
      mergeLeaves(leaves,child.leaves);cycles+=child.cycles;steps+=child.steps;
    }
    candidates.push({leaves,cycles,steps,recipe});
  }
  const clean=candidates.filter(c=>c.cycles===0);
  if(!clean.length)return{leaves:new Map([[name,qty]]),cycles:1,steps:0};
  clean.sort((a,b)=>a.leaves.size-b.leaves.size||totalQty(a.leaves)-totalQty(b.leaves)||a.steps-b.steps||String(a.recipe.s).localeCompare(String(b.recipe.s))||JSON.stringify(a.recipe.i).localeCompare(JSON.stringify(b.recipe.i)));
  return clean[0];
}
function shoppingEntries(name){
  if(shoppingCache.has(name))return shoppingCache.get(name);
  const cp=curatedByName.get(name);let entries=[];
  if(cp){
    entries=cp.items.map(([id,qty])=>{const i=items[id]||{name:id};return{id,name:i.name||id,qty:Number(qty)||1,key:keyFor(i.name||id,id),fishable:!!i.fish,info:i}});
  }else{
    const plan=planItem(name,1);
    entries=[...plan.leaves.entries()].map(([leaf,qty])=>{const id=legacyExact(leaf),i=id?items[id]:null;return{id,name:leaf,qty,key:keyFor(leaf,id),fishable:!!i?.fish,info:i}}).sort((a,b)=>a.name.localeCompare(b.name));
  }
  shoppingCache.set(name,entries);
  return entries;
}
function shoppingSummary(name){
  const entries=shoppingEntries(name), total=entries.length, fishable=entries.filter(e=>e.fishable).length, obtained=entries.filter(e=>acquired[e.key]).length;
  return{entries,total,fishable,obtained};
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
const stationGroup=new Map([['Demon Altar',{token:'@evil-altar',names:['Demon Altar','Crimson Altar']}],['Crimson Altar',{token:'@evil-altar',names:['Demon Altar','Crimson Altar']}]]);
function normalizedRecipeKey(r){
  const station=stationGroup.get(String(r.s||''))?.token||String(r.s||'By hand');
  const ingredients=(r.i||[]).map(([name,qty])=>[evilMap.get(String(name))?.token||String(name),Number(qty)||1]).sort((a,b)=>a[0].localeCompare(b[0])||a[1]-b[1]);
  return JSON.stringify([Number(r.a)||1,station,ingredients]);
}
function groupedRecipes(name){
  const groups=new Map();
  for(const r of byResult.get(name)||[]){const key=normalizedRecipeKey(r);if(!groups.has(key))groups.set(key,[]);groups.get(key).push(r)}
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
function shoppingRow(entry){
  if(missingOnly.checked&&!isMissing(entry.key))return'';
  const i=entry.info, source=i?(i.fish?(i.fishSource||i.source):i.source):'';
  const meta=entry.id?avPills(entry.id):'';
  const sourceHtml=source?`<div class="source">${i?.fish?'<span class="pill fish">FISHING ROUTE</span> ':''}${esc(source)}</div>`:'';
  return`<div class="row">${checkbox(entry.key,entry.name)}<div><div class="itemname">${esc(entry.name)} ${entry.qty>1?`<span class="qty">×${entry.qty}</span>`:''}</div>${meta?`<div class="itemmeta">${meta}</div>`:''}${sourceHtml}${i?.note?`<div class="note">${esc(i.note)}</div>`:''}</div><a class="wiki" href="${entry.id?wikiItem(entry.id):wikiName(entry.name)}" target="_blank" rel="noopener">Wiki ↗</a></div>`;
}
function buildProjectBody(name){
  const cp=curatedByName.get(name), summary=shoppingSummary(name);let rows='';
  for(const entry of summary.entries)rows+=shoppingRow(entry);
  if(!rows)rows='<div class="empty">Everything in this shopping list is checked.</div>';
  const note=cp?.note?`<div class="projectnote">${esc(cp.note)}</div>`:'';
  return note+`<div class="bodyhead">Shopping list • ${summary.total} unique item${summary.total===1?'':'s'}</div><div class="rows">${rows}</div>`+recipeOptionsHtml(name);
}
function projectSearchBlob(name){
  const cp=curatedByName.get(name), rs=byResult.get(name)||[];const immediate=rs.flatMap(r=>r.i||[]).map(x=>x[0]);const stations=rs.map(r=>r.s||'');
  let enrich='';if(cp)enrich=[cp.group,cp.mode,cp.when,cp.note,...cp.items.flatMap(x=>{const i=items[x[0]]||{};return[i.name,i.source,i.fishSource,i.type,i.note]})].join(' ');
  return[name,...immediate,...stations,enrich].join(' ').toLowerCase();
}
function nodeSort(a,b){
  if(sort.value==='name')return a.name.localeCompare(b.name);
  return uniqueNeeded(b)-uniqueNeeded(a)||a.name.localeCompare(b.name);
}
function projectCard(n){
  const s=shoppingSummary(n.name);
  const fish=s.fishable?`<span class="pill fish">${s.fishable}/${s.total} FISHABLE</span>`:'<span class="pill hard">LANDLOCKED</span>';
  const obtained=`<span class="pill ${s.obtained===s.total&&s.total?'pre':''}">${s.obtained}/${s.total} OBTAINED</span>`;
  return`<details class="card project" data-name="${esc(n.name)}"><summary class="projectsummary"><div class="projecttop"><div><div class="projecttitle">${esc(n.name)}</div><div class="projectmeta">${fish}${obtained}</div></div><span class="chev">›</span></div></summary><div class="projectbody" data-loaded="0"><div class="loading">Opening shopping list…</div></div></details>`;
}
function renderCatalog(openNames=new Set()){
  const base=view==='projects'?projectNodes:componentNodes;const q=search.value.trim().toLowerCase();let list=base;
  if(q)list=list.filter(n=>projectSearchBlob(n.name).includes(q));list=[...list].sort(nodeSort);
  const visible=list.slice(0,shown),label=view==='projects'?'Projects':'All craftable items';
  let html=`<div class="sectionhead"><h2>${label}</h2><span>${list.length} matches</span></div><div class="projectgrid">${visible.map(projectCard).join('')}</div>`;
  if(list.length>visible.length)html+=`<div class="morewrap"><button class="more" id="showMore">Show ${Math.min(PAGE,list.length-visible.length)} more • ${list.length-visible.length} remaining</button></div>`;
  if(!list.length)html='<div class="card empty">No craftables match the current search.</div>';
  content.innerHTML=html;for(const d of $$('details.project'))if(openNames.has(d.dataset.name)){d.open=true;loadBody(d)}
  $('#showMore')?.addEventListener('click',()=>{const keepOpen=new Set($$('details.project[open]').map(el=>el.dataset.name));shown+=PAGE;render(keepOpen)});renderStats(list.length);
}
function sourceSearchText(id){const i=items[id],a=av(id),ps=curatedProjects.filter(p=>p.items.some(x=>x[0]===id)).map(p=>p.name).join(' ');return[i.name,i.fishSource||'',i.source||'',i.type,a.mode,cleanWhen(a.when),i.note||'',ps].join(' ').toLowerCase()}
function renderSources(wantFish){
  const q=search.value.trim().toLowerCase();let ids=Object.keys(items).filter(id=>!!items[id].fish===wantFish);if(q)ids=ids.filter(id=>sourceSearchText(id).includes(q));if(missingOnly.checked)ids=ids.filter(id=>isMissing(keyFor(items[id].name,id)));
  ids.sort((a,b)=>sort.value==='name'?items[a].name.localeCompare(items[b].name):av(a).rank-av(b).rank||items[a].name.localeCompare(items[b].name));
  const title=wantFish?'Ingredients with a fishing route':'Ingredients still needed on land';let html=`<div class="sectionhead"><h2>${title}</h2><span>${ids.length} items</span></div><div class="ingredientgrid">`;
  for(const id of ids){const i=items[id],key=keyFor(i.name,id),ps=curatedProjects.filter(p=>p.items.some(x=>x[0]===id));const src=wantFish?(i.fishSource||i.source):i.source;html+=`<article class="card ingredient"><div class="row">${checkbox(key,i.name)}<div><div class="itemname">${esc(i.name)}</div><div class="itemmeta"><span class="pill">${esc(i.type)}</span>${avPills(id)}</div><div class="source">${esc(src)}</div>${i.note?`<div class="note">${esc(i.note)}</div>`:''}<div class="tags">${ps.map(p=>`<span class="tag">${esc(p.name)}</span>`).join('')}</div></div><a class="wiki" href="${wikiItem(id)}" target="_blank" rel="noopener">Wiki ↗</a></div></article>`}
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
clearSearch.addEventListener('click',()=>{search.value='';search.focus();shown=PAGE;render()});
$('#resetChecks').addEventListener('click',()=>{if(confirm('Clear every acquired checkmark?')){acquired={};save();render()}});
const stamp=D.generatedAt?new Date(D.generatedAt).toLocaleString():null;$('#dataLine').textContent=graphReady?`Official Wiki recipe data • updated ${stamp}`:`Source data checked ${E.checked||'recently'} • full recipe data pending`;
render();
})();
