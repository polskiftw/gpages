#!/usr/bin/env python3
"""Monte-Carlo period-boundary model calibrated to the real site scale.

The base synthetic generator was designed for 10k-ish benchmark worlds.  At very
large n its finite lexical templates become exhausted and fallback identifiers
start to distort the period prevalence.  Rather than pretending that artifact is
real, this tool aggregates many independent 10k spelling worlds, then rescales
period-bearing query counts to the observed site constraint (229k dotted tags out
of 330k total).

Counts use exact substring-query semantics: a tag contributes at most once to a
query even if that query occurs multiple times inside the tag.
"""
from __future__ import annotations
import argparse, collections
from period_scale_probe import generate_tags

K=40
ALNUM='abcdefghijklmnopqrstuvwxyz0123456789'
NEXT='abcdefghijklmnopqrstuvwxyz0123456789.'


def valid_suffixes(max_after:int):
    levels={1:['.'+c for c in ALNUM]}
    for n in range(2,max_after+1):
        cur=[]
        for q in levels[n-1]:
            chars=ALNUM if q.endswith('.') else NEXT
            cur.extend(q+c for c in chars)
        levels[n]=cur
    return levels


def count_tag_queries(tag:str,max_after:int):
    out=set()
    for i,ch in enumerate(tag):
        if ch!='.':
            continue
        for n in range(1,max_after+1):
            z=i+1+n
            if z<=len(tag):
                q=tag[i:z]
                if len(q)==n+1 and not q.startswith('..') and '..' not in q:
                    out.add(q)
    return out


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--archetype',default='balanced')
    ap.add_argument('--shards',type=int,default=33)
    ap.add_argument('--shard-n',type=int,default=10000)
    ap.add_argument('--seed-base',type=int,default=86000)
    ap.add_argument('--target-total',type=int,default=330000)
    ap.add_argument('--target-dotted',type=int,default=229000)
    ap.add_argument('--max-after',type=int,default=3,
                    help='characters after the leading period; 3 means queries through .abc')
    a=ap.parse_args()

    counts=collections.Counter()
    observed_dotted=0
    observed_periods=0
    observed_total=0
    dotted_hist=collections.Counter()
    for s in range(a.shards):
        seed=a.seed_base+s
        tags=generate_tags(a.shard_n,seed,a.archetype)
        observed_total += len(tags)
        for t in tags:
            p=t.count('.')
            dotted_hist[p]+=1
            if p:
                observed_dotted += 1
                observed_periods += p
                for q in count_tag_queries(t,a.max_after):
                    counts[q]+=1

    scale=(a.target_dotted/observed_dotted) if observed_dotted else 0.0
    levels=valid_suffixes(a.max_after)
    predicted={q:int(round(counts[q]*scale)) for qs in levels.values() for q in qs}

    print(f'REAL_SCALE archetype={a.archetype} shards={a.shards} shard_n={a.shard_n} observed_total={observed_total} observed_dotted={observed_dotted} observed_fraction={observed_dotted/observed_total:.6f} target_total={a.target_total} target_dotted={a.target_dotted} target_fraction={a.target_dotted/a.target_total:.6f} rescale={scale:.6f} periods_per_dotted={(observed_periods/observed_dotted if observed_dotted else 0):.6f}')
    print('PERIOD_HIST ' + ' '.join(f'{k}:{v}' for k,v in sorted(dotted_hist.items()) if k<=6) + (f' gt6:{sum(v for k,v in dotted_hist.items() if k>6)}' if any(k>6 for k in dotted_hist) else ''))

    # Cost of an exhaustive separator trie: query root, then every child of a
    # SAT node.  This is a structural upper-cost diagnostic, not a recommendation.
    cumulative=1
    sat_prev=['.']
    for after in range(1,a.max_after+1):
        qs=levels[after]
        vals=[predicted[q] for q in qs]
        sat=[q for q in qs if predicted[q]>=K]
        closed=[q for q in qs if predicted[q]<K]
        zero=[q for q in closed if predicted[q]==0]
        near_closed=sorted(((predicted[q],q) for q in closed if predicted[q]>0), reverse=True)[:24]
        near_sat=sorted((predicted[q],q) for q in sat)[:24]

        # Only children whose immediate parent is SAT are actually reached by an
        # exhaustive stopping trie.  At after=1 the parent is the saturated '.'.
        if after==1:
            reached=qs
        else:
            parent_sat=set(q for q in levels[after-1] if predicted[q]>=K)
            reached=[q for q in qs if q[:-1] in parent_sat]
        cumulative += len(reached)
        reached_sat=sum(predicted[q]>=K for q in reached)
        reached_closed=len(reached)-reached_sat
        print(f'DEPTH query_len={after+1} possible={len(qs)} sat={len(sat)} closed={len(closed)} zero={len(zero)} reached={len(reached)} reached_sat={reached_sat} reached_closed={reached_closed} exhaustive_cumulative_queries={cumulative}')
        print('NEAR_CLOSED ' + (' '.join(f'{q}:{n}' for n,q in near_closed) if near_closed else '-'))
        print('NEAR_SAT ' + (' '.join(f'{q}:{n}' for n,q in near_sat) if near_sat else '-'))

        if after==1:
            print('DEPTH2_BUCKETS ' + ' '.join(f'{q}:{predicted[q]}:{"SAT" if predicted[q]>=K else "CLOSED"}' for q in qs))

if __name__=='__main__':
    main()
