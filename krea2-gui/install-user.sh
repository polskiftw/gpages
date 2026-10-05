#!/bin/sh
set -eu

base_url="https://raw.githubusercontent.com/polskiftw/gpages/main/krea2-gui"
bin_dir="$HOME/.local/bin"
app_dir="$HOME/.local/share/applications"
tmp_dir="${TMPDIR:-/tmp}/krea2-gui-install-$$"

cleanup() {
    rm -rf "$tmp_dir"
}
trap cleanup EXIT HUP INT TERM

mkdir -p "$bin_dir" "$app_dir" "$tmp_dir"

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

cat > "$app_dir/krea2-gui.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Krea2
Comment=Local Krea2 image generation GUI
Exec=$bin_dir/krea2-gui
Terminal=false
Categories=Graphics;
StartupNotify=true
DESKTOP

printf 'Installed/updated %s\n' "$bin_dir/krea2-gui"
printf 'Installed/updated %s\n' "$app_dir/krea2-gui.desktop"
printf '%s\n' "No Git checkout was created."
