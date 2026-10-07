#!/usr/bin/env bash
set -u
set -o pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
CONFIG_FILE="$SCRIPT_DIR/config.sh"
STATE_DB="$SCRIPT_DIR/state.sqlite3"
STAGING_DIR="$SCRIPT_DIR/.staging"
ERROR_DIR="$SCRIPT_DIR/.errors"

say() {
    printf '[%(%H:%M:%S)T] %s\n' -1 "$*"
}

die() {
    printf 'boink-local: %s\n' "$*" >&2
    exit 1
}

dupe() {
    printf 'DUPE: %s\n' "$*"
}

is_background_media_error() {
    local line="${1,,}"

    case "$line" in
        *deleted*|        *notfounderror*|        *"404 not found"*|        *"http error 404"*|        *"404 client error"*|        *"does not exist"*|        *"no longer exists"*|        *" has been removed"*|        *" was removed"*|        *"content removed"*|        *"media removed"*|        *"post removed"*)
            return 0
            ;;
    esac

    return 1
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

    # Backward-compatible defaults for configs created before these knobs were exposed.
    POSTS_PER_SUBREDDIT="${POSTS_PER_SUBREDDIT:-100}"
    STOP_AFTER_KNOWN_ITEMS="${STOP_AFTER_KNOWN_ITEMS:-15}"
    [[ ${#FILE_TYPES[@]} -gt 0 ]] || die "FILE_TYPES is empty"
    [[ ${#SUBREDDITS[@]} -gt 0 ]] || die "SUBREDDITS is empty; edit config.sh first"
    [[ -n "${SAVE_DIR:-}" ]] || die "SAVE_DIR is empty"
    [[ "${POSTS_PER_SUBREDDIT:-}" =~ ^[1-9][0-9]*$ ]] || die "POSTS_PER_SUBREDDIT must be a positive whole number"
    [[ "${STOP_AFTER_KNOWN_ITEMS:-}" =~ ^[1-9][0-9]*$ ]] || die "STOP_AFTER_KNOWN_ITEMS must be a positive whole number"
    [[ "${SCAN_PAUSE_SECONDS:-}" =~ ^[1-9][0-9]*$ ]] || die "SCAN_PAUSE_SECONDS must be a positive whole number"

    local configured_ext
    for configured_ext in "${FILE_TYPES[@]}"; do
        configured_ext="$(normalize_extension "$configured_ext")"
        [[ "$configured_ext" =~ ^[a-z0-9]+$ ]] || die "invalid FILE_TYPES entry: $configured_ext"
    done

    if [[ "$SAVE_DIR" != /* ]]; then
        SAVE_DIR="$SCRIPT_DIR/$SAVE_DIR"
    fi
    mkdir -p -- "$SAVE_DIR" "$STAGING_DIR" "$ERROR_DIR"
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

saved_filename_for_hash() {
    local hash="$1"
    sqlite3 "$STATE_DB" "SELECT filename FROM local_saved WHERE sha256='${hash}' LIMIT 1;"
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
    local path="$1" name ext hash bytes stem final existing
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
        existing="$(saved_filename_for_hash "$hash")"
        dupe "${name} matches ${existing:-previously-saved media} (${bytes} bytes) - not saved"
        rm -f -- "$path"
        return 0
    fi

    while :; do
        stem="$(random_stem)"
        final="$SAVE_DIR/${stem}.${ext}"
        [[ ! -e "$final" ]] && break
    done

    mv -- "$path" "$final" || return 1
    if ! record_saved "$hash" "${stem}.${ext}"; then
        mv -- "$final" "$path" || true
        return 1
    fi
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
    local raw="$1" sub run_dir error_log gallery_rc=0 staging_rc=0 line
    local ignored_errors=0 start=0 index
    local -a visible_errors=()
    sub="$(validate_subreddit "$raw")" || {
        say "skipping invalid subreddit: $raw"
        return 0
    }

    run_dir="$STAGING_DIR/$sub"
    error_log="$ERROR_DIR/${sub}.log"
    mkdir -p -- "$run_dir"
    : > "$error_log"

    say "checking r/$sub (newest ${POSTS_PER_SUBREDDIT} posts from /new; move on after ${STOP_AFTER_KNOWN_ITEMS} known in a row)"

    BOINK_LOCAL_SCRIPT="$SCRIPT_DIR/boink-local.sh" gallery-dl \
        --quiet \
        --no-colors \
        --config-ignore \
        --cookies-from-browser 'firefox/reddit.com' \
        --download-archive "$STATE_DB" \
        --destination "$run_dir" \
        --post-range "1-${POSTS_PER_SUBREDDIT}" \
        --abort "${STOP_AFTER_KNOWN_ITEMS}" \
        -P exec \
        -O 'command=["{_env[BOINK_LOCAL_SCRIPT]}","--ingest","{_path}"]' \
        -O 'event=after' \
        -O 'output=true' \
        -O 'verbose=false' \
        --sleep-request '2-4' \
        --sleep-429 '60' \
        --retries 4 \
        --http-timeout 45 \
        -o 'reddit.videos=true' \
        -o 'reddit.previews=true' \
        -o 'ytdl.enabled=true' \
        "https://www.reddit.com/r/${sub}/new/" 2>"$error_log" || gallery_rc=$?

    process_staging || staging_rc=$?

    if [[ -s "$error_log" ]]; then
        while IFS= read -r line; do
            [[ -n "$line" ]] || continue
            if is_background_media_error "$line"; then
                ((ignored_errors += 1))
            else
                visible_errors+=("$line")
            fi
        done < "$error_log"
    fi

    if (( gallery_rc != 0 )); then
        if (( ${#visible_errors[@]} > 0 )); then
            say "r/$sub completed with media errors (exit ${gallery_rc}); continuing"
            if (( ${#visible_errors[@]} > 8 )); then
                start=$(( ${#visible_errors[@]} - 8 ))
            fi
            for (( index=start; index<${#visible_errors[@]}; index++ )); do
                printf 'ERROR: %s\n' "${visible_errors[index]}"
            done
        elif (( ignored_errors > 0 && gallery_rc == 4 )); then
            :
        else
            say "r/$sub gallery-dl error (exit ${gallery_rc}); continuing"
            if (( gallery_rc & 4 )); then
                printf 'ERROR: gallery-dl exit 4 indicates an extraction error; no diagnostic text was logged\n'
            else
                printf 'ERROR: gallery-dl returned no diagnostic text\n'
            fi
        fi
    fi

    if (( staging_rc != 0 )); then
        say "r/$sub local processing error; continuing"
        printf 'ERROR: one or more completed files could not be hashed, indexed, or moved into the archive\n'
    fi

    rm -f -- "$error_log"
}

stop_now() {
    printf '\n'
    say "stopping"
    process_staging || true
    exit 0
}

ingest_main() {
    [[ $# -eq 1 ]] || die "internal --ingest expects exactly one completed file"

    need_command sqlite3
    need_command sha256sum
    need_command stat
    need_command od
    need_command tr

    load_config
    init_state
    save_candidate "$1"
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
    if [[ "${1:-}" == "--ingest" ]]; then
        shift
        ingest_main "$@"
    else
        main "$@"
    fi
fi
