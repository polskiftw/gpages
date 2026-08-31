(() => {
'use strict';
const W='https://terraria.wiki.gg/wiki/';
const wikiName=name=>W+encodeURIComponent(String(name).replaceAll(' ','_'));

function decorateRecipeName(strong){
  if(strong.dataset.recipeWiki==='1')return;
  const name=strong.closest('details.project')?.dataset.name;
  if(!name)return;

  const text=String(strong.textContent||'');
  const suffix=text.startsWith(name)?text.slice(name.length):'';
  strong.textContent='';

  const link=document.createElement('a');
  link.href=wikiName(name);
  link.target='_blank';
  link.rel='noopener';
  link.textContent=name;
  link.title=`Open ${name} on the Terraria Wiki`;
  link.setAttribute('aria-label',`${name} — Terraria Wiki`);
  link.style.color='inherit';
  link.style.textDecoration='none';

  strong.append(link);
  if(suffix)strong.append(document.createTextNode(suffix));
  strong.dataset.recipeWiki='1';
}

function decorate(root=document){
  if(root.matches?.('.recipeopt strong'))decorateRecipeName(root);
  root.querySelectorAll?.('.recipeopt strong').forEach(decorateRecipeName);
}

const content=document.querySelector('#content');
if(content){
  decorate(content);
  new MutationObserver(records=>{
    for(const record of records)for(const node of record.addedNodes)if(node.nodeType===1)decorate(node);
  }).observe(content,{childList:true,subtree:true});
}
})();
