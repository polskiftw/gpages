# Boink Local

A tiny local-only descendant of GParty's old **Boink** collector.

It watches the configured subreddits while it is running, saves only media files, gives every saved file a random normalized filename, and never uploads anything anywhere.

## Files

- `boink-local.sh` - the collector; run it in a terminal and press `Ctrl+C` to stop.
- `config.sh` - the only file you normally edit.
- `state.sqlite3` - created automatically. It contains gallery-dl's download archive plus Boink Local's SHA-256 content index.
- `.staging/` - temporary/resumable downloads, created automatically.
- `.errors/` - transient per-subreddit gallery-dl diagnostics, created automatically.
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

POSTS_PER_SUBREDDIT=100
STOP_AFTER_KNOWN_ITEMS=15
SCAN_PAUSE_SECONDS=60

SAVE_DIR="./archive"

SUBREDDITS=(
    pics
    gifs
)
```

Subreddits are names only; use `pics`, not a full Reddit URL.

`POSTS_PER_SUBREDDIT` controls how far each pass asks gallery-dl to walk through the subreddit's `/new` listing. The default `100` means posts 1 through 100, newest first. It is a post count rather than a time window, so how many hours/days it covers depends on how active that subreddit is. Raising it to `500`, for example, asks for posts 1 through 500 if Reddit exposes that much listing history.

`STOP_AFTER_KNOWN_ITEMS` tells gallery-dl to stop the current subreddit after that many consecutive file downloads were skipped because they were already known (for example, already present in its download archive). The default is `15`. A newly downloaded file breaks the streak and the counter starts over. This can end a subreddit pass before `POSTS_PER_SUBREDDIT` is reached once Boink gets back into territory it has already seen. Older local configs that do not define this setting automatically use `15`.

`SCAN_PAUSE_SECONDS` is the delay after one complete pass through all configured subreddits before the next pass begins. The default is 60 seconds.

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

Completed downloads are handed straight back to Boink Local through gallery-dl's synchronous exec postprocessor. That means each file is hashed, deduplicated, moved to the archive, and printed as soon as that individual download finishes rather than being held until the entire subreddit pass is over. The final staging sweep remains as a recovery path for completed files left behind by an interruption or callback failure.

gallery-dl still runs in quiet mode, but its error-level stderr is captured per subreddit in `.errors/`, outside the media staging tree. Explicitly permanent missing-media conditions such as deleted/removed content, 404/NotFound results, and resources that no longer exist are treated as background misses and are not printed. Other failures still print the gallery-dl exit code followed by up to the last eight actionable diagnostic lines prefixed with `ERROR:`. Local hashing/indexing/move failures are reported separately so a downloader problem is not confused with a Boink processing problem.

gallery-dl only adds successful downloads to its normal download archive. That means a permanently unavailable child item may still be encountered and retried silently on a later pass; Boink Local suppresses that expected noise rather than pretending it was successfully archived.

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
