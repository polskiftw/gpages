# Site tag-grammar corrections — 2026-08-20

The real site supports tag/search characters `a-z`, `0-9`, and `.`. Hyphen (`-`) is **not supported**. A second correction clarified that period is primarily the site's **space / word-boundary character** (for example `red.hair`), not generic punctuation.

These facts define two research correction boundaries. Historical runs are retained for provenance, but results are never mixed across a boundary when drawing conclusions intended to transfer to the real site.

## Correction 1: alphabet

Earlier synthetic universes allowed both `.` and `-`. Any result from those worlds is not site-faithful. In particular, the apparent benefit from probing both period- and hyphen-start subtrees cannot be used as production evidence.

The scheduler algorithms are not automatically wrong, but old measured query counts, learned coefficients, debt conclusions, proof bounds, and punctuation experiments require revalidation under the corrected alphabet.

## Correction 2: period means word boundary

The first period-only generator still treated `.` too much like arbitrary punctuation. The corrected synthetic grammar now models period as a separator between nonempty word-like components.

Canonical synthetic tags therefore:

- contain only lowercase `a-z`, digits `0-9`, and period `.`;
- do not start or end with `.`;
- do not contain `..` (there is no empty word between two spaces);
- use `.` primarily to join conceptual words such as `red.hair`.

Substring queries are less restrictive than complete tag spellings. Queries such as `.hair`, `red.`, or just `.` are valid because they can match an internal word boundary even though a complete tag cannot start or end there.

The **normal tag-prefix frontier knows this grammar for free**. It must not waste network requests proving that leading-period or consecutive-period tag prefixes are impossible. `period_only_sim.py` installs this grammar guard while preserving external period-start substring probes.

## Current corrected contract

- Tag alphabet: `a-z0-9.`.
- Root tag-prefix frontier: `a-z0-9`.
- Right-extension alphabet: `a-z0-9.` subject to the word-boundary grammar.
- Normal frontier rejects leading-period and consecutive-period candidates.
- Generated tags hard-fail on unsupported characters, leading/trailing period, empty dot-separated components, or consecutive periods.
- Period-root research may query `.` and legal continuations `.a`–`.z` and `.0`–`.9` because substring search can match internal word boundaries.
- `..` is not a legal child probe and is not queried merely to rediscover a known syntax rule.

## Correction-run provenance

- `32381519662`: first six-world period-only matrix. Alphabet-correct but period semantics were still too punctuation-like. **Superseded.**
- `32383255086`: six fresh 10k worlds with period generated primarily as a word boundary. The period-depth-2 effect survived, but the normal frontier still spent requests learning that `..` was impossible. **Intermediate evidence; superseded for final query counts.**
- `32383860385`: same semantic worlds with the separator grammar supplied to the normal frontier and impossible `..` probes removed. **Current trustworthy checkpoint.**

On run `32383860385`, learned-prune versus a period-root depth-2 prepass remained complete and improved on all six 10k worlds:

| archetype | learned-prune | period depth 2 | delta |
| --- | ---: | ---: | ---: |
| balanced | 9030 | 8772 | -258 |
| linguistic | 8935 | 8712 | -223 |
| names | 9533 | 9290 | -243 |
| weird | 8112 | 7908 | -204 |
| shortheavy | 8756 | 8513 | -243 |
| compound | 9084 | 8861 | -223 |

Total improvement: **1,394 fewer requests across 60,000 synthetic tags**, or about **232.3 requests per 10k world**, with completeness preserved in every case. This remains synthetic evidence, not a claim about the private real-world database.

## Provenance policy

Old artifacts and commits remain intact as evidence of what was tested. Research notes explicitly label superseded runs rather than rewriting history. New scheduler promotion requires results generated under the full alphabet + word-boundary grammar above.
