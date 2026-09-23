# Expanded Worlds THICC x64 Stress Results

## Canonical acceptance run

The full THICC ladder has now been exercised end-to-end on the private x64 Terraria 1.4.5.8 runtime.

- Workflow: `Expanded Worlds THICC x64 stress`
- Run: `#4` / `33871462031`
- Date: `2026-09-04`
- Tested commit: `887245d129a5a200826ae320ea2947f36a8ea16b`
- Seed: `1337420`
- Runner: GitHub-hosted `windows-latest`, one independent VM per THICC tier
- Result: **11 / 11 THICC tiers passed**
- Run URL: https://github.com/polskiftw/gloader/actions/runs/33871462031

Every tier independently:

1. built/staged the matching private x64 Terraria runtime;
2. loaded Expanded Worlds as the only gmod;
3. generated the requested physical dimensions;
4. reached dedicated-server ready state;
5. saved a normal `.wld`;
6. reloaded that same `.wld`;
7. verified the exact expected dimensions after reload;
8. reported realized chest occupancy against Terraria's unchanged `8,000`-chest capacity.

## Results

| Preset | Dimensions | Generation | Peak working set | Peak private memory | `.wld` size | Chests | Reload |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| THICC | `10,600 x 3,000` | `115.255 s` | `1.65 GiB` | `1.62 GiB` | `18.5 MiB` | `746 / 8,000` | PASS |
| THICC 2 | `12,600 x 3,600` | `137.778 s` | `2.23 GiB` | `2.21 GiB` | `26.3 MiB` | `983 / 8,000` | PASS |
| THICC 3 | `14,800 x 4,200` | `296.051 s` | `2.80 GiB` | `2.86 GiB` | `35.9 MiB` | `1,306 / 8,000` | PASS |
| THICC 4 | `16,800 x 4,800` | `431.182 s` | `3.52 GiB` | `3.58 GiB` | `46.4 MiB` | `1,641 / 8,000` | PASS |
| THICC 5 | `19,000 x 5,400` | `611.912 s` | `4.45 GiB` | `4.61 GiB` | `59.3 MiB` | `2,028 / 8,000` | PASS |
| THICC 6 | `21,000 x 6,000` | `851.042 s` | `5.39 GiB` | `5.55 GiB` | `72.2 MiB` | `2,251 / 8,000` | PASS |
| THICC 7 | `23,200 x 6,600` | `1160.289 s` | `6.48 GiB` | `6.63 GiB` | `86.5 MiB` | `2,645 / 8,000` | PASS |
| THICC 8 | `25,200 x 7,200` | `1582.755 s` | `7.61 GiB` | `7.75 GiB` | `101.5 MiB` | `2,892 / 8,000` | PASS |
| THICC 9 | `27,400 x 7,800` | `2066.547 s` | `9.07 GiB` | `9.31 GiB` | `119.4 MiB` | `3,187 / 8,000` | PASS |
| THICC 10 | `29,400 x 8,400` | `2374.512 s` | `10.33 GiB` | `10.56 GiB` | `137.5 MiB` | `3,532 / 8,000` | PASS |
| THICC 11 | `31,600 x 9,000` | `3072.459 s` | `11.82 GiB` | `12.04 GiB` | `156.1 MiB` | `3,855 / 8,000` | PASS |

The hardest tier, **THICC 11**, therefore generated in about **51.21 minutes**, stayed at about **12.04 GiB peak private memory**, produced a **156.1 MiB** world file, used **3,855 / 8,000** chest slots, and reloaded with its exact `31,600 x 9,000` dimensions intact.

## What this proves

For this canonical normal-world seed on the supported Windows x64 path, every public THICC tier is generation/save/reload validated through the design maximum. The old concern that the upper ladder might simply run out of process address space or immediately hit Terraria's audited bookkeeping stores did not materialize.

This is the size-limit acceptance run, not an exhaustive every-secret-seed or every-multiplayer-scenario matrix. Fast CI and source-shape audits continue to guard the underlying continuation/capacity rules separately.

## Why the ladder still stops at THICC 11

Passing THICC 11 does **not** justify adding THICC 12. The next canonical tier is `33,600 x 9,600`; width `33,600` exceeds the signed 16-bit positive coordinate ceiling of `32,767` still present in Terraria paths. The public ladder therefore remains intentionally capped at **THICC 11** until that underlying coordinate contract is redesigned.
