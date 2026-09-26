# Tag Gremlin: universal anytime optimality

## Status

**Exact universal anytime optimality is impossible under Model M0.**

A finite two-world counterexample forces incompatible first queries for two stopping targets. The contradiction occurs before dataset size, rank, count, or later scheduling logic can matter.

This directory contains the formal model, theorem, concrete substring-realizable witness, and an executable verifier.

## Research question

We asked whether there can exist one deterministic, target-oblivious adaptive query policy whose decisions depend only on observations collected so far and which is exactly optimal for *every* external stopping target.

The desired policy would not be told the hidden dataset size `N` or the user's eventual target `K`. A deployment wrapper could stop at any target, while the scheduler itself would continue to use the same decision rule.

Under the capped deterministic substring-retrieval model defined in `THEOREM.md`, the answer is **no**.

## Core witness

Response cap: `C = 4`.

Two possible hidden worlds contain eight tags each:

```text
D0 = {a0, a1, a2, pc, c4, c5, c6, c7}
D1 = {a0, a1, a2, pd, d8, d9, dx, dy}
```

The deterministic priority orders are chosen so query `c` returns the four `c*` tags in `D0` while `pc` is below the cap, and symmetrically for `d` / `pd` in `D1`.

For stopping target `K=3`, query `a` is the unique first query that guarantees completion in one request.

For stopping target `K=5`, query `p` is the unique first query from which an adaptive policy can guarantee completion in two requests: `p` reveals `pc` or `pd`, identifying the world, then `c` or `d` returns four new tags.

Therefore a target-oblivious policy cannot be exactly optimal for both targets: exact `K=3` optimality forces first query `a`, while exact `K=5` optimality forces first query `p`.

## Scale result

The witness is not a small-dataset artifact. For every `m >= 0`, pad `D0` with `e, ee, ..., e^m` and pad `D1` with `f, ff, ..., f^m`. The two worlds then have equal size `8+m`, and the same contradiction remains. Thus the obstruction exists at **every finite dataset size N >= 8**.

The policy may even be told `N`; it still cannot know the future stopping target and the conflict remains.

## Files

- `THEOREM.md` — model, theorem, proof, padding lemma, scope.
- `verify_counterexample.py` — enumerates every productive substring query and mechanically checks the incompatible optimal first actions.
- `.github/workflows/tag-gremlin-anytime-proof.yml` — CI verifier.

## What this settles — and what it does not

This settles the exact existence question for Model M0: there is no single policy that is simultaneously minimax-optimal at every stopping target.

It does **not** say useful universal policies are impossible. The next mathematically meaningful questions are stronger approximation notions: best competitive ratio, Pareto-optimal discovery curves, lexicographic criteria, or additional structural axioms under which exact anytime optimality might reappear.
