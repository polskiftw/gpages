# ⚠️ Site-alphabet correction (2026-08-20)

The historical results below were produced under an earlier synthetic alphabet that incorrectly allowed hyphen (`-`). The real site supports only `a-z`, `0-9`, and period (`.`). Therefore the old query counts, parity values, debt conclusions, proof bounds, and punctuation experiments are retained **for provenance only** and are superseded for real-site conclusions until reproduced by the period-only correction matrix. See `SITE_ALPHABET_CORRECTION.md`.

---

# Tag Gremlin v10 corrected matrix provenance

This public lab was promoted from the private `polskiftw/gdlp` staging lab after the corrected research matrix completed successfully on 2026-08-20.

## Corrected staging run

- Repository: `polskiftw/gdlp`
- Branch: `research/tag-gremlin-v10`
- Head commit: `5cbe7d38e8d367cdce0e02fdd41720188d0721fd`
- Head commit message: `Fix synthetic reachability repair ordering`
- GitHub Actions run: `32366260913`
- Result: all 19 jobs completed successfully
- Important correction: synthetic exact-tag reachability repair now runs longest-to-shortest, followed by a hard top-40 reachability invariant. Invalid synthetic universes abort generation instead of contributing scheduler results.

## Exact parity regression

Preserved 1,000-tag balanced world, seed `60001`:

| policy | queries | complete |
| --- | ---: | :---: |
| v9 | 1088 | yes |
| gremlin-ai-v1 | 812 | yes |
| learnedprune-s3-r1 | 807 | yes |

The corrected run reproduced these values exactly.

## 100k balanced probe

Balanced world, seed `93001`, 100,000 tags:

| policy | queries | q50 | q90 | q99 | complete |
| --- | ---: | ---: | ---: | ---: | :---: |
| gremlin-ai-v1 | 88635 | 16458 | 59011 | 77714 | yes |
| learnedprune-s3-r1 | 88372 | 3325 | 54584 | 78025 | yes |

The learned-prune contender used 263 fewer total queries on this probe. This is a synthetic result, not a claim about a private real-world tag database.

## Artifact integrity manifest

The staging workflow produced non-expired JSON artifacts for every job. SHA-256 digests recorded at promotion time:

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

## Reproduction

The workflow records every world size, archetype, and deterministic seed. The simulator exits non-zero for incomplete runs; `run_suite.py` also rejects any suite containing an incomplete policy. `worldgen.py` rejects any generated corpus in which an exact tag query would fall outside the oracle's visible top 40.

The expensive corrected matrix is intentionally manual-dispatch in the public repository. Ordinary pushes and pull requests run the exact 1k parity regression as a fast integrity check.
