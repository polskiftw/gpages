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
const sourceParts=source=>norm(source).split(/\s*\/\s*/).filter(Boolean);
const treasureBagBoss=part=>{
  const match=norm(part).match(/^Treasure Bag \((.+)\)$/i);
  return match?norm(match[1]):'';
};
const requiresExpert=source=>{
  const parts=sourceParts(source);
  return parts.length>0&&parts.every(part=>!!treasureBagBoss(part));
};
const normalizeTreasureBags=source=>{
  const seen=new Set();
  const parts=[];
  for(const part of sourceParts(source)){
    const boss=treasureBagBoss(part);
    const clean=boss?`${boss} (boss)`:norm(part);
    const key=clean.toLowerCase();
    if(seen.has(key))continue;
    seen.add(key);
    parts.push(clean);
  }
  return parts.join(' / ');
};
const addDifficultyBadge=(pill,label,kind)=>{
  const meta=pill.parentElement;
  if(!meta||meta.querySelector(`.pill.difficulty-${kind}`))return;
  const badge=document.createElement('span');
  badge.className=`pill availability difficulty-${kind}`;
  badge.textContent=label;
  const mode=meta.querySelector('.pill.availability');
  if(mode)mode.insertAdjacentElement('afterend',badge);
  else pill.insertAdjacentElement('beforebegin',badge);
};
const vendorOnly=(source,seller)=>{
  const clean=norm(source).replace(/\s*\(NPC\)\s*$/i,'').trim();
  return clean.toLowerCase()===norm(seller).toLowerCase();
};
const COINS={
  PC:['Platinum Coin','platinum'],
  GC:['Gold Coin','gold'],
  SC:['Silver Coin','silver'],
  CC:['Copper Coin','copper']
};
const priceParts=price=>{
  const parts=[];
  const re=/(\d+)\s*(PC|GC|SC|CC)\b/gi;
  let match;
  while((match=re.exec(norm(price)))){
    const code=match[2].toUpperCase(), coin=COINS[code];
    if(coin)parts.push({amount:Number(match[1]),label:coin[0],kind:coin[1]});
  }
  return parts;
};
const addPriceBadges=(pill,price)=>{
  let anchor=pill;
  for(const coin of priceParts(price)){
    const badge=document.createElement('span');
    badge.className=`pill coin-price coin-${coin.kind}`;
    badge.textContent=`${coin.label} (${coin.amount})`;
    anchor.insertAdjacentElement('afterend',badge);
    anchor=badge;
  }
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
  const name=itemNameFor(pill), rawSource=norm(pill.textContent), expert=requiresExpert(rawSource), source=normalizeTreasureBags(rawSource), vendor=vendors[name];
  if(expert)addDifficultyBadge(pill,'EXPERT','expert');
  if(source!==rawSource)pill.textContent=source;
  if(Array.isArray(vendor)&&vendor.length>=2&&vendorOnly(source,vendor[0])){
    pill.textContent=vendor[0];
    pill.classList.add('source-purchase');
    addPriceBadges(pill,vendor[1]);
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
