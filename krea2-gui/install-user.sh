#!/bin/sh
set -eu

base_url="https://raw.githubusercontent.com/polskiftw/gpages/e311044cb04470ea52bf02f9d4058f11a51a7ac1/krea2-gui"
root="${KREA2_ROOT:-$HOME/ai/krea2}"
bin_dir="$root/bin"
tmp_dir="$root/tmp/krea2-gui-install-$$"

cleanup() {
    rm -rf "$tmp_dir"
}
trap cleanup EXIT HUP INT TERM

mkdir -p "$bin_dir" "$tmp_dir"

fetch() {
    url=$1
    dest=$2
    if command -v curl >/dev/null 2>&1; then
        curl -fL --retry 2 --connect-timeout 15 "$url" -o "$dest"
    elif command -v wget >/dev/null 2>&1; then
        wget -O "$dest" "$url"
    else
        printf '%s\n' "Need curl or wget to install/update Krea2 GUI." >&2
        exit 1
    fi
}

fetch "$base_url/krea2_gui.py" "$tmp_dir/krea2_gui.py"
install -m 0755 "$tmp_dir/krea2_gui.py" "$bin_dir/krea2-gui"

printf 'Installed/updated %s\n' "$bin_dir/krea2-gui"
printf '%s\n' "Everything stays under $root."
printf '%s\n' "No desktop entry or local Git checkout was created."
