# Site alphabet correction — 2026-08-20

The real site supports tag/search characters `a-z`, `0-9`, and `.`. Hyphen (`-`) is **not supported**.

This corrects an earlier research assumption that allowed both `.` and `-` in synthetic tags and in frontier continuation. Historical runs that used the old alphabet are retained for provenance, but they are superseded for any conclusion intended to transfer to the real site.

## What is invalidated

Any synthetic result produced before this correction on a universe that allowed `-` is not site-faithful. In particular, the earlier apparent win from probing both period- and hyphen-start query subtrees cannot be used as evidence for production behavior.

The scheduler algorithms themselves are not automatically wrong, but their measured query counts, learned coefficients, debt conclusions, proof bounds, and punctuation experiments must be revalidated on period-only worlds before promotion.

## Corrected contract

- Tag alphabet: lowercase `a-z`, digits `0-9`, period `.`.
- Root frontier: `a-z0-9`.
- Right-extension alphabet: `a-z0-9.`.
- Synthetic generation hard-fails if a hyphen or any unsupported character is emitted.
- CI derives a period-only simulator from the preserved historical native source with `period_only_sim.py`; the transformer fails if the expected legacy hyphen hooks are not found exactly once.
- Period-root experiments may query `.` and its continuations because substring search can match internal periods even when generated tags do not begin with one.

## Provenance policy

Old artifacts and commits remain untouched as historical evidence of what was tested. New corrected runs use fresh seeds and are explicitly labeled `period-only`. Results are not mixed across the correction boundary.
