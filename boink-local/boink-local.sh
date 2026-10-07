#!/usr/bin/env bash
set -u
set -o pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
CONFIG_FILE="$SCRIPT_DIR/config.sh"
STATE_DB="$SCRIPT_DIR/state.sqlite3"
STAGING_DIR="$SCRIPT_DIR/.staging"
SCAN_PAUSE_SECONDS=60
POSTS_PER_SUBREDDIT=100

say() {
    printf '[%(%H:%M:%S)T] %s\n' -1 "$*"
}

die() {
    printf 'boink-local: %s\n' "$*" >&2
    exit 1
}

need_command() {
    command -v "$1" >/dev/null 2>&1 || die "missing required command: $1"
}

random_stem() {
    od -An -N12 -tx1 /dev/urandom | tr -d ' \n'
}

normalize_extension() {
    local ext="${1#.}"
    printf '%s' "${ext,,}"
}

build_filter() {
    local ext out="" sep=""
    for ext in "${FILE_TYPES[@]}"; do
        ext="$(normalize_extension "$ext")"
        [[ "$ext" =~ ^[a-z0-9]+$ ]] || die "invalid FILE_TYPES entry: $ext"
        out+="${sep}'${ext}'"
        sep=","
    done
    [[ -n "$out" ]] || die "FILE_TYPES is empty"
    printf 'extension in (%s)' "$out"
}

validate_subreddit() {
    local sub="$1"
    sub="${sub#/}"
    sub="${sub#r/}"
    sub="${sub#R/}"
    [[ "$sub" =~ ^[A-Za-z0-9_]{3,21}$ ]] || return 1
    printf '%s' "$sub"
}

load_config() {
    [[ -f "$CONFIG_FILE" ]] || die "missing config: $CONFIG_FILE"

    # shellcheck source=/dev/null
    source "$CONFIG_FILE"

    declare -p FILE_TYPES >/dev/null 2>&1 || die "config must define FILE_TYPES as a Bash array"
    declare -p SUBREDDITS >/dev/null 2>&1 || die "config must define SUBREDDITS as a Bash array"
    [[ ${#FILE_TYPES[@]} -gt 0 ]] || die "FILE_TYPES is empty"
    [[ ${#SUBREDDITS[@]} -gt 0 ]] || die "SUBREDDITS is empty; edit config.sh first"
    [[ -n "${SAVE_DIR:-}" ]] || die "SAVE_DIR is empty"

    if [[ "$SAVE_DIR" != /* ]]; then
        SAVE_DIR="$SCRIPT_DIR/$SAVE_DIR"
    fi
    mkdir -p -- "$SAVE_DIR" "$STAGING_DIR"
    SAVE_DIR="$(cd -- "$SAVE_DIR" && pwd -P)"
}

init_state() {
    sqlite3 "$STATE_DB" <<'SQL'
CREATE TABLE IF NOT EXISTS local_saved (
    sha256 TEXT PRIMARY KEY,
    filename TEXT NOT NULL
);
SQL
}

already_saved() {
    local hash="$1"
    [[ "$(sqlite3 "$STATE_DB" "SELECT 1 FROM local_saved WHERE sha256='${hash}' LIMIT 1;")" == "1" ]]
}

record_saved() {
    local hash="$1" filename="$2"
    sqlite3 "$STATE_DB" \
        "INSERT OR IGNORE INTO local_saved(sha256, filename) VALUES('${hash}', '${filename}');"
}

allowed_extension() {
    local wanted ext
    ext="$(normalize_extension "$1")"
    for wanted in "${FILE_TYPES[@]}"; do
        [[ "$ext" == "$(normalize_extension "$wanted")" ]] && return 0
    done
    return 1
}

save_candidate() {
    local path="$1" name ext hash bytes stem final
    name="${path##*/}"

    [[ "$name" == *.part ]] && return 0
    [[ -f "$path" ]] || return 0

    if [[ "$name" == *.* ]]; then
        ext="$(normalize_extension "${name##*.}")"
    else
        rm -f -- "$path"
        return 0
    fi

    if ! allowed_extension "$ext"; then
        rm -f -- "$path"
        return 0
    fi

    read -r hash _ < <(sha256sum -- "$path") || return 1
    bytes="$(stat -c '%s' -- "$path")" || return 1

    if already_saved "$hash"; then
        rm -f -- "$path"
        return 0
    fi

    while :; do
        stem="$(random_stem)"
        final="$SAVE_DIR/${stem}.${ext}"
        [[ ! -e "$final" ]] && break
    done

    mv -- "$path" "$final" || return 1
    record_saved "$hash" "${stem}.${ext}" || return 1
    say "saved ${stem}.${ext} (${bytes} bytes)"
}

process_staging() {
    local path failed=0
    [[ -d "$STAGING_DIR" ]] || return 0

    while IFS= read -r -d '' path; do
        save_candidate "$path" || failed=1
    done < <(find "$STAGING_DIR" -type f ! -name '*.part' -print0)

    find "$STAGING_DIR" -depth -type d -empty -delete 2>/dev/null || true
    mkdir -p -- "$STAGING_DIR"
    return "$failed"
}

scan_subreddit() {
    local raw="$1" sub run_dir filter rc=0
    sub="$(validate_subreddit "$raw")" || {
        say "skipping invalid subreddit: $raw"
        return 0
    }

    run_dir="$STAGING_DIR/$sub"
    mkdir -p -- "$run_dir"
    filter="$(build_filter)"

    say "checking r/$sub"

    gallery-dl \
        --config-ignore \
        --cookies-from-browser 'firefox/reddit.com' \
        --download-archive "$STATE_DB" \
        --destination "$run_dir" \
        --post-range "1-${POSTS_PER_SUBREDDIT}" \
        --filter "$filter" \
        --sleep-request '2-4' \
        --sleep-429 '60' \
        --retries 4 \
        --timeout 45 \
        -o 'reddit.videos=true' \
        -o 'ytdl.enabled=true' \
        "https://www.reddit.com/r/${sub}/new/" || rc=$?

    process_staging || rc=1

    if (( rc != 0 )); then
        say "r/$sub returned an error; continuing"
    fi
}

stop_now() {
    printf '\n'
    say "stopping"
    process_staging || true
    exit 0
}

main() {
    need_command gallery-dl
    need_command sqlite3
    need_command sha256sum
    need_command stat
    need_command find
    need_command od
    need_command tr

    load_config
    init_state
    process_staging || true

    trap stop_now INT TERM

    say "Boink Local started; Ctrl+C to stop"
    say "saving to $SAVE_DIR"

    while :; do
        local sub
        for sub in "${SUBREDDITS[@]}"; do
            scan_subreddit "$sub"
        done

        if [[ "${BOINK_LOCAL_ONCE:-0}" == "1" ]]; then
            break
        fi

        say "scan complete; sleeping ${SCAN_PAUSE_SECONDS}s"
        sleep "$SCAN_PAUSE_SECONDS" || true
    done
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
