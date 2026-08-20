#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, subprocess, sys, time
from pathlib import Path

def run(cmd):
    t=time.time()
    p=subprocess.run(cmd,text=True,capture_output=True)
    if p.returncode:
        print(p.stdout); print(p.stderr,file=sys.stderr); raise SystemExit(p.returncode)
    rows=[]
    for line in p.stdout.splitlines():
        line=line.strip()
        if line.startswith('{'):
            r=json.loads(line); r['wall_seconds']=time.time()-t; rows.append(r)
    if not rows: raise RuntimeError(f'no JSON result: {cmd}\n{p.stdout}\n{p.stderr}')
    return rows

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--sim',default='./native_sim')
    ap.add_argument('--world',required=True)
    ap.add_argument('--out',required=True)
    ap.add_argument('--include-v9',action='store_true')
    ap.add_argument('--soft-grid',action='store_true')
    a=ap.parse_args()
    policies=[]
    if a.include_v9: policies.append(['v9'])
    policies += [['v1'],['learnedprune','3','1']]
    if a.soft_grid:
        for target in ('.18','.25','.35','.50'):
            for slope in ('3','6','10'):
                policies.append(['softmix',target,slope])
    rows=[]
    for args in policies:
        print('RUN',*args,flush=True)
        rr=run([a.sim,a.world,*args]); rows.extend(rr)
        print(json.dumps(rr[-1],sort_keys=True),flush=True)
    Path(a.out).write_text(json.dumps(rows,indent=2)+'\n')
    complete=[r for r in rows if r.get('complete')]
    if len(complete)!=len(rows):
        raise SystemExit('INVARIANT FAILURE: at least one policy was incomplete')
    best=min(rows,key=lambda r:r['queries'])
    print('BEST',json.dumps(best,sort_keys=True))
if __name__=='__main__': main()
