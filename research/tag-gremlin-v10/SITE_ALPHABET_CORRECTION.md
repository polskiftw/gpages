# Site tag-grammar corrections — 2026-08-20

The real site supports tag/search characters `a-z`, `0-9`, and `.`. Hyphen (`-`) is **not supported**. A second correction clarified that period is primarily the site's **space / word-boundary character** (for example `red.hair`), not generic punctuation.

These facts define research correction boundaries. Historical runs are retained for provenance, but results are never mixed across a boundary when drawing conclusions intended to transfer to the real site.

## Correction 1: alphabet

Earlier synthetic universes allowed both `.` and `-`. Any result from those worlds is not site-faithful. In particular, apparent benefit from probing both period- and hyphen-start subtrees cannot be used as production evidence.

The scheduler algorithms are not automatically wrong, but old measured query counts, learned coefficients, debt conclusions, proof bounds, and punctuation experiments require revalidation under the corrected alphabet.

## Correction 2: period means word boundary

The first period-only generator still treated `.` too much like arbitrary punctuation. The corrected synthetic grammar models period as a separator between nonempty word-like components.

Canonical synthetic tags therefore:

- contain only lowercase `a-z`, digits `0-9`, and period `.`;
- do not start or end with `.`;
- do not contain `..`;
- use `.` primarily to join conceptual words such as `red.hair`.

Substring queries are less restrictive than complete tag spellings. Queries such as `.hair`, `red.`, or just `.` are valid because they can match an internal word boundary even though a complete tag cannot start or end there.

The **normal tag-prefix frontier knows this grammar for free**. It must not waste network requests proving that leading-period or consecutive-period tag prefixes are impossible. `period_only_sim.py` installs this grammar guard while preserving legal external period-leading substring probes.

## Current corrected contract

- Tag alphabet: `a-z0-9.`.
- Root tag-prefix frontier: `a-z0-9`.
- Right-extension alphabet: `a-z0-9.` subject to word-boundary grammar.
- Normal frontier rejects leading-period and consecutive-period candidates.
- Generated tags hard-fail on unsupported characters, leading/trailing period, empty dot-separated components, or consecutive periods.
- External substring research may query period-leading strings such as `.a`, `.ha`, or `.hair` because they can match internal boundaries.
- `..` is not a legal child probe and is not queried merely to rediscover a known syntax rule.

## Privacy-safe real aggregate calibration

The public synthetic work uses only these aggregate scale facts supplied on 2026-08-20:

- total tags: **330,000**;
- tags containing at least one period: **229,000**;
- undotted tags: **101,000**;
- dotted prevalence: **69.39%**.

No real tag spellings or private database contents are present in the lab.

A one-shot synthetic generator is **not** trusted to preserve this distribution at large N. Its finite lexical templates become exhausted: raw 100k worlds can drift substantially, and a raw one-shot 330k world is invalid for site-prevalence conclusions.

`period_quota_worldgen.py` is the corrected large-scale path. It globally deduplicates many smaller semantic shards, fills separate dotted/plain quotas, then assigns scores and performs the same top-40 reachability repair as normal semantic worlds.

## Period certificate architecture after correction

Early corrected experiments showed that shallow period-boundary probes can remove ordinary proof work, but recursive separator-tree traversal was rejected. A parent can be SAT and nearly all of its children CLOSED while recursively asking those children still loses requests if their certificates do not overlap active ordinary frontier work.

The current architecture therefore uses **direct external certificates**:

- candidate universe currently `.x` and `.xy`;
- runtime value signal = active ordinary-frontier coverage `fsubcnt[q]`;
- runtime risk signal = discovered support `subCAll[q]`;
- CLOSED response (<40) directly prunes every active frontier query containing `q`;
- SAT response is recorded but does not recursively spawn separator children.

The holdout-frozen policy `cov8_s4` requires at least 8 active covered frontier queries and discovered support at most 4.

## Corrected validation checkpoints

The early semantic depth-2 checkpoint remains useful provenance:

- `32381519662`: alphabet-correct but punctuation-like period semantics. **Superseded.**
- `32383255086`: word-boundary period semantics, but normal frontier still rediscovered impossible `..`. **Intermediate.**
- `32383860385`: grammar-corrected normal frontier; static full `.x` depth-2 prepass saved 1,394 requests across six 10k worlds. **Valid historical checkpoint, superseded architecturally by dynamic direct certificates.**

The current direct-certificate validation is stronger:

- `32399582289`: six-world 10k training selected `cov8_s4`; aggregate -1,754 requests; complete 6/6.
- `32400195602`: untouched 30k holdout; aggregate **147,822 -> 138,795**, saving **9,027 (6.11%)**; complete 6/6.
- `32401071349`: raw 100k scheduler-scale holdout; aggregate **318,733 -> 283,185**, saving **35,548 (11.15%)**; complete 4/4, but large-N dotted prevalence drift means this is not demographic validation.
- `32429734718`: exact-prevalence 100k holdout using exactly 69,394 dotted + 30,606 undotted unique tags per world; aggregate **317,775 -> 279,126**, saving **38,649 (12.16%)**; complete 4/4. Only 25 / 2,666 direct probes were SAT (0.94%). **Current strongest scale checkpoint.**

## Full 330k structural result

`period_unique_scale_model.py` globally deduplicates shards until the exact full aggregate 229k dotted + 101k plain vocabulary is filled. Across tested archetypes the one-character post-period buckets `.a` … `.9` saturate at this scale, while most `.xy` buckets remain CLOSED.

In the repaired weird model:

- all 36 `.x` buckets were SAT;
- `.xy`: 76 SAT / 1,256 CLOSED out of 1,332 possible.

This is why static “rare `.q/.v/.x/...`” guesses are not considered real-scale architecture. The useful structural opportunity moves one character deeper, but those deeper strings should be asked **directly**, not reached through a recursively queried separator trie.

## Debt revalidation under the corrected architecture

Run `32429856355` swept debt enter/exit thresholds on six exact-69.39%-prevalence worlds with the direct-certificate policy frozen. Final query counts were essentially unchanged across the ordinary grid; differences were usually zero and otherwise about one or two requests. Debt settings changed discovery timing and frontier peak much more than final network cost.

There is no robust evidence to change the existing `.35/.18` debt hysteresis for the objective of minimizing total requests.

## Direct-certificate oracle result

Run `32430067858` used hidden truth only as a research upper-bound oracle. Production scheduling never receives this information.

- Allowing deeper period-leading candidates beyond `.x`/`.xy` produced **zero additional benefit** in all six fresh exact-prevalence worlds.
- Lowering the oracle's minimum active coverage from 8 to 2 exposed **725 additional requests of theoretical savings across 60k tags** versus the frozen runtime selector.

Therefore the remaining question is not deeper separator traversal. It is whether a runtime-observable risk model can safely exploit low-coverage `.xy` CLOSED candidates.

Run `32462370440` is the corresponding fresh training matrix. It keeps the ordinary scheduler/debt logic fixed, sweeps support-conditioned `.xy` coverage thresholds, and separately records `.x` versus `.xy` CLOSED/SAT outcomes. Any selected replacement must be frozen before a new untouched holdout.

## Provenance policy

Old artifacts and commits remain intact as evidence of what was tested. Research notes explicitly label superseded runs rather than rewriting history. New scheduler promotion requires alphabet-correct, word-boundary-correct, complete runs, and large-scale claims must use quota-calibrated worlds rather than the drifting one-shot generator.
