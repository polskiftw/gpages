# Krea2 GUI

A small native Qt 6 front end for Claire's existing local `krea2` CLI. It does **not** reimplement inference and does not run a web server. The GUI simply launches the same `krea2` command already used in the terminal and watches `~/ai/krea2/out` for completed images.

## What it exposes

- Text-to-image and ReID-reference modes
- ReID reference-image picker for identity-preserving scene/outfit/pose changes
- Square, portrait, and landscape output presets for ReID
- Prompt editor
- Up to two optional LoRAs, each with its own strength (`file.safetensors:0.8` syntax underneath) in both text-to-image and ReID modes
- Seed field with `Random` mode
- Image count wired to `-q`
- Rebalance modes: none, subtle, balanced, aggressive in both text-to-image and ReID modes
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

## Install/update without local Git

The installer downloads the current GUI directly from the remote repository and writes only inside the existing Krea2 tree:

```sh
curl -fsSL https://raw.githubusercontent.com/polskiftw/gpages/main/krea2-gui/install-user.sh | sh
```

This installs the launcher at:

```text
~/ai/krea2/bin/krea2-gui
```

No desktop/menu entry and no local Git checkout are created.

## Run it

```sh
~/ai/krea2/bin/krea2-gui
```

## Optional environment overrides

Normally none of these are needed.

```sh
KREA2_ROOT=/some/other/krea2 KREA2_CLI=/path/to/krea2 ./krea2_gui.py
```

`KREA2_ROOT` defaults to `~/ai/krea2`. `KREA2_CLI` defaults to the `krea2` found in `PATH`.

## LoRA discovery

Both LoRA boxes are editable, so any value accepted by the CLI can be typed manually. If only LoRA 2 is selected, the GUI shifts it into the first CLI LoRA position automatically. At startup the GUI also looks for `*.safetensors` directly in:

- `~/ai/krea2/`
- `~/ai/krea2/lora/`
- `~/ai/krea2/loras/`

The GUI does not recursively crawl the whole model tree.

The corresponding CLI accepts the two LoRAs as optional positional arguments, so existing one-LoRA commands remain valid:

```sh
krea2 "prompt" first.safetensors:0.8
krea2 "prompt" first.safetensors:0.8 second.safetensors:0.45
```

## ReID mode

ReID uses the existing local Krea 2 model and Python environment; it does not create a second Krea installation. The GUI keeps the entire ReID add-on under:

```text
~/ai/krea2/reid/
├── backend/
├── vendor/
├── runtime/
├── cache/
└── tmp/
```

On the first ReID run, the GUI copies its matching backend script from this repository into `backend/`. The backend then downloads the pinned Krea 2 ReID functional adapter and its pinned reference pipeline into `vendor/`. Hugging Face, Transformers, Torch, XDG, and temporary-file caches used by ReID are redirected into the ReID tree.

The base Krea 2 checkpoint remains the already-installed `~/ai/krea2/models/Krea-2-Turbo`, and generated images continue to go to `~/ai/krea2/out`.

The ReID runtime uses the same low-memory strategy as the existing generator: FP8 transformer storage, BF16 compute, and one-block group offload. Current Transformers requires torchvision for Qwen3-VL image preprocessing; ReID downloads the exact Torch/Python/CUDA-matched torchvision wheel from PyTorch's official wheel index and unpacks it directly into `~/ai/krea2/reid/python/`. It does not require pip and does not modify the working Krea2 venv. Removing the ReID folder removes that dependency too. The identity adapter is fixed at its tested default strength of 1.0 in the GUI for now. The normal LoRA 1 and LoRA 2 controls remain available in ReID mode and are stacked after the ReID adapter at their selected strengths. ReID also applies the same Subtle/Balanced/Aggressive 12-channel prompt-conditioning rebalance profiles used by ordinary Krea2, including global RMS renormalization after weighting. Strong additional LoRAs can compete with identity retention, so lower strengths are a sensible starting point.

To remove the ReID addition completely without touching ordinary Krea 2:

```sh
rm -rf ~/ai/krea2/reid
```

The next ReID run will recreate it.

## Cancellation

On Linux the GUI launches Krea2 through `setsid` when available. Cancel Queue sends `SIGTERM` to the whole Krea2 process group, then escalates to `SIGKILL` after two seconds if it is still alive. That applies to both ordinary generation and ReID setup/generation, so closing/cancelling does not leave a model server or generation worker running.

## Privacy / networking

There is no local web server, daemon, service, desktop/menu entry, or autostart process. GUI settings live in `~/ai/krea2/config/krea2-gui.ini`. Installer scratch files live temporarily under `~/ai/krea2/tmp/`. Ordinary text-to-image generation remains local-only. ReID uses outbound HTTPS only when its small backend file or pinned upstream ReID files are missing; after those files and the Qwen processor metadata are cached, generation uses the local Krea 2 model and local ReID tree.
