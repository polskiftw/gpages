#!/usr/bin/env python3
"""Generate a site-faithful period-only simulator from the preserved v10 source.

The preserved public v10 source historically modeled '-' as supported.  The real
site accepts only letters, digits, and period, and period is the tag-space/word
separator (for example ``red.hair``), not arbitrary punctuation.

This transformer is deliberately strict: it only succeeds if the expected
legacy fragments are present.  It removes hyphen support and teaches the normal
frontier the separator grammar so it never spends requests on impossible
leading-period or consecutive-period tag prefixes.  External substring probes
such as ``.hair`` remain valid and are handled outside addFrontier().
"""
from __future__ import annotations
import argparse
from pathlib import Path

REPLACEMENTS = [
    (
        'static const string NEXT="abcdefghijklmnopqrstuvwxyz0123456789.-";',
        'static const string NEXT="abcdefghijklmnopqrstuvwxyz0123456789.";',
    ),
    (
        "  if(c=='.') return 37; if(c=='-') return 38; return 0;",
        "  if(c=='.') return 37; return 0;",
    ),
    (
        "punc+=(ch=='.'||ch=='-');",
        "punc+=(ch=='.');",
    ),
    (
        ' bool addFrontier(const string&q,int cr){auto gg=gramCnt.find(q);',
        ' bool addFrontier(const string&q,int cr){if(q.empty()||q[0]==\'.\'||q.find("..")!=string::npos)return false;auto gg=gramCnt.find(q);',
    ),
]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default='native_sim.cpp')
    ap.add_argument('--out', default='native_sim_period_only.cpp')
    args = ap.parse_args()

    src = Path(args.src).read_text(encoding='utf-8')
    for old, new in REPLACEMENTS:
        count = src.count(old)
        if count != 1:
            raise RuntimeError(f'expected exactly one legacy fragment, got {count}: {old!r}')
        src = src.replace(old, new)

    if 'abcdefghijklmnopqrstuvwxyz0123456789.-' in src:
        raise RuntimeError('hyphen still present in continuation alphabet')
    if "if(c=='-')" in src:
        raise RuntimeError('hyphen still present in query codepoint mapping')
    if "ch=='-'" in src:
        raise RuntimeError('hyphen still present in punctuation feature')
    if 'q.find("..")!=string::npos' not in src:
        raise RuntimeError('separator grammar guard was not installed')

    Path(args.out).write_text(src, encoding='utf-8', newline='\n')
    print(f'generated site-faithful period-only simulator: {args.out}')


if __name__ == '__main__':
    main()
