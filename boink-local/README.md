# Boink Local

A tiny local-only descendant of GParty's old **Boink** collector.

It watches the configured subreddits while it is running, saves only media files, gives every saved file a random normalized filename, and never uploads anything anywhere.

## Files

- `boink-local.sh` - the collector; run it in a terminal and press `Ctrl+C` to stop.
- `config.sh` - the only file you normally edit.
- `state.sqlite3` - created automatically. It contains gallery-dl's download archive plus Boink Local's SHA-256 content index.
- `.staging/` - temporary/resumable downloads, created automatically.
- `archive/` - default media destination, created automatically unless `SAVE_DIR` points somewhere else.

No post titles, authors, captions, URLs, or metadata sidecars are saved. The SQLite database contains only the downloader's internal archive identifiers and Boink Local's content hashes/random filenames.

## Config

Edit `config.sh`:

```bash
FILE_TYPES=(
    jpg jpeg png gif webp
    mp4 m4v webm
)

SAVE_DIR="./archive"

SUBREDDITS=(
    pics
    gifs
)
```

Subreddits are names only; use `pics`, not a full Reddit URL.

Relative `SAVE_DIR` paths are relative to this folder. An absolute path can point the archive at another disk while the program, config, staging data, and SQLite state stay here.

## Run

Required commands:

- `gallery-dl`
- `sqlite3`
- standard GNU/Linux utilities (`sha256sum`, `stat`, `find`, `od`, `tr`)

`yt-dlp` is useful for media that gallery-dl hands off to its ytdl extractor.

Start it:

```bash
./boink-local.sh
```

Stop it with `Ctrl+C`.

Boink Local ignores any system/user gallery-dl config so its behavior stays self-contained. It reads Reddit cookies directly from the most recently used Firefox profile at runtime; it does not copy a cookie file into this folder.

## Dedupe behavior

There are two layers:

1. gallery-dl records successfully downloaded Reddit/media entries in `state.sqlite3`, so later scans skip items it has already acquired.
2. Every completed download is SHA-256 hashed before it reaches the archive. If the same bytes were already saved under another post or filename, the new copy is deleted instead of being stored again.

The final filenames are random 24-character hexadecimal names with the media extension preserved, for example `8ef23375c1971594d2b4893a.jpg`.

Interrupted complete downloads stay in `.staging/` and are processed on the next start. Partial `.part` files are left for gallery-dl to resume.

If you use the default `./archive` destination, deleting this folder removes the tool, config, database, staging files, and collected media together. If `SAVE_DIR` points outside the folder, that external media directory is intentionally left alone.
