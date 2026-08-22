# Architecture and data contract

The UI is deliberately thin. All correctness-sensitive operations live in importable modules:

| Module | Responsibility |
|---|---|
| `chronology.py` | authoritative 2,622-step load, scope, exact merge audit, resolved ranges |
| `project.py` | atomic project persistence, portable paths, media identity |
| `media.py` | recursive scan, filename normalization/matching, ffprobe cache, stream selection |
| `validation.py` | mappings, streams, outpoints, duplicates, unresolved/reverification gates |
| `playlist.py` | XSPF, standards-compliant file URIs, ffconcat, sidecars |
| `chapters.py` | cumulative chapters, FFmetadata, JSON, HTML browser |
| `build.py` | exact normalized commands, verified segment cache, checkpoint, concat/mux, volumes, receipt |
| `ui.py` | one-window Tk desktop application and edition-boundary resolver |
| `calibration.py` | immutable-reference → local-edition offset, affine, piecewise, and nudge transforms |
| `certification.py` | deduplicated boundary ledger and file/calibration invalidation |
| `certification_ui.py` | keyboard review queue, audit attestation, calibration and subtitle review |
| `subtitles.py` | optional text-subtitle extraction, gap analysis, and experimental proposals |
| `audit.py` | exhaustive VLC boundary-review playlist and JSON batch manifest |
| `cli.py` | automation/debug surface using the same core |

The source chronology is immutable. A manual override is keyed by original `watch_step`, retains stable scene IDs, and records the path/size/mtime identity of the mapped media. If that identity changes, certification fails until the boundary is reverified.

Resolved timestamps are layered as bundled reference → per-work calibration → shared-boundary nudge → explicit manual range. A certificate is valid only when its local timestamp, calibration digest, and mapped-file fingerprint still match. The strict build gate counts unique work/timestamp boundaries rather than work-level confidence states.

The checkpoint is an optimization, not authority. Every reusable segment filename contains a SHA-256 identity derived from the source file identity, range, profile, FFmpeg version, and source watch step; the cached media is also probed before reuse.

Final exact muxing starts only after every active segment passes stream/duration checks. FFmpeg command arrays—not shell strings—are retained in the build receipt for precise auditability without shell-quoting ambiguity.
