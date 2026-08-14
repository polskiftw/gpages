(()=>{
'use strict';
if(window.__tagGremlin){window.__tagGremlin.show();return;}

const HOST=location.hostname;
const KEY='tag-gremlin:'+HOST;
const ROOTS=[...'abcdefghijklmnopqrstuvwxyz0123456789._-'];
const NEXT=[...'abcdefghijklmnopqrstuvwxyz0123456789 _-\'.'];
const WAIT_BASE=900;
const WAIT_JITTER=350;

let running=false;
let paused=false;
let state=loadState()||freshState();
let panel=null;
let statusEl=null;
let countEl=null;
let prefixEl=null;
let input=null;

function freshState(){return{version:1,phase:'roots',rootIndex:0,queue:[],seenPrefixes:{},tags:{},cap:0,queries:0,startedAt:null,finished:false};}
function loadState(){try{return JSON.parse(localStorage.getItem(KEY)||'null')}catch{return null}}
function saveState(){try{localStorage.setItem(KEY,JSON.stringify(state))}catch{}}
function sleep(ms){return new Promise(r=>setTimeout(r,ms));}
function jitter(){return WAIT_BASE+Math.floor(Math.random()*WAIT_JITTER);}
function esc(s){return String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}

function findTagInput(){
  const labels=[...document.querySelectorAll('label')];
  for(const l of labels){
    if(/^\s*tags?\s*:?\s*$/i.test(l.textContent||'')){
      if(l.htmlFor){const x=document.getElementById(l.htmlFor);if(x)return x;}
      const x=l.querySelector('input,textarea');if(x)return x;
    }
  }
  const all=[...document.querySelectorAll('body *')];
  for(const el of all){
    const t=(el.textContent||'').trim();
    if(/^tags?:$/i.test(t)||/^tags?$/i.test(t)){
      let p=el;
      for(let i=0;i<5&&p;i++,p=p.parentElement){
        const x=p.querySelector&&p.querySelector('input:not([type]),input[type="text"],input[type="search"],textarea');
        if(x)return x;
      }
    }
  }
  const focused=document.activeElement;
  if(focused&&/^(INPUT|TEXTAREA)$/.test(focused.tagName))return focused;
  const visible=[...document.querySelectorAll('input:not([type]),input[type="text"],input[type="search"],textarea')].filter(isVisible);
  return visible[visible.length-1]||null;
}

function isVisible(el){
  if(!el||!el.getBoundingClientRect)return false;
  const r=el.getBoundingClientRect();
  const s=getComputedStyle(el);
  return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'&&Number(s.opacity)!==0;
}

function setQuery(q){
  input.focus();
  const proto=input.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;
  const setter=Object.getOwnPropertyDescriptor(proto,'value')?.set;
  if(setter)setter.call(input,q);else input.value=q;
  input.dispatchEvent(new Event('input',{bubbles:true}));
  input.dispatchEvent(new Event('change',{bubbles:true}));
  for(const type of ['keydown','keyup']){
    try{input.dispatchEvent(new KeyboardEvent(type,{key:q.slice(-1)||'',bubbles:true}))}catch{}
  }
}

function extractSuggestions(){
  const ir=input.getBoundingClientRect();
  const found=new Map();
  const nodes=[...document.querySelectorAll('li,a,div,span,td')];
  const re=/^\s*(.+?)\s*\(([\d,]+)\)\s*$/;
  for(const el of nodes){
    if(!isVisible(el))continue;
    const r=el.getBoundingClientRect();
    if(r.bottom<ir.top-40||r.top>ir.bottom+900)continue;
    if(r.right<ir.left-500||r.left>ir.right+700)continue;
    const txt=(el.textContent||'').replace(/\s+/g,' ').trim();
    if(!txt||txt.length>140)continue;
    const m=txt.match(re);if(!m)continue;
    const name=m[1].trim();
    if(!name||name.length>100)continue;
    const count=Number(m[2].replace(/,/g,''));
    if(!Number.isFinite(count))continue;
    found.set(name.toLowerCase(),{name:name.toLowerCase(),count});
  }
  return [...found.values()];
}

async function queryPrefix(prefix){
  setQuery(prefix);
  await sleep(jitter());
  let rows=extractSuggestions();
  if(rows.length===0){await sleep(450);rows=extractSuggestions();}
  for(const row of rows){
    const old=state.tags[row.name];
    if(old==null||row.count>old)state.tags[row.name]=row.count;
  }
  state.queries++;
  state.seenPrefixes[prefix]=rows.length;
  saveState();
  refresh(prefix);
  return rows;
}

function enqueueChildren(prefix){
  for(const c of NEXT){
    const q=prefix+c;
    if(state.seenPrefixes[q]===undefined&&!state.queue.includes(q))state.queue.push(q);
  }
}

async function run(){
  if(running)return;
  input=findTagInput();
  if(!input){alert('Tag Gremlin could not find the Tags field. Tap the Tags box, then run the bookmarklet again.');return;}
  running=true;paused=false;state.finished=false;
  if(!state.startedAt)state.startedAt=Date.now();
  setStatus('running');
  try{
    if(state.phase==='roots'){
      while(state.rootIndex<ROOTS.length&&!paused){
        const p=ROOTS[state.rootIndex++];
        const rows=await queryPrefix(p);
        if(rows.length>state.cap)state.cap=rows.length;
        saveState();
      }
      if(!paused){
        state.phase='tree';
        for(const p of ROOTS){if((state.seenPrefixes[p]||0)>=state.cap&&state.cap>0)enqueueChildren(p);}
        saveState();
      }
    }
    while(state.phase==='tree'&&state.queue.length&&!paused){
      const p=state.queue.shift();
      if(state.seenPrefixes[p]!==undefined)continue;
      const rows=await queryPrefix(p);
      if(state.cap>0&&rows.length>=state.cap)enqueueChildren(p);
      saveState();
    }
    if(!paused&&state.phase==='tree'&&!state.queue.length){
      state.finished=true;state.phase='done';saveState();setStatus('finished');
    }
  }catch(err){console.error('Tag Gremlin',err);setStatus('error: '+err.message);}
  finally{running=false;refresh();}
}

function pause(){paused=true;running=false;setStatus('paused');saveState();}
function reset(){
  if(!confirm('Erase Tag Gremlin progress for '+HOST+'?'))return;
  paused=true;running=false;state=freshState();saveState();refresh();setStatus('reset');
}
function tagsArray(){return Object.entries(state.tags).map(([name,count])=>({name,count})).sort((a,b)=>a.name.localeCompare(b.name));}
function copyTags(){
  const text=tagsArray().map(x=>x.name).join('\n');
  navigator.clipboard.writeText(text).then(()=>setStatus('copied '+tagsArray().length+' tags')).catch(()=>fallbackCopy(text));
}
function fallbackCopy(text){
  const ta=document.createElement('textarea');ta.value=text;ta.style.position='fixed';ta.style.left='-9999px';document.body.appendChild(ta);ta.select();document.execCommand('copy');ta.remove();setStatus('copied '+tagsArray().length+' tags');
}
function download(){
  const blob=new Blob([JSON.stringify({host:HOST,exportedAt:new Date().toISOString(),count:tagsArray().length,tags:tagsArray()},null,2)],{type:'application/json'});
  const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download='tags-'+HOST+'.json';document.body.appendChild(a);a.click();setTimeout(()=>{URL.revokeObjectURL(a.href);a.remove();},1500);setStatus('exported');
}

function setStatus(s){if(statusEl)statusEl.textContent=s;}
function refresh(prefix){
  if(countEl)countEl.textContent=Object.keys(state.tags).length.toLocaleString()+' tags • '+state.queries.toLocaleString()+' queries';
  if(prefixEl)prefixEl.textContent='prefix: '+(prefix??'—')+' • cap: '+(state.cap||'?')+' • queued: '+state.queue.length.toLocaleString();
}
function hide(){if(panel)panel.style.display='none';}
function show(){if(panel){panel.style.display='block';refresh();}}

function buildUI(){
  panel=document.createElement('div');
  panel.id='tag-gremlin-panel';
  panel.innerHTML=`<div style="display:flex;align-items:center;gap:8px"><strong style="font-size:17px">Tag Gremlin</strong><span style="margin-left:auto;font-size:12px;opacity:.65">${esc(HOST)}</span><button id="tg-close" style="border:0;background:transparent;font-size:20px;color:inherit">×</button></div><div id="tg-status" style="margin-top:8px;font-weight:700">ready</div><div id="tg-count" style="margin-top:4px;font-size:13px"></div><div id="tg-prefix" style="margin-top:2px;font-size:12px;opacity:.7;word-break:break-all"></div><div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:12px"><button id="tg-start">Start / Resume</button><button id="tg-pause">Pause</button><button id="tg-copy">Copy tags</button><button id="tg-export">Export JSON</button><button id="tg-reset" style="grid-column:1/-1">Reset progress</button></div><div style="font-size:11px;line-height:1.35;opacity:.68;margin-top:10px">Runs slowly on purpose and saves after every query. Safari may pause it when this tab is backgrounded; reopen the page and tap Start / Resume.</div>`;
  Object.assign(panel.style,{position:'fixed',zIndex:'2147483647',left:'12px',right:'12px',top:'max(12px, env(safe-area-inset-top))',background:'rgba(28,28,30,.96)',color:'#fff',padding:'14px',borderRadius:'16px',boxShadow:'0 10px 35px rgba(0,0,0,.35)',fontFamily:'-apple-system,BlinkMacSystemFont,"Helvetica Neue",Arial,sans-serif',backdropFilter:'blur(14px)',webkitBackdropFilter:'blur(14px)'});
  for(const b of panel.querySelectorAll('button'))Object.assign(b.style,{minHeight:'40px',borderRadius:'10px',border:'0',fontWeight:'700',fontSize:'14px'});
  document.documentElement.appendChild(panel);
  statusEl=panel.querySelector('#tg-status');countEl=panel.querySelector('#tg-count');prefixEl=panel.querySelector('#tg-prefix');
  panel.querySelector('#tg-start').onclick=run;panel.querySelector('#tg-pause').onclick=pause;panel.querySelector('#tg-copy').onclick=copyTags;panel.querySelector('#tg-export').onclick=download;panel.querySelector('#tg-reset').onclick=reset;panel.querySelector('#tg-close').onclick=hide;
  refresh();if(state.finished)setStatus('finished');
}

window.__tagGremlin={show,hide,run,pause,reset,state:()=>state};
buildUI();
})();
