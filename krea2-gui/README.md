# Krea2 GUI

A small native Qt 6 front end for Claire's existing local `krea2` CLI. It does **not** reimplement inference and does not run a web server. The GUI simply launches the same `krea2` command already used in the terminal and watches `~/ai/krea2/out` for completed images.

## What it exposes

- Prompt editor
- Optional LoRA, with strength (`file.safetensors:0.8` syntax underneath)
- Seed field with `Random` mode
- Image count wired to `-q`
- Rebalance modes: none, subtle, balanced, aggressive
- Large Generate button that becomes Cancel Queue while running
- Live queue/seed status parsed from `krea2` output
- Current-image preview
- Session thumbnail rail
- Open File / Open Folder / Copy Seed
- Expandable raw CLI output for debugging
- `Ctrl+Enter` / `Ctrl+Return` to generate

Queued seeds are left to the existing CLI. With an explicit seed, the current Krea2 behavior is preserved: for example, seed `123` with `-q 5` produces seeds `123` through `127`.

## Requirements

- The existing `krea2` command available in `PATH`
- The existing Krea2 tree at `~/ai/krea2`
- Python 3
- PySide6 / Qt 6 Widgets

On Gentoo, PySide6 is provided by `dev-python/pyside`:

```sh
emerge -av dev-python/pyside
```

## Run it

From this directory:

```sh
python3 krea2_gui.py
```

Or make the script executable and run it directly:

```sh
chmod +x krea2_gui.py
./krea2_gui.py
```

## Optional environment overrides

Normally none of these are needed.

```sh
KREA2_ROOT=/some/other/krea2 KREA2_CLI=/path/to/krea2 ./krea2_gui.py
```

`KREA2_ROOT` defaults to `~/ai/krea2`. `KREA2_CLI` defaults to the `krea2` found in `PATH`.

## LoRA discovery

The LoRA box is editable, so any value accepted by the CLI can be typed manually. At startup the GUI also looks for `*.safetensors` directly in:

- `~/ai/krea2/`
- `~/ai/krea2/lora/`
- `~/ai/krea2/loras/`

The GUI does not recursively crawl the whole model tree.

## Cancellation

On Linux the GUI launches Krea2 through `setsid` when available. Cancel Queue sends `SIGTERM` to the whole Krea2 process group, then escalates to `SIGKILL` after two seconds if it is still alive. That prevents a shell launcher from exiting while leaving the CUDA/Python child running.

## Privacy / networking

The GUI has no networking code. It is a local Qt application and only talks to the existing local CLI and output directory.
