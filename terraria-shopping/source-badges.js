(() => {
'use strict';
const fishing=window.TERRARIA_GENERATED_FISHING||{};
const vendors=window.TERRARIA_GENERATED_VENDORS||{};

const norm=s=>String(s||'').replace(/\s+/g,' ').trim();
const itemNameFor=pill=>{
  const item=pill.closest('.row')?.querySelector('.itemname');
  if(!item)return'';
  const clone=item.cloneNode(true);
  clone.querySelectorAll('.qty').forEach(el=>el.remove());
  return norm(clone.textContent);
};
const dedupeTreasureBag=source=>{
  const parts=norm(source).split(/\s*\/\s*/).filter(Boolean);
  const bosses=new Set(parts.map(part=>{
    const match=part.match(/^(.+?)\s*\(boss\)$/i);
    return match?norm(match[1]).toLowerCase():null;
  }).filter(Boolean));
  return parts.filter(part=>{
    const match=part.match(/^Treasure Bag \((.+)\)$/i);
    return !match||!bosses.has(norm(match[1]).toLowerCase());
  }).join(' / ');
};
const vendorOnly=(source,seller)=>{
  const clean=norm(source).replace(/\s*\(NPC\)\s*$/i,'').trim();
  return clean.toLowerCase()===norm(seller).toLowerCase();
};
const fishingSource=(name,source)=>{
  if(/\bfishing\b/i.test(source)||/\bAngler\b.*\b(?:quest|reward)\b/i.test(source))return true;
  const route=String(fishing[name]||'');
  return !!route&&/\b(?:crate|lock box|oyster)\b/i.test(source);
};
const killSource=source=>/\b(?:mob|mobs|boss|bosses|mini-boss|pillar)\b/i.test(source);

function decoratePill(pill){
  if(pill.dataset.sourceSemantic==='1')return;
  pill.dataset.sourceSemantic='1';
  const name=itemNameFor(pill), source=dedupeTreasureBag(pill.textContent), vendor=vendors[name];
  if(source!==norm(pill.textContent))pill.textContent=source;
  if(Array.isArray(vendor)&&vendor.length>=2&&vendorOnly(source,vendor[0])){
    pill.textContent=`${vendor[0]} • ${vendor[1]}`;
    pill.classList.add('source-purchase');
    return;
  }
  if(fishingSource(name,source)){
    pill.classList.add('source-fishing');
    return;
  }
  if(killSource(source))pill.classList.add('source-kill');
}
function decorate(root=document){
  if(root.matches?.('.sourcepill'))decoratePill(root);
  root.querySelectorAll?.('.sourcepill').forEach(decoratePill);
}

const content=document.querySelector('#content');
if(content){
  decorate(content);
  new MutationObserver(records=>{
    for(const record of records)for(const node of record.addedNodes)if(node.nodeType===1)decorate(node);
  }).observe(content,{childList:true,subtree:true});
}
})();
