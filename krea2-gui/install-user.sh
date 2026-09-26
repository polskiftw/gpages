#!/bin/sh
set -eu

src_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
bin_dir="$HOME/.local/bin"
app_dir="$HOME/.local/share/applications"

mkdir -p "$bin_dir" "$app_dir"
install -m 0755 "$src_dir/krea2_gui.py" "$bin_dir/krea2-gui"
install -m 0755 "$src_dir/setup-anypaint.sh" "$bin_dir/krea2-anypaint-setup"

cat > "$app_dir/krea2-gui.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Krea2
Comment=Local Krea2 image generation and editing GUI
Exec=$bin_dir/krea2-gui
Terminal=false
Categories=Graphics;
StartupNotify=true
DESKTOP

printf 'Installed %s\n' "$bin_dir/krea2-gui"
printf 'Installed %s\n' "$bin_dir/krea2-anypaint-setup"
printf 'Installed %s\n' "$app_dir/krea2-gui.desktop"
