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
    jpg jpeg png webp avif jxl
    gif gifv
    bmp tif tiff
    mp4 m4v webm mov mkv
)

SAVE_DIR="./archive"

SUBREDDITS=(
    pics
    gifs
)
```

Subreddits are names only; use `pics`, not a full Reddit URL.

The configured extension list is a **keep list**, not a limitation on what gallery-dl may inspect or temporarily download. Boink Local lets Reddit's own media handling and gallery-dl child extractors resolve the post first, then keeps only files whose final extension appears in `FILE_TYPES`. This is intentional so Reddit galleries and external hosts are not rejected just because the parent Reddit post does not expose the final media extension yet.

`gifv` is accepted, but GIFV is normally a URL/container convention rather than the actual stored media format. Imgur-style GIFV links commonly resolve to a real `.mp4`, `.gif`, or `.webm`, which is what Boink Local will save.

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

Boink Local runs gallery-dl quietly and ignores any system/user gallery-dl config so the terminal mostly shows its own scan/save lines and its behavior stays self-contained. It reads Reddit cookies directly from the most recently used Firefox profile at runtime; it does not copy a cookie file into this folder.

## Dedupe behavior

There are two layers:

1. gallery-dl records successfully downloaded Reddit/media entries in `state.sqlite3`, so later scans skip items it has already acquired.
2. Every completed download is SHA-256 hashed before it reaches the archive. If the same bytes were already saved under another post or filename, the new copy is deleted instead of being stored again. Each byte-for-byte duplicate encountered prints a terminal line beginning with `DUPE:`.

The final filenames are random 24-character hexadecimal names with the media extension preserved, for example `8ef23375c1971594d2b4893a.jpg`.

Interrupted complete downloads stay in `.staging/` and are processed on the next start. Partial `.part` files are left for gallery-dl to resume.

If you use the default `./archive` destination, deleting this folder removes the tool, config, database, staging files, and collected media together. If `SAVE_DIR` points outside the folder, that external media directory is intentionally left alone.

## Reddit media handling

Boink Local leaves gallery-dl child extractors enabled, enables Reddit video handling, keeps Reddit preview fallback enabled, and enables the yt-dlp fallback. That covers native Reddit images, native Reddit galleries, native Reddit video, and recognized external media hosts supported by gallery-dl such as Imgur and RedGIFs. With ffmpeg installed, yt-dlp can merge DASH/HLS video streams when required.

The local extension keep-list is applied after media resolution/download, so external gallery/host handling is not blocked by an early filename-extension guess.
