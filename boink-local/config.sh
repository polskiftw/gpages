# File extensions to keep. Do not include the leading dot.
# Common supported media types:
# jpg jpeg png webp avif jxl gif gifv bmp tif tiff mp4 m4v webm mov mkv
# Note: GIFV is usually a link/container convention rather than a real stored format;
# gallery-dl/yt-dlp normally resolves GIFV-style links to the actual mp4/gif/webm file.
FILE_TYPES=(
    jpg jpeg png webp avif jxl
    gif gifv
    bmp tif tiff
    mp4 m4v webm mov mkv
)

# Seconds to wait after a complete scan of all configured subreddits.
SCAN_PAUSE_SECONDS=60

# Relative paths are resolved from this folder.
SAVE_DIR="./archive"

# Subreddit names only ("pics", not a full Reddit URL).
SUBREDDITS=(
    # pics
    # gifs
)
