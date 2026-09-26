# Krea2 GUI

A native Qt 6 front end for Claire's existing local Krea2 runtime. It does **not** run a web server. Normal text-to-image generation still launches the existing `krea2` command and watches `~/ai/krea2/out` for completed images.

An optional AnyPaint backend adds arbitrary-mask image editing, inpainting, and outpainting without replacing the working text-to-image path.

## What it exposes

### Generate

- Prompt editor with local Hunspell spellcheck
- Optional LoRA, with strength (`file.safetensors:0.8` syntax underneath)
- Seed field with `Random` mode
- Image count wired to `-q`
- Rebalance modes: none, subtle, balanced, aggressive
- Large Generate button that becomes Cancel Queue while running
- Live queue/seed status parsed from `krea2` output
- Current-image preview and session thumbnail rail
- Open File / Open Folder / Copy Seed
- Expandable raw CLI output for debugging
- `Ctrl+Enter` / `Ctrl+Return` to generate

Queued seeds are left to the existing CLI. With an explicit seed, the current Krea2 behavior is preserved: for example, seed `123` with `-q 5` produces seeds `123` through `127`.

### Edit / Inpaint / Outpaint

The image toolbar includes **Edit / Inpaint / Outpaint…**. It opens a native AnyPaint editor that can:

- use the currently selected generated image or choose another local image;
- paint an arbitrary freehand regenerate mask directly over the source;
- erase mask strokes with the eraser tool or right mouse button;
- clear, fill, or invert the source mask;
- add independent left/right/top/bottom outpaint padding;
- reuse Random/integer seeds and queue multiple edits;
- return finished edits to the same output directory, preview, and session rail.

The overlay is intentionally high-contrast rather than color-coded: **white overlay = generated/edited**, uncovered source = preserved. Areas outside the source image are always generated, matching AnyPaint's input contract.

## Requirements

- Existing Krea2 tree at `~/ai/krea2`
- Existing `~/ai/krea2/bin/krea2`
- Python 3
- PySide6 / Qt 6 Widgets
- Hunspell is optional; without it the prompt editor simply has no spellcheck

On Gentoo, PySide6 is provided by `dev-python/pyside`:

```sh
emerge -av dev-python/pyside
```

## Install / update the GUI

From this directory:

```sh
./install-user.sh
```

This installs:

- `~/.local/bin/krea2-gui`
- `~/.local/bin/krea2_anypaint_editor.py`
- `~/.local/lib/krea2-gui/krea2_anypaint.py`
- `~/.local/bin/krea2-anypaint-setup`
- `~/.local/share/applications/krea2-gui.desktop`

You can also run the source tree directly with:

```sh
python3 krea2_gui.py
```

## One-time AnyPaint setup

Normal generation needs no additional setup. To enable editing, run once:

```sh
krea2-anypaint-setup
```

The setup helper downloads the current `yijunwang2/krea2-anypaint` functional adapter into `~/ai/krea2/models/krea2-anypaint` and creates `~/ai/krea2/bin/krea2-anypaint`.

The 229 MB AnyPaint adapter shares the existing local `~/ai/krea2/models/Krea-2-Turbo` base. It does **not** download a second Krea2 checkpoint. The helper also downloads only Qwen3-VL processor/tokenizer metadata needed to encode reference images; it explicitly excludes Qwen model weights because the existing Krea2 text encoder is reused.

The setup helper intentionally does **not** install AnyPaint's upstream `requirements.txt` or downgrade the working Krea2 environment. The local backend uses the already-working Krea2 venv and replaces AnyPaint's OpenCV-only mask dilation with Pillow's equivalent square max filter, so OpenCV is not added just for this feature.

## 12 GB VRAM strategy

The AnyPaint backend is adapted to the same memory strategy already proven by the local text-to-image runtime:

1. Load the pipeline without the transformer.
2. Put Qwen3-VL on CUDA only long enough to encode the prompt/reference, then free it.
3. Load the AnyPaint-compatible transformer in BF16 on CPU.
4. Use FP8 E4M3FN layerwise storage with BF16 compute.
5. Use one-block-at-a-time CUDA group offload.
6. Keep the VAE on CPU except while an encode/decode call is actually running.

This is deliberately different from AnyPaint's portable reference example, which loads the full BF16 pipeline on CUDA and is not suitable for a 12 GB card.

## Optional environment overrides

Normally none of these are needed.

```sh
KREA2_ROOT=/some/other/krea2 KREA2_CLI=/path/to/krea2 KREA2_ANYPAINT_CLI=/path/to/krea2-anypaint ./krea2_gui.py
```

Runtime-specific AnyPaint overrides are also available:

- `KREA2_MODEL_DIR`
- `KREA2_ANYPAINT_DIR`
- `KREA2_VLM_PROCESSOR_DIR`
- `KREA2_OUT`

## LoRA discovery

The normal generation LoRA box is editable, so any value accepted by the existing CLI can be typed manually. At startup the GUI also looks for `*.safetensors` directly in:

- `~/ai/krea2/`
- `~/ai/krea2/lora/`
- `~/ai/krea2/loras/`

The GUI does not recursively crawl the whole model tree. AnyPaint's own functional adapter is managed separately and is not mixed into this normal-generation selector.

## Cancellation

On Linux the GUI launches both Krea2 backends through `setsid` when available. Cancel Queue sends `SIGTERM` to the whole process group, then escalates to `SIGKILL` after two seconds if it is still alive. That prevents a shell launcher from exiting while leaving the CUDA/Python child running.

## Privacy / networking

The GUI itself has no networking code. Normal generation and AnyPaint inference are local. `krea2-anypaint-setup` is the only AnyPaint component that uses the network: it downloads the adapter/pipeline and processor metadata once, after which the installed AnyPaint wrapper forces Hugging Face and Transformers offline mode.

## Upstream AnyPaint licensing

The AnyPaint model weights are subject to the Krea 2 Community License. Its `pipeline.py` and helper code are Apache-2.0 licensed upstream. The setup command downloads and preserves upstream `LICENSE.pdf`, `PIPELINE_LICENSE`, `NOTICE`, and `README.md` alongside the adapter rather than vendoring those third-party files into this repository.
