# Tag Gremlin v10 — current period-only results

The real-site transfer contract is now `a-z0-9.` only, with period used primarily as a word boundary. Complete tag spellings have no leading/trailing period and no `..`. Historical results from the earlier hyphen-capable synthetic alphabet are preserved later in this file for provenance but are not used for current conclusions.

The only private-database facts used by this public research are privacy-safe aggregate constraints supplied on 2026-08-20: 330,000 total tags, 229,000 tags containing at least one period, 101,000 undotted tags, and therefore 69.39% dotted prevalence. No real tag vocabulary is present in this repository.

## Current scheduler architecture

The useful separator optimization is **direct external period certificates**, not recursive traversal of a special period-root trie. The runtime selector considers `.x` and `.xy` substring probes and uses only live observable state:

- `fsubcnt[q]`: active normal-frontier queries containing `q`;
- `subCAll[q]`: support for `q` among tags already discovered.

The frozen holdout policy `cov8_s4` probes a candidate when active coverage is at least 8 and discovered support is at most 4. A CLOSED response (<40 matches) is a certificate that every active normal-frontier query containing that substring can be removed. A SAT response is recorded but does not recursively spawn separator children.

Recursive period traversal was rejected experimentally: deeper children can be mostly CLOSED while still increasing total requests because those certificates may not overlap the ordinary proof burden. Direct certificates avoid that traversal tax.

## 10k policy training

Run `32399582289` trained the original direct-certificate thresholds on six fresh semantic archetypes. `cov8_s4` was selected before holdout.

- aggregate savings versus learned-prune: **1,754 requests / 60k tags**;
- mean savings: **292.3 per 10k world**;
- direct probes: 109;
- SAT probes: 3 (2.75%);
- completeness: 6/6.

## Untouched 30k holdout

Run `32400195602`, frozen `cov8_s4`, 6/6 complete:

| archetype | learned-prune | cov8_s4 | delta |
| --- | ---: | ---: | ---: |
| balanced | 18,713 | 17,095 | -1,618 |
| linguistic | 16,970 | 15,541 | -1,429 |
| names | 18,568 | 17,282 | -1,286 |
| weird | 56,953 | 55,427 | -1,526 |
| shortheavy | 20,467 | 18,569 | -1,898 |
| compound | 16,151 | 14,881 | -1,270 |
| **total** | **147,822** | **138,795** | **-9,027** |

Aggregate reduction: **6.11%**. Every world improved and every run completed.

## Raw 100k scale holdout

Run `32401071349`, 4/4 complete:

| archetype | learned-prune | cov8_s4 | delta |
| --- | ---: | ---: | ---: |
| balanced | 84,108 | 74,187 | -9,921 |
| linguistic | 75,474 | 66,348 | -9,126 |
| weird | 70,490 | 64,055 | -6,435 |
| compound | 88,661 | 78,595 | -10,066 |
| **total** | **318,733** | **283,185** | **-35,548** |

Aggregate reduction: **11.15%**. Across 2,548 direct probes, only 27 were SAT (1.06%).

This matrix is retained as scheduler-scale evidence only. The one-shot lexical generator drifts in dotted prevalence as N grows (for example balanced fell to 61.23% dotted and weird to 35.25%), so these worlds are not accepted as demographic replicas of the supplied real aggregate.

## Exact-prevalence 100k holdout

`period_quota_worldgen.py` fixes the large-N generator distortion by globally deduplicating many small semantic shards, filling separate dotted/plain quotas, then applying the normal synthetic score assignment and top-40 reachability repair.

Run `32429734718` used **exactly 69,394 dotted + 30,606 undotted unique tags in every world** and tested the already-frozen `cov8_s4` without retuning:

| archetype | learned-prune | cov8_s4 | delta |
| --- | ---: | ---: | ---: |
| balanced | 84,059 | 74,070 | -9,989 |
| linguistic | 70,792 | 61,803 | -8,989 |
| weird | 79,468 | 69,652 | -9,816 |
| compound | 83,456 | 73,601 | -9,855 |
| **total** | **317,775** | **279,126** | **-38,649** |

Aggregate reduction: **12.16%**. The matrix is 4/4 complete. It issued 2,666 direct probes, of which 25 were SAT (**0.94%**). This is the strongest current scale validation because the dotted prevalence is fixed to the supplied real aggregate instead of inherited from a drifting one-shot generator.

## Full-scale structural model

`period_unique_scale_model.py` globally deduplicates synthetic shards until exact unique quotas are filled at the supplied full aggregate: **229,000 dotted + 101,000 undotted = 330,000**.

The important structural transition is stable across tested archetypes: at this scale the one-character post-period buckets `.a` … `.9` are saturated, while the overwhelming majority of two-character post-period `.xy` buckets are CLOSED. The repaired weird model, for example, filled the exact quotas and reported all 36 `.x` buckets SAT but 1,256 / 1,332 `.xy` buckets CLOSED.

That result rejects static “rare initial” guesses at real scale and supports direct `.xy` certificates.

## Debt hysteresis revalidation

Run `32429856355` retrained debt thresholds on six fresh exact-69.39%-prevalence 10k worlds while keeping `cov8_s4` frozen.

Across the ordinary enter/exit grid, final request totals were essentially invariant: most settings tied the `.35/.18` reference exactly and the remaining differences were usually only +/-1 or 2. Always-debt / never-debt extremes changed q99 timing and frontier peak far more than final request count; the largest isolated total difference was compound never-debt at -9.

Conclusion: there is **no robust total-query evidence to replace `debt_enter_ratio=.35` / `debt_exit_ratio=.18`**. Those thresholds stay frozen unless a future objective explicitly optimizes discovery timing or memory/frontier shape instead of total network requests.

## Omniscient direct-certificate headroom

Run `32430067858` used hidden world truth only as a research oracle to measure how much direct-certificate headroom remains. Production selection never receives this information.

Two results are decisive:

1. Expanding the candidate universe beyond `.x`/`.xy` to deeper period-leading grams produced **zero additional benefit on all six fresh exact-prevalence worlds**. Deeper separator strings are not the missing architecture.
2. Allowing an omniscient selector to use known-CLOSED `.x`/`.xy` candidates at active coverage 2 reduced the six-world frozen total from **52,239 to 51,514**, an additional **725 requests** of theoretical headroom. Coverage 4 retained 536 requests of that headroom; coverage 8 retained only 131.

The remaining optimization problem is therefore risk estimation: can runtime-observable evidence identify low-coverage CLOSED `.xy` probes without paying too many SAT guesses?

## Active risk-aware training

Run `32462370440` is training that exact question on six new exact-prevalence 10k worlds. The ordinary scheduler and debt thresholds remain frozen. The harness tests support-conditioned coverage thresholds for `.xy`, separately instruments `.x` and `.xy` CLOSED/SAT outcomes, and includes uniform `cov8_s4` as an exact control. Any winner must be frozen before a new untouched holdout.

---

# Historical corrected-matrix provenance

The material below documents the public lab's promotion from the private staging repository and earlier checkpoints. Some measurements predate the final period-only grammar and are provenance, not current transfer evidence.

## Corrected staging run

- Repository: `polskiftw/gdlp`
- Branch: `research/tag-gremlin-v10`
- Head commit: `5cbe7d38e8d367cdce0e02fdd41720188d0721fd`
- Head commit message: `Fix synthetic reachability repair ordering`
- GitHub Actions run: `32366260913`
- Result: all 19 jobs completed successfully
- Important correction: synthetic exact-tag reachability repair runs longest-to-shortest, followed by a hard top-40 reachability invariant. Invalid synthetic universes abort generation instead of contributing scheduler results.

## Exact parity regression

Preserved 1,000-tag balanced world, seed `60001`:

| policy | queries | complete |
| --- | ---: | :---: |
| v9 | 1088 | yes |
| gremlin-ai-v1 | 812 | yes |
| learnedprune-s3-r1 | 807 | yes |

The corrected staging run reproduced these values exactly. They are retained for regression provenance rather than current site-transfer claims.

## Historical 100k balanced probe

Balanced world, seed `93001`, 100,000 tags:

| policy | queries | q50 | q90 | q99 | complete |
| --- | ---: | ---: | ---: | ---: | :---: |
| gremlin-ai-v1 | 88635 | 16458 | 59011 | 77714 | yes |
| learnedprune-s3-r1 | 88372 | 3325 | 54584 | 78025 | yes |

The learned-prune contender used 263 fewer total queries on this historical probe.

## Artifact integrity manifest from promotion

- `parity-1k`: `c4b9df6866a00e3e7a93a4e2751cabc8128ab9c9b46c698ed2bf787e8192168d`
- `25k-balanced`: `ab89848b7c415a8965ad322afb6887fb8d83a3a92f11be0bd5237bbe43b22d77`
- `25k-linguistic`: `d666a8960badfbf44444f1f279182a2c2f98f6f9af9a76412d9b6cb2628ec13a`
- `25k-names`: `22721e45e9ee9bf35f706ba34ded9e7f5ce9a6cf5d36ee17be3382bea278a679`
- `25k-weird`: `ed681a1692fa89e8c5a9d4b234a2eef115aeb765680d36a8f04148e11b9586a8`
- `25k-shortheavy`: `d3536c8692889169ea568b146d99a54a0bf768641291ffa4fd097e84a3a82f3b`
- `25k-compound`: `446ceca61c7d38b9b521e86b627ea581a3c1ef50148b0b6d408e6086abf4ce65`
- `50k-balanced`: `a9d6b0e5f5eaee3511b680b9dfb3b8bb3b62cb0b693039233ee8c1d6b00b0956`
- `50k-weird`: `685320891e22b87b11c44ce3199782c26f3e6040a6d3f8cee65df24e02540351`
- `50k-compound`: `0d61606686110986dcc6c3ecca9c32f20b37327ee0058c875ebba875f2d1f1da`
- `100k-balanced`: `8da5f9d6ce94c692eee6a95d19a64d1283a5e69e80679b6d50b91a3cdc3293a4`
- `debtless-t0.10-s3`: `845191398c1116df1b1f6052e637ca7beccb8ad4fde73bcc7e75999ef193520a`
- `debtless-t0.15-s3`: `03463fa6a662f17b53b0f3f42d0c9204eed160c03152f54a2b8820c16dbb1610`
- `debtless-t0.20-s3`: `4cf6e2c8af5849ab9a9eaad26f5ac40875280dd48859782973ae5c3480980dbf`
- `debtless-t0.25-s3`: `5fc3edbf8228ed73f7b97f1ea38ec175da3410853ec23fad5cba3298539b5b79`
- `debtless-t0.15-s6`: `9514e3e2fee5a8f6996bfd294e32d93c38de230bf105b392e314a4a898bca6d0`
- `debtless-t0.20-s6`: `368f646293661e526c320a796ff295371e1c8d7ee0bb1ea2d9f72ab75accad08`
- `debtless-t0.25-s6`: `1c979c5f6f597c33e299b3c4b034e8b23291fcfca68b455d33628ba5d6578c5d`
- `debtless-t0.35-s6`: `b6e536e7a5adc7bc6692854ac93b53e1efcfdad410ba857404a178120e13ae65`

## Reproduction policy

Workflows record world sizes, archetypes, deterministic seeds, and hard completeness checks. `worldgen.py` / `period_quota_worldgen.py` abort invalid corpora rather than contributing results. Expensive research matrices are manual-dispatch after their initial launch; ordinary PR integrity checks remain separate.
