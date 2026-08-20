#!/usr/bin/env python3
from __future__ import annotations
import argparse, random
import benchmark as b

K=b.K

def build_tag_table(n:int, seed:int, archetype:str):
    r=random.Random(seed)
    cfg=b.ARCHETYPES[archetype]
    tags=[]; seen=set(); cluster=[]; attempts=0
    while len(tags)<n and attempts<n*150:
        attempts+=1
        u=r.random()
        if cluster and r.random() < cfg['reuse']*.18:
            s=b.mutate(r.choice(cluster),r)
        elif u < cfg['short']:
            s=b.make_short(r)
        elif u < cfg['short']+cfg['proper']:
            s=b.make_proper(r,cfg)
        elif u < cfg['short']+cfg['proper']+cfg['weird']:
            s=b.rand_weird(r)
        else:
            s=b.make_linguistic(r,cfg)
        if len(s)<2 or s in seen: continue
        seen.add(s); tags.append(s)
        if len(s)>=4 and r.random()<.4: cluster.append(s)
    while len(tags)<n:
        s=b.clean(b.rand_weird(r)+str(len(tags)))
        if s not in seen:
            seen.add(s); tags.append(s)

    order=list(range(n)); r.shuffle(order)
    rank=[0]*n
    for j,i in enumerate(order,1): rank[i]=j
    scores=[max(1,int(3_000_000/(rank[i]**cfg['skew'])*r.uniform(.72,1.35))) for i in range(n)]

    tag_id={t:i for i,t in enumerate(tags)}
    containers=[[] for _ in range(n)]
    for j,t in enumerate(tags):
        hits=set(); L=len(t)
        for a in range(L):
            for z in range(a+2,L+1):
                i=tag_id.get(t[a:z])
                if i is not None and i!=j: hits.add(i)
        for i in hits: containers[i].append(j)

    # Reachability repair MUST run longest -> shortest.  A later promotion can
    # only damage exact-query reachability for a tag contained inside the
    # promoted tag.  Therefore fixing long tags first and short tags last makes
    # every repair monotonic: once a long tag is reachable, no subsequent
    # shorter-tag promotion can become one of its containers.
    for i in sorted(range(n), key=lambda x:(-len(tags[x]),tags[x])):
        if len(containers[i])<K: continue
        top=sorted((scores[j] for j in containers[i]), reverse=True)[:K]
        kth=top[-1]
        if scores[i] <= kth: scores[i]=kth+1

    # Hard generation invariant.  The exact query for every tag must rank in
    # the visible top K under the same score-desc/tag-asc ordering used by the
    # emulator.  If this ever trips, the synthetic universe is invalid and no
    # scheduler result from it is allowed to count.
    for i,t in enumerate(tags):
        before=0
        si=scores[i]
        for j in containers[i]:
            if scores[j] > si or (scores[j] == si and tags[j] < t):
                before += 1
                if before >= K:
                    raise RuntimeError(f'unreachable synthetic tag after repair: {t!r} rank>{K}')
    return tags,scores

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--n',type=int,required=True)
    ap.add_argument('--seed',type=int,required=True)
    ap.add_argument('--archetype',choices=sorted(b.ARCHETYPES),required=True)
    ap.add_argument('--out',required=True)
    a=ap.parse_args()
    tags,scores=build_tag_table(a.n,a.seed,a.archetype)
    with open(a.out,'w',encoding='utf-8',newline='\n') as f:
        for score,tag in zip(scores,tags): f.write(f'{score}\t{tag}\n')
    print(f'generated n={len(tags)} seed={a.seed} archetype={a.archetype} out={a.out}')
if __name__=='__main__': main()
