# Tag Gremlin v10 scheduler lab

Public synthetic research lab for the Firefox v10 scheduler. This directory was promoted from the private `polskiftw/gdlp` staging lab after the corrected v10 matrix completed successfully on 2026-08-20.

## Contract

The site is treated as a black-box substring autocomplete oracle:

- query string `q`
- at most 40 popularity-ordered tags containing `q`
- `count < 40` permanently proves `q` CLOSED
- `count == 40` proves only SATURATED
- CLOSED substring certificates may prune every current/future candidate containing that substring
- discovered tag names may be indexed and analyzed arbitrarily
- scheduler completion means every reachable tag discovered **and** proof frontier empty

No scheduler receives hidden corpus state at runtime. Synthetic hidden truth is allowed only for offline labels/oracles.

## What is intentionally *not* sacred

Everything above the site contract is replaceable:

- v9 debt mode
- harvest/prune cadence
- scoring formulas
- candidate horizons
- popularity weighting
- proof strategy
- language model
- controller state machine

The Firefox backend can spend substantial local CPU/RAM to save network requests.

## Native lab

`native_sim.cpp` is the current C++ simulator. Its autocomplete implementation uses a compact 1–4 gram postings index plus exact filtering for longer queries. This produces the same query answers as the earlier all-substrings materialization while dramatically reducing world-index memory.

Parity check on the preserved 1,000-tag seed 60001 balanced world:

- v9: 1088 queries
- gremlin-ai-v1: 812 queries
- learnedprune: 807 queries

These match the previous native/Python laboratory exactly.

## Current contenders

- `v9` — immutable control
- `v1` — first synthetic winner
- `learnedprune` — pass-2 leader; learned CLOSED probability + estimated future certificate shadow
- `softmix` — experimental **debtless** controller. It continuously blends normalized harvest and proof value using a dynamic proof price; there is no debt flag, enter/exit threshold, or harvest/prune cadence.

`softmix` is an experiment, not a presumed improvement. Completeness failure disqualifies any contender immediately.

## Workflows

The repository workflow compiles the simulator with optimized C++ and runs:

- exact parity regression
- six-archetype 25k scale suite
- selected 50k scale worlds
- one 100k scale probe
- a smaller debtless-controller sweep

Every job uploads JSON result artifacts. See `RESULTS.md` for the promotion provenance and corrected private-staging run record.

## Next research architecture

The intended next leap is rollout-trained value ranking rather than another hand-tuned formula:

1. snapshot synthetic scheduler states;
2. force diverse legal next queries;
3. finish every clone to full completion;
4. label actions by final remaining-query regret;
5. train only on runtime-observable features;
6. deploy the learned ranker and repeat.

That permits the controller to learn long-horizon discovery/proof tradeoffs instead of assuming a debt system at all.
