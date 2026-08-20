#!/usr/bin/env python3
"""Generate a period-only simulator source from the preserved v10 native source.

The public v10 source historically modeled '-' as a supported continuation.
The real site does not support hyphen. This transformer is deliberately strict:
it only succeeds if the expected legacy fragments are present, then removes the
hyphen continuation/codepoint and makes the learned punctuation feature period-only.
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

    # Explicit guardrails for the three semantic hyphen hooks that mattered.
    if 'abcdefghijklmnopqrstuvwxyz0123456789.-' in src:
        raise RuntimeError('hyphen still present in continuation alphabet')
    if "if(c=='-')" in src:
        raise RuntimeError('hyphen still present in query codepoint mapping')
    if "ch=='-'" in src:
        raise RuntimeError('hyphen still present in punctuation feature')

    Path(args.out).write_text(src, encoding='utf-8', newline='\n')
    print(f'generated period-only simulator: {args.out}')


if __name__ == '__main__':
    main()
