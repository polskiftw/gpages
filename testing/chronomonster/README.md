# MCU ChronoMonster

Turn the 2,622-step continuous MCU chronology into a VLC virtual edit—or physically encode it into **MCU: THE MOVIE**, a chaptered, resumable Matroska abomination.

The authoritative data is already bundled. The app never uploads, renames, modifies, or deletes your media.

## The five-minute path

1. On Windows, double-click `RUN_CHRONOMONSTER.bat`.
2. Click **ADD FOLDER** and choose the folder containing your MCU media. You can also drag folders onto the window after the optional drag/drop package is installed.
3. Click **RESCAN + MATCH**. High-confidence matches are accepted; questionable and missing works stay visible.
4. Fix problem rows with **MAP FILE** or **USE BEST CANDIDATE**. Click **RESOLVE 9 BOUNDARIES** and mark the exact credit-tag ranges for your editions—or explicitly disable any you do not want.
5. Click **VALIDATE EVERYTHING**. Red items block certified outputs; yellow items are warnings.
6. Click **BUILD VLC PLAYLIST** for the practical version. Open the resulting `.xspf` in VLC and press play. The playlist repeats source files as needed and supplies each exact start/stop range.

Your mappings, boundary choices, scope, profiles, and output history live in a human-readable `.chronomonster.json` project. Save it and reopen it later.

## If you really want one giant file

Click **BUILD THIS ABOMINATION**. The default exact mode:

- decodes each active chronology step for accurate cuts;
- scales to fit a 1080p canvas and pads without stretching;
- normalizes video/audio to a compatible profile;
- verifies every intermediate with ffprobe;
- checkpoints after every segment;
- reuses validated segments after a cancellation or restart;
- concatenates without a second lossy encode;
- writes one chapter per active watch step;
- verifies the finished MKV;
- emits a JSON build receipt containing chronology hash, source identities, profile, FFmpeg version, and exact commands.

Choose volume mode to split only between playback steps. The default target is eight hours. Volume JSON/text indexes and per-volume chapter browsers are written beside the MKVs.

The first full build may require enormous temporary space. The app estimates final and working size before encoding and prints the disk-terror message honestly.

## Required software

- Windows 11 or another OS with Python 3.12+
- FFmpeg/ffprobe for media probing and MKV builds
- VLC for playback/preview

The playlist writer itself does not need FFmpeg. If automatic detection misses portable copies, open **SETTINGS** and select `ffmpeg.exe`, `ffprobe.exe`, and `vlc.exe`.

`INSTALL_WINDOWS.bat` creates a local virtual environment and installs the optional drag/drop support. The core app otherwise uses the Python standard library.

## Important honesty

- The tool uses **2,622 continuous playback steps**, not 5,456 research fragments. The 2,834 fragment boundaries that merely continued the immediately following part of the same source were collapsed.
- Golden check: `Eyes of Wakanda — S01E01` is one step, `00:00:00 → 00:27:20`, collapsing strict ranks 8–19.
- Forty-five source rows are honest whole-file blocks.
- Nine tag/credit scenes lack edition-specific boundaries. A certified output will never guess them.
- Many research boundaries are edition-dependent or subtitle-gap candidates. Runtime/stream checks cannot prove every cut artistically; manual overrides remain available without mutating the bundled chronology.
- The experimental ffconcat plan can attempt `-c copy`, but its in/out cuts may be GOP/keyframe-inexact. It is deliberately not called certified exact mode.
- Monster mode currently selects one preferred/default audio stream and emits no subtitles. VLC playlist mode preserves access to the original file's tracks.

## Scope and custom inclusion

The main scope menu supports All, Core, and Core + Completionist. **TOGGLE WORK SCOPE** disables or restores all steps belonging to the selected work. The boundary resolver can disable an individual unresolved tag. Disabled source-step IDs stay in the project as provenance; generated outputs are renumbered by active sequence.

## Outputs

VLC builds include:

- `.xspf`
- `.build-report.json`
- `.mapping.json`
- `.txt` ordered index

Exact builds include:

- final `.mkv` or numbered volume MKVs
- `.chapters.ffmeta`, `.chapters.json`, and `.chapters.html`
- `.segments.ffconcat`
- `.build-receipt.json`
- `.volumes.json` and `.volumes.txt` when applicable
- `.chronomonster-cache/checkpoint.json` and validated cached segments

Do not delete the cache until the final output is verified and you are sure you will not resume/rebuild.

## Command line

```powershell
python -m chronomonster check-data
python -m chronomonster new MyMCU.chronomonster.json --media D:\MCU
python -m chronomonster scan MyMCU.chronomonster.json
python -m chronomonster validate MyMCU.chronomonster.json --output validation.json
python -m chronomonster xspf MyMCU.chronomonster.json MCU.xspf
python -m chronomonster monster MyMCU.chronomonster.json MCU-THE-MOVIE.mkv
python -m chronomonster monster MyMCU.chronomonster.json MCU.mkv --volumes --volume-hours 8
```

Use `--encoder h264_nvenc` on the target RTX 4070 Super if the selected FFmpeg build exposes that encoder. Software `libx264` remains the quality-oriented fallback.

## Tests

Run the complete dependency-free test runner:

```powershell
python tools\run_tests.py
```

Or install pytest and run `pytest`. The integration test generates tiny non-copyrighted red/blue A → B → A media, checks XSPF repetition/ranges, performs the exact build, verifies its duration and three chapters, reruns to prove three cache hits, and builds three volumes.

See `TEST_RESULTS.md` for the executed build's results and environment. The portable `demo/` folder contains that same style of synthetic project, media, playlist, chapter sidecars, receipt, and verified MKV.

## Privacy

There is no telemetry, login, cloud service, or normal-operation network call. Paths and filenames remain local. Source media is opened read-only by FFmpeg/VLC; generated files are confined to paths you choose and the explicit build cache.

