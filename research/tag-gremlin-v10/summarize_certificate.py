#!/usr/bin/env python3
import glob
import os
import re
import sys


def fields(line):
    out = {}
    for tok in line.strip().split()[1:]:
        if '=' not in tok:
            continue
        k, v = tok.split('=', 1)
        try:
            out[k] = int(v)
        except ValueError:
            try:
                out[k] = float(v)
            except ValueError:
                out[k] = v
    return out

rows = []
for path in sorted(glob.glob(sys.argv[1] if len(sys.argv) > 1 else 'cert-*.txt')):
    cert = logic = base = None
    with open(path, encoding='utf-8-sig') as f:
        for line in f:
            if line.startswith('CERT_BOUND '): cert = fields(line)
            elif line.startswith('SAT_LOGIC '): logic = fields(line)
            elif line.startswith('BASELINE '): base = fields(line)
    if not (cert and logic and base):
        raise SystemExit(f'incomplete certificate output: {path}')
    name = os.path.basename(path)
    m = re.match(r'cert-(.+)-(\d+)-(\d+)\.txt$', name)
    if not m:
        raise SystemExit(f'unexpected filename: {name}')
    archetype, n, seed = m.group(1), int(m.group(2)), int(m.group(3))
    rows.append({
        'archetype': archetype, 'n': n, 'seed': seed,
        'queries': base['queries'], 'closed': base['closed'],
        'inferred': base['saturated_inferred'],
        'logical_lb': logic['logical_total_lb'],
        'gap': logic['baseline_gap_to_logical_lb'],
        'closed_over_floor': base['closed_over_floor'],
        'external': logic['externally_discoverable_non_descendant'],
        'infer_headroom': logic['remaining_infer_headroom'],
    })

if not rows:
    raise SystemExit('no certificate outputs found')

cols = ['archetype','n','seed','queries','logical_lb','gap','closed','closed_over_floor','inferred','external','infer_headroom']
with open('proof-summary.tsv','w',encoding='utf-8') as o:
    o.write('\t'.join(cols)+'\n')
    for r in rows:
        o.write('\t'.join(str(r[c]) for c in cols)+'\n')

qsum = sum(r['queries'] for r in rows)
gsum = sum(r['gap'] for r in rows)
maxgap = max(r['gap'] for r in rows)
exact = sum(r['gap'] == 0 for r in rows)
closed_bad = [r for r in rows if r['closed_over_floor'] != 0]
print(f'PROOF_BATTERY worlds={len(rows)} baseline_queries={qsum} total_improvement_upper_bound={gsum} max_world_upper_bound={maxgap} exact_on_bound={exact} closed_floor_violations={len(closed_bad)}')
for n in sorted({r['n'] for r in rows}):
    rr=[r for r in rows if r['n']==n]
    print(f'PROOF_SCALE n={n} worlds={len(rr)} baseline_queries={sum(r["queries"] for r in rr)} improvement_upper_bound={sum(r["gap"] for r in rr)} max_world_upper_bound={max(r["gap"] for r in rr)} exact_on_bound={sum(r["gap"]==0 for r in rr)}')
for r in rows:
    print('PROOF_WORLD ' + ' '.join(f'{c}={r[c]}' for c in cols))

if closed_bad:
    raise SystemExit('a world exceeded the exact CLOSED certificate floor')
