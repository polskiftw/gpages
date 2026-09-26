# Real-site oracle specification

This file is the benchmark contract. If later observations contradict it, the oracle and every affected benchmark must be versioned and rerun.

## Character model

- Universe: lowercase ASCII `a`–`z`, digits `0`–`9`, period `.`.
- Minimum query length: 1.
- Uppercase is intentionally treated as nonexistent.
- Period has no grammar role in the benchmark. It is an ordinary character.
- Therefore names/queries may begin with `.`, end with `.`, or contain consecutive periods.

## Matching and ordering

For legal query `q`:

1. `P = { tag | tag.name starts_with q }`
2. sort `P` by popularity descending;
3. emit up to 40 entries from `P`;
4. only if fewer than 40 entries have been emitted, define `S = { tag | q occurs in tag.name AND tag not in P }`;
5. sort `S` by popularity descending;
6. append entries from `S` until 40 total or `S` is exhausted.

Thus prefix status outranks popularity across the two groups. A low-popularity prefix match appears before a much more popular substring-only match.

## Cap semantics

The observable response contains at most 40 entries. A response length below 40 is CLOSED: the caller has received every matching tag. A response length of exactly 40 is SATURATED/ambiguous: the caller cannot tell whether exactly 40 or more than 40 tags matched.

## Tie fallback

The real site tie-break is unknown and expected to be rare. The benchmark uses name ascending to make repeated runs deterministic. No production scheduler should depend on the relative ordering of equal-popularity tags. Before final private benchmarking, report only the *number* of queries for which an equal-popularity tie intersects the 40-item cutoff; do not expose the affected names.
