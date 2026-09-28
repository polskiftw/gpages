#!/usr/bin/env python3
"""Build a unique synthetic vocabulary calibrated to real aggregate site counts.

Unlike period_real_scale_model.py, this model does not treat independent shards
as samples with replacement.  Tags are deduplicated globally and accepted into
separate dotted/plain quotas until the requested unique vocabulary is full.
This directly matches the supplied real-site aggregate: 229k dotted + 101k
undotted = 330k unique tag spellings.
"""
from __future__ import annotations
import argparse, collections
from period_scale_probe import generate_tags

K=40
ALNUM='abcdefghijklmnopqrstuvwxyz0123456789'
NEXT='abcdefghijklmnopqrstuvwxyz0123456789.'


def suffix_queries(tag:str,max_after:int):
    out=set()
    for i,ch in enumerate(tag):
        if ch!='.': continue
        for n in range(1,max_after+1):
            z=i+1+n
            if z<=len(tag):
                q=tag[i:z]
                if '..' not in q: out.add(q)
    return out


def possible_level(after:int):
    if after==1: return ['.'+c for c in ALNUM]
    prev=possible_level(after-1)
    out=[]
    for q in prev:
        chars=ALNUM if q.endswith('.') else NEXT
        for c in chars:
            x=q+c
            if '..' not in x: out.append(x)
    return out


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--archetype',default='balanced')
    ap.add_argument('--seed-base',type=int,default=89000)
    ap.add_argument('--shard-n',type=int,default=10000)
    ap.add_argument('--target-total',type=int,default=330000)
    ap.add_argument('--target-dotted',type=int,default=229000)
    ap.add_argument('--max-shards',type=int,default=250)
    ap.add_argument('--max-after',type=int,default=3)
    a=ap.parse_args()
    target_plain=a.target_total-a.target_dotted

    dotted=set(); plain=set(); raw_seen=set(); duplicate_draws=0; raw_draws=0
    shard=0
    while (len(dotted)<a.target_dotted or len(plain)<target_plain) and shard<a.max_shards:
        tags=generate_tags(a.shard_n,a.seed_base+shard,a.archetype)
        shard+=1
        for t in tags:
            raw_draws+=1
            if t in raw_seen:
                duplicate_draws+=1
            else:
                raw_seen.add(t)
            if '.' in t:
                if len(dotted)<a.target_dotted: dotted.add(t)
            else:
                if len(plain)<target_plain: plain.add(t)
        if shard%10==0:
            print(f'FILL shards={shard} dotted={len(dotted)}/{a.target_dotted} plain={len(plain)}/{target_plain} global_unique_seen={len(raw_seen)}',flush=True)

    if len(dotted)<a.target_dotted or len(plain)<target_plain:
        raise RuntimeError(f'could not fill unique quotas after {shard} shards: dotted={len(dotted)} plain={len(plain)}')

    tags=list(dotted)+list(plain)
    counts=collections.Counter()
    hist=collections.Counter(t.count('.') for t in tags)
    periods=sum(t.count('.') for t in dotted)
    for t in dotted:
        for q in suffix_queries(t,a.max_after): counts[q]+=1

    print(f'UNIQUE_SCALE archetype={a.archetype} shards_used={shard} raw_draws={raw_draws} raw_unique_seen={len(raw_seen)} duplicate_draws={duplicate_draws} duplicate_fraction={duplicate_draws/max(1,raw_draws):.6f} total={len(tags)} dotted={len(dotted)} plain={len(plain)} dotted_fraction={len(dotted)/len(tags):.6f} periods_per_dotted={periods/len(dotted):.6f}')
    print('PERIOD_HIST '+' '.join(f'{k}:{v}' for k,v in sorted(hist.items()) if k<=6)+(f' gt6:{sum(v for k,v in hist.items() if k>6)}' if any(k>6 for k in hist) else ''))

    levels={n:possible_level(n) for n in range(1,a.max_after+1)}
    cumulative=1
    for after in range(1,a.max_after+1):
        qs=levels[after]
        if after==1:
            reached=qs
        else:
            parent_sat={q for q in levels[after-1] if counts[q]>=K}
            reached=[q for q in qs if q[:-1] in parent_sat]
        sat=[q for q in qs if counts[q]>=K]
        closed=[q for q in qs if counts[q]<K]
        cumulative+=len(reached)
        print(f'UNIQUE_DEPTH query_len={after+1} possible={len(qs)} sat={len(sat)} closed={len(closed)} reached={len(reached)} reached_sat={sum(counts[q]>=K for q in reached)} reached_closed={sum(counts[q]<K for q in reached)} cumulative={cumulative}')
        nearc=sorted(((counts[q],q) for q in closed if counts[q]>0),reverse=True)[:24]
        nears=sorted((counts[q],q) for q in sat)[:24]
        print('UNIQUE_NEAR_CLOSED '+(' '.join(f'{q}:{n}' for n,q in nearc) if nearc else '-'))
        print('UNIQUE_NEAR_SAT '+(' '.join(f'{q}:{n}' for n,q in nears) if nears else '-'))
        if after==1:
            print('UNIQUE_DEPTH2 '+' '.join(f'{q}:{counts[q]}:{"SAT" if counts[q]>=K else "CLOSED"}' for q in qs))

    rare='qvxz0123456789'
    satparents=[c for c in rare if counts['.'+c]>=K]
    closedparents=[c for c in rare if counts['.'+c]<K]
    child_queries=sum(len(NEXT) for c in satparents)
    child_closed=sum(counts['.'+c+x]<K for c in satparents for x in NEXT)
    child_sat=child_queries-child_closed
    print(f'UNIQUE_RARE parents={rare} parent_sat={"".join(satparents) or "-"} parent_closed={"".join(closedparents) or "-"} recursive_child_queries={child_queries} recursive_child_closed={child_closed} recursive_child_sat={child_sat} total_probe_budget={1+len(rare)+child_queries}')

if __name__=='__main__': main()
