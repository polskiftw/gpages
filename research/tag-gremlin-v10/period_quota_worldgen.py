#!/usr/bin/env python3
"""Generate a score-bearing synthetic world with an exact dotted/plain quota.

The ordinary ``worldgen.py`` is useful at small scales, but at large N its finite
lexical template pool is exhausted and the dotted-tag fraction drifts.  This
builder instead draws many small semantic shards, deduplicates globally, and
accepts tags into separate dotted/plain quotas.  It then performs the same score
assignment and reachability repair used by the normal simulator worlds.

This lets full scheduler experiments match the privacy-safe real aggregate
constraint (229k dotted + 101k plain out of 330k) without needing the private DB.
"""
from __future__ import annotations

import argparse
import random
from pathlib import Path

import benchmark as b
from period_scale_probe import generate_tags

K = b.K


def fill_unique_quotas(*, target_total: int, target_dotted: int, seed_base: int,
                       shard_n: int, archetype: str, max_shards: int):
    target_plain = target_total - target_dotted
    if not (0 <= target_dotted <= target_total):
        raise ValueError('target_dotted must be in [0,target_total]')

    dotted: set[str] = set()
    plain: set[str] = set()
    raw_seen: set[str] = set()
    raw_draws = 0
    duplicate_draws = 0

    shards = 0
    while (len(dotted) < target_dotted or len(plain) < target_plain) and shards < max_shards:
        tags = generate_tags(shard_n, seed_base + shards, archetype)
        shards += 1
        for tag in tags:
            raw_draws += 1
            if tag in raw_seen:
                duplicate_draws += 1
            else:
                raw_seen.add(tag)
            if '.' in tag:
                if len(dotted) < target_dotted:
                    dotted.add(tag)
            else:
                if len(plain) < target_plain:
                    plain.add(tag)
        if shards % 10 == 0:
            print(
                f'QUOTA_FILL shards={shards} dotted={len(dotted)}/{target_dotted} '
                f'plain={len(plain)}/{target_plain} global_unique_seen={len(raw_seen)}',
                flush=True,
            )

    if len(dotted) != target_dotted or len(plain) != target_plain:
        raise RuntimeError(
            f'could not fill exact quotas after {shards} shards: '
            f'dotted={len(dotted)}/{target_dotted} plain={len(plain)}/{target_plain}'
        )

    # Sets are sorted before shuffling so the output is reproducible regardless
    # of Python hash randomization.
    tags = sorted(dotted) + sorted(plain)
    return tags, shards, raw_draws, len(raw_seen), duplicate_draws


def assign_scores_and_repair(tags: list[str], *, seed: int, archetype: str):
    """Mirror worldgen.py score assignment + top-40 reachability repair."""
    n = len(tags)
    cfg = b.ARCHETYPES[archetype]
    r = random.Random(seed)
    r.shuffle(tags)

    allowed = set(b.NEXT)
    for tag in tags:
        if any(ch not in allowed for ch in tag):
            raise RuntimeError(f'unsupported character in generated tag: {tag!r}')
        if '-' in tag:
            raise RuntimeError('hyphen leaked into period-only synthetic universe')
        if tag.startswith('.') or tag.endswith('.') or '..' in tag or not all(tag.split('.')):
            raise RuntimeError(f'malformed word-boundary syntax: {tag!r}')

    order = list(range(n))
    r.shuffle(order)
    rank = [0] * n
    for j, i in enumerate(order, 1):
        rank[i] = j
    scores = [
        max(1, int(3_000_000 / (rank[i] ** cfg['skew']) * r.uniform(.72, 1.35)))
        for i in range(n)
    ]

    tag_id = {tag: i for i, tag in enumerate(tags)}
    containers: list[list[int]] = [[] for _ in range(n)]
    for j, tag in enumerate(tags):
        hits: set[int] = set()
        L = len(tag)
        for a in range(L):
            for z in range(a + 2, L + 1):
                i = tag_id.get(tag[a:z])
                if i is not None and i != j:
                    hits.add(i)
        for i in hits:
            containers[i].append(j)

    repaired = 0
    for i in sorted(range(n), key=lambda x: (-len(tags[x]), tags[x])):
        if len(containers[i]) < K:
            continue
        top = sorted((scores[j] for j in containers[i]), reverse=True)[:K]
        kth = top[-1]
        if scores[i] <= kth:
            scores[i] = kth + 1
            repaired += 1

    for i, tag in enumerate(tags):
        before = 0
        si = scores[i]
        for j in containers[i]:
            if scores[j] > si or (scores[j] == si and tags[j] < tag):
                before += 1
                if before >= K:
                    raise RuntimeError(f'unreachable synthetic tag after repair: {tag!r} rank>{K}')

    return scores, repaired


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--target-total', type=int, required=True)
    ap.add_argument('--target-dotted', type=int, required=True)
    ap.add_argument('--seed-base', type=int, required=True)
    ap.add_argument('--score-seed', type=int)
    ap.add_argument('--shard-n', type=int, default=10000)
    ap.add_argument('--max-shards', type=int, default=150)
    ap.add_argument('--archetype', choices=sorted(b.ARCHETYPES), required=True)
    ap.add_argument('--out', required=True)
    a = ap.parse_args()

    score_seed = a.score_seed if a.score_seed is not None else (a.seed_base ^ 0xA0761D6478BD642F)
    tags, shards, raw_draws, raw_unique, duplicates = fill_unique_quotas(
        target_total=a.target_total,
        target_dotted=a.target_dotted,
        seed_base=a.seed_base,
        shard_n=a.shard_n,
        archetype=a.archetype,
        max_shards=a.max_shards,
    )
    scores, repaired = assign_scores_and_repair(tags, seed=score_seed, archetype=a.archetype)

    out = Path(a.out)
    with out.open('w', encoding='utf-8', newline='\n') as f:
        for score, tag in zip(scores, tags):
            f.write(f'{score}\t{tag}\n')

    dotted = sum('.' in tag for tag in tags)
    periods = sum(tag.count('.') for tag in tags)
    print(
        f'QUOTA_WORLD archetype={a.archetype} total={len(tags)} dotted={dotted} '
        f'plain={len(tags)-dotted} dotted_fraction={dotted/len(tags):.6f} '
        f'periods={periods} periods_per_dotted={periods/max(1,dotted):.6f} '
        f'shards_used={shards} raw_draws={raw_draws} raw_unique_seen={raw_unique} '
        f'duplicate_draws={duplicates} duplicate_fraction={duplicates/max(1,raw_draws):.6f} '
        f'reachability_repairs={repaired} score_seed={score_seed} out={a.out}'
    )


if __name__ == '__main__':
    main()
