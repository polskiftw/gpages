# GDupe

GDupe is a self-contained Windows 10/11 desktop application that watches one folder tree and finds byte-for-byte duplicate images and videos. It keeps its index between launches, updates results as files are created, edited, renamed, moved, or deleted, and never modifies media files itself.

## Use

1. Extract `GDupe-win-x64.zip` anywhere.
2. Run `GDupe.exe`. No installation or .NET runtime is required.
3. Choose a folder. The first scan hashes every supported media file; later scans skip unchanged files.
4. Select a duplicate group, then select a file to preview it. Double-click a row to reveal that file in Explorer.

**Restart scan** cancels the current session safely and starts it again. **Cancel** stops scanning and watching without discarding existing results. Choosing or restarting a folder never deletes, moves, or renames media.

The database, settings, and failure log live in `%LOCALAPPDATA%\GDupe`. Delete that folder to reset GDupe completely.

## Supported media

- Images: JPEG, PNG, GIF, BMP, WebP, TIFF, HEIC, and AVIF
- Videos: MP4, M4V, MOV, AVI, MKV, WebM, WMV, MPEG/MPG, and TS

SHA-256 and filesystem metadata are recorded for all supported files. Width/height are read directly for PNG, JPEG, GIF, and BMP. Duration and display dimensions are read directly from standard MP4/M4V/MOV containers. Unknown metadata is shown as an em dash and does not prevent duplicate detection. Windows supplies previews when an installed codec supports the selected format.

## Reliability behavior

- Files are opened with shared read/delete access so normal applications can keep using them.
- Size and modification time are checked before and after hashing. A moving target is retried three times and logged if it never settles.
- Watcher events are debounced. Watcher overflow or directory-level changes trigger authoritative full reconciliation.
- A cancelled scan does not run deletion reconciliation, so partial scans cannot erase good database rows.
- SQLite uses WAL journaling and indexed hash/size lookups. Exact matches require both identical byte length and SHA-256.

## Build and test

Requirements: Windows 10/11, PowerShell 7 or Windows PowerShell 5.1, and the .NET 8 SDK.

```powershell
cd gdupe
.\build\verify.ps1
.\build\package.ps1
```

The distributable is written to `artifacts\GDupe-win-x64.zip`. GitHub Actions runs the same Release tests and packaging process and uploads the ZIP plus test results.

## Architecture

- `GDupe.Core`: SQLite store, streaming SHA-256, metadata parsing, resilient scanner, and watcher lifecycle
- `GDupe.App`: dependency-free WPF review UI and settings lifecycle
- `GDupe.Tests`: persistence, duplicate grouping, mutation/reconciliation, cancellation, and metadata tests

GDupe is licensed under the repository's PolyForm Noncommercial License 1.0.0.
