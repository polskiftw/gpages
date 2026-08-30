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
const nodeMap=new Map(nodes.map(n=>[n[0],{name:n[0],complexity:Number(n[1])||0,endpoint:!!n[2],direct:Number(n[3])||0,recipeCount:Number(n[4])||0}]));
const curatedByName=new Map(curatedProjects.map(p=>[p.name,p]));
const legacyNameIds=new Map();
for(const [id,i] of Object.entries(items)){const name=i&&i.name;if(!name)continue;if(!legacyNameIds.has(name))legacyNameIds.set(name,[]);legacyNameIds.get(name).push(id)}
const legacyExact=name=>{const ids=legacyNameIds.get(name)||[];return ids.length===1?ids[0]:null};
const fallbackNodes=curatedProjects.map(p=>({name:p.name,complexity:p.items.length,endpoint:true,direct:p.items.reduce((n,x)=>n+Number(x[1]||0),0),recipeCount:0,fallback:true}));
const projectNodes=graphReady?[...nodeMap.values()].filter(n=>n.endpoint):fallbackNodes;
const componentNodes=graphReady?[...nodeMap.values()]:fallbackNodes;

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
  if(!options.length||depth>48)return{leaves:new Map([[name,qty]]),cycles:0,steps:0,tree:{name,qty,leaf:true}};
  if(stack.has(name))return{leaves:new Map([[name,qty]]),cycles:1,steps:0,tree:{name,qty,leaf:true,cycle:true}};
  const next=new Set(stack);next.add(name);
  const candidates=[];
  for(const recipe of options){
    const batches=Math.ceil(qty/Math.max(1,Number(recipe.a)||1));
    const leaves=new Map();let cycles=0,steps=1;const children=[];
    for(const pair of recipe.i||[]){
      const childName=String(pair[0]), childQty=Math.max(1,Number(pair[1])||1)*batches;
      const child=planItem(childName,childQty,next,depth+1);
      mergeLeaves(leaves,child.leaves);cycles+=child.cycles;steps+=child.steps;children.push(child.tree);
    }
    candidates.push({leaves,cycles,steps,recipe,tree:{name,qty,recipe,children}});
  }
  const clean=candidates.filter(c=>c.cycles===0);
  if(!clean.length)return{leaves:new Map([[name,qty]]),cycles:1,steps:0,tree:{name,qty,leaf:true,cycle:true}};
  clean.sort((a,b)=>a.leaves.size-b.leaves.size||totalQty(a.leaves)-totalQty(b.leaves)||a.steps-b.steps||String(a.recipe.s).localeCompare(String(b.recipe.s))||JSON.stringify(a.recipe.i).localeCompare(JSON.stringify(b.recipe.i)));
  return clean[0];
}
function curatedPlan(p){
  const leaves=new Map();
  for(const [id,qty] of p.items)addQty(leaves,id,Number(qty)||1);
  return{leaves,curated:true};
}
function shoppingFor(name){const cp=curatedByName.get(name);return cp?curatedPlan(cp):planItem(name,1)}
function treeHtml(node,root=true){
  if(!node)return'';
  const label=`${esc(node.name)}${node.qty>1?` <span class="qty">×${node.qty}</span>`:''}`;
  if(node.leaf)return`<div class="treeleaf">${label}${node.cycle?' <span class="pill generated">cycle stop</span>':''}</div>`;
  const station=node.recipe?.s?` <span class="pill">${esc(node.recipe.s)}</span>`:'';
  return`<details ${root?'open':''}><summary>${label}${station}</summary>${(node.children||[]).map(x=>treeHtml(x,false)).join('')}</details>`;
}
function recipeOptionsHtml(name){
  const opts=byResult.get(name)||[];
  if(!opts.length)return'';
  return`<div class="bodyhead">Direct recipe${opts.length===1?'':'s'} (${opts.length})</div><div class="recipebox">${opts.map(r=>`<div class="recipeopt"><strong>${esc(name)}${Number(r.a)>1?` ×${Number(r.a)}`:''}</strong> <span class="pill">${esc(r.s||'By hand')}</span><div class="recipeingredients">${(r.i||[]).map(x=>`${esc(x[0])}${Number(x[1])>1?` ×${Number(x[1])}`:''}`).join(' • ')}</div></div>`).join('')}</div>`;
}
function curatedItemRow(id,qty){
  const i=items[id]||{name:id,fish:false,source:'No source annotation yet.',type:'Unannotated'};const key=keyFor(i.name,id);
  if(missingOnly.checked&&!isMissing(key))return'';
  return`<div class="row">${checkbox(key,i.name)}<div><div class="itemname">${esc(i.name)} ${qty>1?`<span class="qty">×${qty}</span>`:''}</div><div class="itemmeta"><span class="pill curated">CURATED SOURCE</span>${avPills(id)}</div><div class="source">${i.fish?'<span class="pill fish">FISHING ROUTE</span> ':'<span class="pill remaining">REMAINING</span> '}${esc(i.fish?(i.fishSource||i.source):i.source)}</div>${i.note?`<div class="note">${esc(i.note)}</div>`:''}</div><a class="wiki" href="${wikiItem(id)}" target="_blank" rel="noopener">Wiki ↗</a></div>`;
}
function generatedItemRow(name,qty){
  const id=legacyExact(name), i=id?items[id]:null, key=keyFor(name,id);
  if(missingOnly.checked&&!isMissing(key))return'';
  const source=i?(i.fish?(i.fishSource||i.source):i.source):'No hand-verified acquisition annotation yet.';
  const sourcePill=i?(i.fish?'<span class="pill fish">FISHING ROUTE</span> ':'<span class="pill remaining">REMAINING</span> '):'<span class="pill generated">GENERATED LEAF</span> ';
  return`<div class="row">${checkbox(key,name)}<div><div class="itemname">${esc(name)} ${qty>1?`<span class="qty">×${qty}</span>`:''}</div><div class="itemmeta">${i?'<span class="pill curated">CURATED SOURCE</span>':''}${id?avPills(id):''}</div><div class="source">${sourcePill}${esc(source)}</div>${i?.note?`<div class="note">${esc(i.note)}</div>`:''}</div><a class="wiki" href="${id?wikiItem(id):wikiName(name)}" target="_blank" rel="noopener">Wiki ↗</a></div>`;
}
function buildProjectBody(name){
  const cp=curatedByName.get(name), plan=shoppingFor(name);let rows='';let leafCount=0;
  if(cp){for(const [id,qty] of cp.items){const row=curatedItemRow(id,Number(qty)||1);if(row){rows+=row;leafCount++}}}
  else{for(const [leaf,qty] of [...plan.leaves.entries()].sort((a,b)=>a[0].localeCompare(b[0]))){const row=generatedItemRow(leaf,qty);if(row){rows+=row;leafCount++}}}
  if(!rows)rows='<div class="empty">Everything in this shopping list is checked.</div>';
  const note=cp?.note?`<div class="projectnote">${esc(cp.note)}</div>`:'';
  const shopping=`<div class="bodyhead">Shopping list${cp?' • hand-verified override':' • canonical generated route'} (${leafCount})</div><div class="rows">${rows}</div>`;
  let tree='';
  if(graphReady){const generated=planItem(name,1);tree=`<div class="bodyhead">Canonical recipe path</div><div class="treewrap">${treeHtml(generated.tree,true)}</div>`}
  return note+shopping+recipeOptionsHtml(name)+tree;
}
function projectSearchBlob(name){
  const cp=curatedByName.get(name), rs=byResult.get(name)||[];const immediate=rs.flatMap(r=>r.i||[]).map(x=>x[0]);const stations=rs.map(r=>r.s||'');
  let enrich='';if(cp)enrich=[cp.group,cp.mode,cp.when,cp.note,...cp.items.flatMap(x=>{const i=items[x[0]]||{};return[i.name,i.source,i.fishSource,i.type,i.note]})].join(' ');
  return[name,...immediate,...stations,enrich].join(' ').toLowerCase();
}
function nodeSort(a,b){
  if(sort.value==='name')return a.name.localeCompare(b.name);
  if(sort.value==='direct')return b.direct-a.direct||b.complexity-a.complexity||a.name.localeCompare(b.name);
  if(sort.value==='recipes')return b.recipeCount-a.recipeCount||b.complexity-a.complexity||a.name.localeCompare(b.name);
  return b.complexity-a.complexity||b.recipeCount-a.recipeCount||a.name.localeCompare(b.name);
}
function projectCard(n){
  const cp=curatedByName.get(n.name), keyList=cp?cp.items.map(x=>keyFor(items[x[0]]?.name||x[0],x[0])):[];
  const done=keyList.length?keyList.filter(k=>acquired[k]).length:0,pct=keyList.length?Math.round(done/keyList.length*100):0;
  const mode=cp?.mode?`<span class="pill ${modeClass(cp.mode)}">${esc(cp.mode.toUpperCase())}</span>`:'';
  return`<details class="card project" data-name="${esc(n.name)}"><summary class="projectsummary"><div class="projecttop"><div><div class="projecttitle">${esc(n.name)}</div><div class="projectmeta">${cp?'<span class="pill curated">CURATED</span>':'<span class="pill generated">GENERATED</span>'}<span class="pill">${n.complexity} dependenc${n.complexity===1?'y':'ies'}</span><span class="pill">${n.recipeCount||((byResult.get(n.name)||[]).length)} recipe${(n.recipeCount||((byResult.get(n.name)||[]).length))===1?'':'s'}</span>${mode}</div></div><span class="chev">›</span></div></summary>${keyList.length?`<div class="progress"><i style="width:${pct}%"></i></div>`:''}<div class="projectbody" data-loaded="0"><div class="loading">Open to build shopping list…</div></div></details>`;
}
function renderCatalog(openNames=new Set()){
  const base=view==='projects'?projectNodes:componentNodes;const q=search.value.trim().toLowerCase();let list=base;
  if(q)list=list.filter(n=>projectSearchBlob(n.name).includes(q));list=[...list].sort(nodeSort);
  const visible=list.slice(0,shown);const label=view==='projects'?'Terminal projects':'All craftable components';
  let html=`<div class="sectionhead"><h2>${label}</h2><span>${list.length} matches</span></div><div class="projectgrid">${visible.map(projectCard).join('')}</div>`;
  if(list.length>visible.length)html+=`<div class="morewrap"><button class="more" id="showMore">Show ${Math.min(PAGE,list.length-visible.length)} more • ${list.length-visible.length} remaining</button></div>`;
  if(!list.length)html='<div class="card empty">No craftables match the current search.</div>';
  content.innerHTML=html;for(const d of $$('details.project'))if(openNames.has(d.dataset.name)){d.open=true;loadBody(d)}
  $('#showMore')?.addEventListener('click',()=>{const keepOpen=new Set($$('details.project[open]').map(el=>el.dataset.name));shown+=PAGE;render(keepOpen)});renderStats(list.length);
}
function sourceSearchText(id){const i=items[id],a=av(id),ps=curatedProjects.filter(p=>p.items.some(x=>x[0]===id)).map(p=>p.name).join(' ');return[i.name,i.fishSource||'',i.source||'',i.type,a.mode,cleanWhen(a.when),i.note||'',ps].join(' ').toLowerCase()}
function renderSources(wantFish){
  const q=search.value.trim().toLowerCase();let ids=Object.keys(items).filter(id=>!!items[id].fish===wantFish);if(q)ids=ids.filter(id=>sourceSearchText(id).includes(q));if(missingOnly.checked)ids=ids.filter(id=>isMissing(keyFor(items[id].name,id)));
  ids.sort((a,b)=>{if(sort.value==='name')return items[a].name.localeCompare(items[b].name);if(sort.value==='direct'||sort.value==='recipes')return items[a].name.localeCompare(items[b].name);return av(a).rank-av(b).rank||items[a].name.localeCompare(items[b].name)});
  const title=wantFish?'Base ingredients with a fishing route':'Still required after fishing';let html=`<div class="sectionhead"><h2>${title}</h2><span>${ids.length} curated items</span></div><div class="ingredientgrid">`;
  for(const id of ids){const i=items[id],key=keyFor(i.name,id),ps=curatedProjects.filter(p=>p.items.some(x=>x[0]===id));const src=wantFish?(i.fishSource||i.source):i.source;html+=`<article class="card ingredient"><div class="row">${checkbox(key,i.name)}<div><div class="itemname">${esc(i.name)}</div><div class="itemmeta"><span class="pill">${esc(i.type)}</span>${avPills(id)}</div><div class="source">${esc(src)}</div>${i.note?`<div class="note">${esc(i.note)}</div>`:''}<div class="tags">${ps.map(p=>`<span class="tag">${esc(p.name)}</span>`).join('')}</div></div><a class="wiki" href="${wikiItem(id)}" target="_blank" rel="noopener">Wiki ↗</a></div></article>`}
  html+='</div>';if(!ids.length)html=`<div class="card empty">No ${wantFish?'fishable':'remaining'} curated ingredients match.</div>`;content.innerHTML=html;renderStats(ids.length)
}
function renderStats(visible){
  const endpoints=graphReady?Number(D.endpointCount)||projectNodes.length:projectNodes.length, craftables=graphReady?Number(D.craftableCount)||componentNodes.length:componentNodes.length, recipeCount=graphReady?Number(D.recipeCount)||recipes.length:0;
  stats.innerHTML=`<span class="stat"><strong>${visible}</strong> shown/matched</span><span class="stat"><strong>${endpoints.toLocaleString()}</strong> projects</span><span class="stat"><strong>${craftables.toLocaleString()}</strong> craftables</span><span class="stat"><strong>${recipeCount.toLocaleString()}</strong> recipes</span><span class="stat"><strong>${checkedCount()}</strong> checked</span>`;
}
function loadBody(details){const body=details.querySelector('.projectbody');if(!body||body.dataset.loaded==='1')return;body.innerHTML=buildProjectBody(details.dataset.name);body.dataset.loaded='1'}
function render(openNames=new Set()){
  clearSearch.style.display=search.value?'block':'none';$$('.segments button').forEach(b=>b.classList.toggle('active',b.dataset.view===view));
  status.innerHTML=graphReady?'':`<div class="status warn">Full recipe graph has not been generated yet. Showing the existing curated projects until the automatic data refresh succeeds.</div>`;
  if(view==='projects'||view==='components')renderCatalog(openNames);else renderSources(view==='fishable');
}
content.addEventListener('toggle',e=>{const d=e.target;if(d.matches?.('details.project')&&d.open)loadBody(d)},true);
content.addEventListener('change',e=>{if(e.target.matches('.check'))setAcquired(e.target.dataset.key,e.target.checked)});
$$('.segments button').forEach(b=>b.addEventListener('click',()=>{view=b.dataset.view;shown=PAGE;render();scrollTo({top:0,behavior:'smooth'})}));
search.addEventListener('input',()=>{shown=PAGE;render()});sort.addEventListener('change',()=>render());missingOnly.addEventListener('change',()=>render(new Set($$('details.project[open]').map(el=>el.dataset.name))));
clearSearch.addEventListener('click',()=>{search.value='';search.focus();shown=PAGE;render()});
$('#resetChecks').addEventListener('click',()=>{if(confirm('Clear every acquired checkmark?')){acquired={};save();render()}});
const stamp=D.generatedAt?new Date(D.generatedAt).toLocaleString():null;$('#dataLine').textContent=graphReady?`Official Wiki recipe graph • ${Number(D.recipeCount).toLocaleString()} recipes • generated ${stamp}`:`Curated source data checked ${E.checked||'recently'} • recipe graph pending`;
render();
})();
