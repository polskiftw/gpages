#!/usr/bin/env python3
from __future__ import annotations
import argparse, random
import benchmark as b

K=b.K
BOUNDARY_BIAS_ALPHABET='qvxz0123456789'

def thin_boundaries(s:str, r:random.Random, keep:float)->str:
    """Optionally collapse some explicit word boundaries for robustness sweeps.

    keep=1.0 is the normal generator. Lower values turn some `red.hair` style
    spellings into lexical compounds such as `redhair`; they never create
    malformed period syntax. A dedicated RNG is used by build_tag_table so this
    experiment does not perturb the main world-generation random stream.
    """
    if keep >= 1.0 or '.' not in s:
        return s
    out=[]
    for ch in s:
        if ch=='.' and r.random()>=keep:
            continue
        out.append(ch)
    return b.clean(''.join(out))

def bias_boundary_starts(s:str, r:random.Random, rate:float)->str:
    """Adversarially make the sparse-certificate initials common after '.'.

    This is not intended to model natural tag spelling.  It is a stress-test:
    with probability ``rate`` for each post-boundary word, prepend one character
    from q/v/x/z/0-9.  The original word remains intact, so the transform cannot
    erase lexical information and still obeys the site's period-as-space grammar.
    A separate RNG keeps this adversarial dimension isolated from the main world
    generator and from the boundary-prevalence sweep.
    """
    if rate <= 0.0 or '.' not in s:
        return s
    parts=s.split('.')
    for i in range(1,len(parts)):
        if r.random() < rate:
            parts[i]=r.choice(BOUNDARY_BIAS_ALPHABET)+parts[i]
    return b.clean('.'.join(parts))

def build_tag_table(n:int, seed:int, archetype:str, boundary_keep:float=1.0,
                    boundary_start_bias:float=0.0):
    if not 0.0 <= boundary_keep <= 1.0:
        raise ValueError('boundary_keep must be in [0,1]')
    if not 0.0 <= boundary_start_bias <= 1.0:
        raise ValueError('boundary_start_bias must be in [0,1]')
    r=random.Random(seed)
    # Experimental dimensions have independent RNGs so changing them does not
    # reshuffle the generator's main random stream merely by consuming draws.
    rb=random.Random(seed ^ 0x9E3779B97F4A7C15)
    rs=random.Random(seed ^ 0xD1B54A32D192ED03)
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
        s=thin_boundaries(s,rb,boundary_keep)
        s=bias_boundary_starts(s,rs,boundary_start_bias)
        if len(s)<2 or s in seen: continue
        seen.add(s); tags.append(s)
        if len(s)>=4 and r.random()<.4: cluster.append(s)
    while len(tags)<n:
        s=b.clean(b.rand_weird(r)+str(len(tags)))
        s=thin_boundaries(s,rb,boundary_keep)
        s=bias_boundary_starts(s,rs,boundary_start_bias)
        if s not in seen:
            seen.add(s); tags.append(s)

    allowed=set(b.NEXT)
    bad=[t for t in tags if any(ch not in allowed for ch in t)]
    if bad:
        raise RuntimeError(f'unsupported character in generated tag: {bad[0]!r}')
    if any('-' in t for t in tags):
        raise RuntimeError('hyphen leaked into period-only synthetic universe')
    malformed=[t for t in tags if t.startswith('.') or t.endswith('.') or '..' in t or not all(t.split('.'))]
    if malformed:
        raise RuntimeError(f'malformed word-boundary syntax: {malformed[0]!r}')

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

    for i in sorted(range(n), key=lambda x:(-len(tags[x]),tags[x])):
        if len(containers[i])<K: continue
        top=sorted((scores[j] for j in containers[i]), reverse=True)[:K]
        kth=top[-1]
        if scores[i] <= kth: scores[i]=kth+1

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
    ap.add_argument('--boundary-keep',type=float,default=1.0,
                    help='fraction of generated explicit period word boundaries retained; default 1.0')
    ap.add_argument('--boundary-start-bias',type=float,default=0.0,
                    help='adversarial rate for prefixing post-period words with q/v/x/z/digits; default 0')
    ap.add_argument('--out',required=True)
    a=ap.parse_args()
    tags,scores=build_tag_table(a.n,a.seed,a.archetype,a.boundary_keep,a.boundary_start_bias)
    with open(a.out,'w',encoding='utf-8',newline='\n') as f:
        for score,tag in zip(scores,tags): f.write(f'{score}\t{tag}\n')
    period_tags=sum('.' in t for t in tags)
    period_count=sum(t.count('.') for t in tags)
    biased=sum(any(part and part[0] in BOUNDARY_BIAS_ALPHABET for part in t.split('.')[1:]) for t in tags)
    print(f'generated n={len(tags)} seed={a.seed} archetype={a.archetype} boundary_keep={a.boundary_keep:g} boundary_start_bias={a.boundary_start_bias:g} period_tags={period_tags} periods={period_count} sparse_initial_tags={biased} out={a.out}')
if __name__=='__main__': main()
