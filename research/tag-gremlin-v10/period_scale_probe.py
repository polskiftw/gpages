#!/usr/bin/env python3
"""Cheap scale probe for period-as-space structure.

This intentionally stops before score assignment / reachability repair because
those operations do not change tag spellings.  It lets us ask the scale question
cheaply at the real-site vocabulary size: which post-period one-character buckets
would be CLOSED (<40) or SATURATED (>=40) purely from spelling prevalence?
"""
from __future__ import annotations
import argparse, collections, random
import benchmark as b
from worldgen import thin_boundaries, bias_boundary_starts, BOUNDARY_BIAS_ALPHABET

K=b.K
ALPHABET='abcdefghijklmnopqrstuvwxyz0123456789'


def generate_tags(n:int, seed:int, archetype:str, boundary_keep:float=1.0,
                  boundary_start_bias:float=0.0):
    r=random.Random(seed)
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
    return tags


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--n',type=int,required=True)
    ap.add_argument('--seed',type=int,required=True)
    ap.add_argument('--archetype',choices=sorted(b.ARCHETYPES),default='balanced')
    ap.add_argument('--boundary-keep',type=float,default=1.0)
    ap.add_argument('--boundary-start-bias',type=float,default=0.0)
    a=ap.parse_args()

    tags=generate_tags(a.n,a.seed,a.archetype,a.boundary_keep,a.boundary_start_bias)
    period_tags=sum('.' in t for t in tags)
    periods=sum(t.count('.') for t in tags)
    per_tag=collections.Counter(t.count('.') for t in tags)
    starts=collections.Counter()
    for t in tags:
        parts=t.split('.')
        for p in parts[1:]:
            if p: starts[p[0]] += 1

    print(f'SCALE n={a.n} seed={a.seed} archetype={a.archetype} period_tags={period_tags} period_fraction={period_tags/a.n:.6f} periods={periods} periods_per_dotted={(periods/period_tags if period_tags else 0):.6f}')
    print('PERIOD_HIST ' + ' '.join(f'{k}:{v}' for k,v in sorted(per_tag.items()) if k<=6) + (f' gt6:{sum(v for k,v in per_tag.items() if k>6)}' if any(k>6 for k in per_tag) else ''))
    for c in ALPHABET:
        n=starts[c]
        print(f'BUCKET .{c} count={n} state={"SAT" if n>=K else "CLOSED"}')
    closed=''.join(c for c in ALPHABET if starts[c] < K)
    sat=''.join(c for c in ALPHABET if starts[c] >= K)
    sparse='qvxz0123456789'
    sparse_closed=''.join(c for c in sparse if starts[c] < K)
    sparse_sat=''.join(c for c in sparse if starts[c] >= K)
    print(f'SUMMARY closed={closed or "-"} sat={sat or "-"} sparse_closed={sparse_closed or "-"} sparse_sat={sparse_sat or "-"} sparse_closed_n={len(sparse_closed)} sparse_sat_n={len(sparse_sat)}')

if __name__=='__main__': main()
