#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import os
import platform
import random
import re
import shutil
import sys
import urllib.parse
import urllib.request
import zipfile
from datetime import datetime
from pathlib import Path

BACKEND_VERSION = "2026-10-05.12"
UPSTREAM_REVISION = "121fb0183944f1befeb712d92e9ca07d0e282088"
UPSTREAM_BASE = f"https://huggingface.co/yijunwang2/krea2-reid/resolve/{UPSTREAM_REVISION}"

ASSETS = {
    "pipeline.py": "5bb41a473c717c94dc5823af6775f78b8d8062d942798265c29e2302c0c4934e",
    "krea2_reid_rank32.safetensors": "a80349faee4a2d80eff9a83820cd523c74cd0bbc6039cee21fa34b084d967944",
    "PIPELINE_LICENSE": None,
    "NOTICE": None,
    "LICENSE.pdf": None,
}

REBALANCE_PROFILES = {
    "subtle": [1, 1, 1, 1, 1, 1, 1, 1.4, 2, 1.05, 1.8, 1],
    "balanced": [1, 1, 1, 1, 1, 1, 1, 2.5, 5, 1.1, 4, 1],
    "aggressive": [1, 1, 1, 1, 1, 1, 1, 3, 6, 1.2, 5, 1],
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Krea 2 ReID using the existing local Krea2 runtime")
    parser.add_argument("prompt")
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--seed", type=int)
    parser.add_argument("-q", "--queue", type=int, default=1)
    parser.add_argument("--width", type=int, default=1024)
    parser.add_argument("--height", type=int, default=1024)
    parser.add_argument("--steps", type=int, default=8)
    parser.add_argument("--identity-strength", type=float, default=1.0)
    parser.add_argument(
        "--rebalance",
        choices=tuple(REBALANCE_PROFILES),
        help="Reweight Krea 2's 12 prompt-conditioning layers using the existing GUI profiles.",
    )
    parser.add_argument(
        "--lora",
        action="append",
        default=[],
        help="Additional Krea 2 LoRA path, optionally suffixed with :strength. May be passed twice.",
    )
    parser.add_argument("--out", type=Path)
    return parser.parse_args()


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def download(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".part")
    print(f"Downloading {dest.name}...", flush=True)
    try:
        with urllib.request.urlopen(url, timeout=120) as response, tmp.open("wb") as out:
            while True:
                chunk = response.read(1024 * 1024)
                if not chunk:
                    break
                out.write(chunk)
        tmp.replace(dest)
    finally:
        try:
            tmp.unlink()
        except FileNotFoundError:
            pass


def ensure_assets(vendor: Path) -> None:
    vendor.mkdir(parents=True, exist_ok=True)
    for name, expected_hash in ASSETS.items():
        path = vendor / name
        valid = path.is_file()
        if valid and expected_hash is not None:
            valid = sha256(path) == expected_hash
        if not valid:
            if path.exists():
                path.unlink()
            download(f"{UPSTREAM_BASE}/{name}", path)
        if expected_hash is not None:
            actual = sha256(path)
            if actual != expected_hash:
                path.unlink(missing_ok=True)
                raise RuntimeError(f"Checksum mismatch for {name}: {actual}")


def prepare_local_pipeline(vendor: Path, runtime: Path) -> Path:
    """Patch only device/dtype dispatch for the existing low-VRAM Krea2 strategy."""
    source_path = vendor / "pipeline.py"
    source = source_path.read_text(encoding="utf-8")
    needle = "        device = self._execution_device\n        transformer_dtype = self.transformer.dtype"
    replacement = "        device = torch.device(\"cuda\")\n        transformer_dtype = torch.bfloat16"
    if needle not in source:
        raise RuntimeError("Pinned ReID pipeline no longer matches the expected compatibility patch")
    patched = source.replace(needle, replacement, 1)

    kv_needle = (
        "            if kv_cache:\n"
        "                # Precompute pass: the refs alone run through the blocks once at t=0\n"
        "                # and every denoising step reuses their per-block K/V, so the ref\n"
        "                # tokens are dropped from the per-step sequence entirely.\n"
        "                ref_kv = self.transformer.precompute_ref_kv(ref_tokens, ref_position_ids, attention_kwargs)\n"
        "                ref_tokens, ref_seq_len = None, 0"
    )
    kv_replacement = (
        "            if kv_cache:\n"
        "                # Block-level group offload hooks run on transformer.forward(), but\n"
        "                # precompute_ref_kv() is called directly. Stage only the small\n"
        "                # top-level modules used by that direct method, then return them\n"
        "                # to CPU; each large transformer block still onloads one at a time.\n"
        "                _ref_prelude = (\n"
        "                    self.transformer.time_embed,\n"
        "                    self.transformer.time_mod_proj,\n"
        "                    self.transformer.img_in,\n"
        "                )\n"
        "                for _module in _ref_prelude:\n"
        "                    _module.to(device)\n"
        "                try:\n"
        "                    ref_kv = self.transformer.precompute_ref_kv(\n"
        "                        ref_tokens, ref_position_ids, attention_kwargs\n"
        "                    )\n"
        "                finally:\n"
        "                    for _module in _ref_prelude:\n"
        "                        _module.to(\"cpu\")\n"
        "                ref_tokens, ref_seq_len = None, 0"
    )
    if kv_needle not in patched:
        raise RuntimeError("Pinned ReID pipeline no longer matches the K/V offload compatibility patch")
    patched = patched.replace(kv_needle, kv_replacement, 1)

    runtime.mkdir(parents=True, exist_ok=True)
    target = runtime / "pipeline_local.py"
    content = (
        f"# Generated from yijunwang2/krea2-reid {UPSTREAM_REVISION}\n"
        "# Local compatibility patch: CUDA execution device + BF16 compute dtype.\n"
        + patched
    )
    if not target.exists() or target.read_text(encoding="utf-8") != content:
        target.write_text(content, encoding="utf-8")
    return target


def load_pipeline_module(path: Path):
    spec = importlib.util.spec_from_file_location("krea2_reid_local_pipeline", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not import ReID pipeline from {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def output_path(out_dir: Path, seed: int, index: int) -> Path:
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    return out_dir / f"reid_{stamp}_{index:02d}_seed{seed}.png"


def parse_lora_specs(values: list[str], root: Path) -> list[tuple[Path, float]]:
    specs: list[tuple[Path, float]] = []
    for raw in values:
        text = raw.strip()
        if not text:
            continue

        strength = 1.0
        path_text = text
        if ":" in text:
            maybe_path, maybe_strength = text.rsplit(":", 1)
            try:
                strength = float(maybe_strength)
                path_text = maybe_path
            except ValueError:
                pass

        path = Path(path_text).expanduser()
        if not path.is_absolute():
            path = root / path
        path = path.resolve()
        if not path.is_file():
            raise SystemExit(f"LoRA not found: {path}")

        specs.append((path, strength))

    if len(specs) > 2:
        raise SystemExit("ReID mode supports at most two additional LoRAs")
    return specs


def load_transformer_lora(
    module,
    transformer,
    load_file,
    path: Path,
    adapter_name: str,
    strength: float,
) -> bool:
    """Load a normal transformer LoRA or the tiny Krea2 text-fusion projector delta.

    Returns True when an actual PEFT adapter was loaded and should be added to
    set_adapters(). Projector-only patches modify the base projector directly
    and therefore return False.
    """
    state_dict = dict(load_file(str(path), device="cpu"))
    # Normalize first: fedor_bypass is stored with an upstream-style prefix
    # that normalizes down to txtfusion.projector.diff.
    state_dict = module._normalize_lora_state_dict(state_dict)

    projector_keys = [
        key
        for key in state_dict
        if key in ("txtfusion.projector.diff", "text_fusion.projector.diff")
        or key.endswith(".txtfusion.projector.diff")
        or key.endswith(".text_fusion.projector.diff")
    ]
    if len(projector_keys) > 1:
        raise ValueError(
            f"LoRA file contains multiple projector delta aliases: {projector_keys}"
        )

    if projector_keys:
        key = projector_keys[0]
        delta = state_dict.pop(key)
        weight = transformer.text_fusion.projector.weight
        if tuple(delta.shape) != tuple(weight.shape):
            raise ValueError(
                f"Projector delta shape {tuple(delta.shape)} does not match "
                f"Krea2 projector weight {tuple(weight.shape)}"
            )
        print(f"Applying Krea2 projector delta: {path} @ {strength:g}", flush=True)
        with __import__("torch").no_grad():
            weight.add_(delta.to(device=weight.device, dtype=weight.dtype), alpha=strength)
        del delta

    if not state_dict:
        return False

    if not any(key.startswith("transformer.") for key in state_dict):
        state_dict = module._convert_non_diffusers_krea2_lora_to_diffusers(state_dict)
    transformer.load_lora_adapter(state_dict, prefix="transformer", adapter_name=adapter_name)
    del state_dict
    return True


def ensure_torchvision(torch, reid_root: Path) -> None:
    """Install a Torch-matched torchvision wheel only inside the ReID subtree."""
    local_site = reid_root / "python"
    local_site.mkdir(parents=True, exist_ok=True)
    local_site_str = str(local_site)
    if local_site_str not in sys.path:
        sys.path.insert(0, local_site_str)

    version_match = re.match(r"^(\d+)\.(\d+)\.(\d+)", str(torch.__version__))
    if version_match is None:
        raise RuntimeError(f"Cannot determine torchvision version for torch {torch.__version__}")

    torch_major, torch_minor, torch_patch = (int(part) for part in version_match.groups())
    if torch_major != 2:
        raise RuntimeError(
            f"Automatic isolated torchvision setup only knows the PyTorch 2.x version scheme; "
            f"found torch {torch.__version__}"
        )

    torchvision_version = f"0.{torch_minor + 15}.{torch_patch}"

    try:
        import torchvision

        installed = str(getattr(torchvision, "__version__", ""))
        if installed.split("+", 1)[0] == torchvision_version:
            print(
                f"Using isolated torchvision {installed} from {Path(torchvision.__file__).parent}",
                flush=True,
            )
            return
    except Exception:
        pass

    # A failed binary import can leave half-imported torchvision modules behind.
    for name in list(sys.modules):
        if name == "torchvision" or name.startswith("torchvision."):
            del sys.modules[name]

    build = str(torch.__version__).split("+", 1)[1] if "+" in str(torch.__version__) else ""
    if not build:
        if getattr(torch.version, "cuda", None):
            build = "cu" + str(torch.version.cuda).replace(".", "")
        else:
            build = "cpu"

    machine = platform.machine().lower()
    if machine in {"x86_64", "amd64"}:
        wheel_arch = "x86_64"
    elif machine in {"aarch64", "arm64"}:
        wheel_arch = "aarch64"
    else:
        raise RuntimeError(f"Unsupported architecture for automatic torchvision setup: {machine}")

    py_tag = f"cp{sys.version_info.major}{sys.version_info.minor}"
    filename = (
        f"torchvision-{torchvision_version}+{build}-"
        f"{py_tag}-{py_tag}-manylinux_2_28_{wheel_arch}.whl"
    )
    index_build = build if build.startswith("cu") else "cpu"
    encoded_name = urllib.parse.quote(filename, safe="-_.")
    wheel_url = f"https://download.pytorch.org/whl/{index_build}/{encoded_name}"

    wheel_dir = reid_root / "cache" / "wheels"
    wheel_path = wheel_dir / filename
    if not wheel_path.is_file():
        wheel_dir.mkdir(parents=True, exist_ok=True)
        print(
            f"Downloading isolated torchvision {torchvision_version}+{build} "
            f"for torch {torch.__version__}...",
            flush=True,
        )
        download(wheel_url, wheel_path)

    print(f"Installing isolated torchvision from {wheel_path.name}...", flush=True)

    # Remove any stale/partial prior extraction, but only inside the ReID subtree.
    for path in (
        local_site / "torchvision",
        local_site / "torchvision.libs",
    ):
        if path.is_dir():
            shutil.rmtree(path)
        elif path.exists():
            path.unlink()
    for path in local_site.glob("torchvision-*.dist-info"):
        if path.is_dir():
            shutil.rmtree(path)
        else:
            path.unlink()

    try:
        with zipfile.ZipFile(wheel_path) as wheel:
            wheel.extractall(local_site)
    except (OSError, zipfile.BadZipFile) as exc:
        wheel_path.unlink(missing_ok=True)
        raise RuntimeError(f"Could not unpack torchvision wheel: {exc}") from exc

    import importlib

    importlib.invalidate_caches()
    for name in list(sys.modules):
        if name == "torchvision" or name.startswith("torchvision."):
            del sys.modules[name]

    import torchvision

    installed = str(getattr(torchvision, "__version__", ""))
    if installed.split("+", 1)[0] != torchvision_version:
        raise RuntimeError(
            f"Installed torchvision {installed}, expected {torchvision_version} "
            f"for torch {torch.__version__}"
        )
    print(f"Installed isolated torchvision {installed} in {local_site}", flush=True)


def rebalance_prompt_embeds(torch, prompt_embeds, profile: str):
    weights = REBALANCE_PROFILES[profile]
    if prompt_embeds.ndim != 4 or prompt_embeds.shape[2] != len(weights):
        raise RuntimeError(
            "Unexpected Krea 2 prompt embedding shape for rebalance: "
            f"{tuple(prompt_embeds.shape)}"
        )

    orig_rms = prompt_embeds.float().pow(2).mean().sqrt()
    layer_weights = torch.tensor(
        weights,
        device=prompt_embeds.device,
        dtype=prompt_embeds.dtype,
    ).view(1, 1, len(weights), 1)

    balanced = prompt_embeds * layer_weights
    new_rms = balanced.float().pow(2).mean().sqrt().clamp_min(1e-8)
    balanced = balanced * (orig_rms / new_rms).to(dtype=balanced.dtype)
    return balanced


def main() -> int:
    args = parse_args()
    if args.queue < 1:
        raise SystemExit("--queue must be at least 1")
    if args.width < 64 or args.height < 64:
        raise SystemExit("width and height must be at least 64")
    if not args.reference.is_file():
        raise SystemExit(f"Reference image not found: {args.reference}")

    root = Path(os.environ.get("KREA2_ROOT", str(Path.home() / "ai" / "krea2"))).expanduser()
    reid_root = Path(os.environ.get("KREA2_REID_ROOT", str(root / "reid"))).expanduser()
    vendor = reid_root / "vendor"
    runtime = reid_root / "runtime"
    cache = reid_root / "cache"
    tmp = reid_root / "tmp"
    model_dir = root / "models" / "Krea-2-Turbo"
    out_dir = (args.out or (root / "out")).expanduser()
    extra_loras = parse_lora_specs(args.lora, root)

    for directory in (
        cache,
        tmp,
        out_dir,
        cache / "xdg" / "torch" / "kernels",
        cache / "torchinductor",
        cache / "triton",
        cache / "nvidia",
    ):
        directory.mkdir(parents=True, exist_ok=True)

    # Keep everything ReID-specific under ~/ai/krea2/reid.
    os.environ["HF_HOME"] = str(cache / "huggingface")
    os.environ["HF_HUB_CACHE"] = str(cache / "huggingface" / "hub")
    os.environ["HF_ASSETS_CACHE"] = str(cache / "huggingface" / "assets")
    os.environ["HUGGINGFACE_HUB_CACHE"] = str(cache / "huggingface" / "hub")
    os.environ["TRANSFORMERS_CACHE"] = str(cache / "huggingface" / "transformers")
    os.environ["TORCH_HOME"] = str(cache / "torch")
    os.environ["PIP_CACHE_DIR"] = str(cache / "pip")
    os.environ["TORCHINDUCTOR_CACHE_DIR"] = str(cache / "torchinductor")
    os.environ["TRITON_CACHE_DIR"] = str(cache / "triton")
    os.environ["CUDA_CACHE_PATH"] = str(cache / "nvidia")
    os.environ["XDG_CACHE_HOME"] = str(cache / "xdg")
    os.environ["TMPDIR"] = str(tmp)
    os.environ["PYTORCH_ALLOC_CONF"] = "expandable_segments:True"

    if not model_dir.is_dir():
        raise SystemExit(f"Local Krea 2 model not found: {model_dir}")

    ensure_assets(vendor)
    pipeline_path = prepare_local_pipeline(vendor, runtime)

    # Heavy imports happen only after cache paths are redirected.
    import torch
    from PIL import Image, ImageOps
    from safetensors.torch import load_file

    if not torch.cuda.is_available():
        raise RuntimeError("CUDA is not available")

    ensure_torchvision(torch, reid_root)

    module = load_pipeline_module(pipeline_path)
    cuda = torch.device("cuda")
    cpu = torch.device("cpu")

    print("Loading Krea 2 transformer...", flush=True)
    transformer = module.Krea2Transformer2DModel.from_pretrained(
        str(model_dir),
        subfolder="transformer",
        torch_dtype=torch.bfloat16,
        local_files_only=True,
    )

    # Load the functional ReID adapter before adding low-memory hooks.
    state_dict = load_file(str(vendor / "krea2_reid_rank32.safetensors"), device="cpu")
    state_dict = module._normalize_lora_state_dict(state_dict)
    if not any(key.startswith("transformer.") for key in state_dict):
        state_dict = module._convert_non_diffusers_krea2_lora_to_diffusers(state_dict)
    transformer.load_lora_adapter(state_dict, prefix="transformer", adapter_name="reid")
    del state_dict

    adapter_names = ["reid"]
    adapter_weights = [args.identity_strength]
    for index, (lora_path, lora_strength) in enumerate(extra_loras, start=1):
        adapter_name = f"user_lora_{index}"
        print(f"Loading extra LoRA {index}: {lora_path} @ {lora_strength:g}", flush=True)
        loaded_adapter = load_transformer_lora(
            module,
            transformer,
            load_file,
            lora_path,
            adapter_name,
            lora_strength,
        )
        if loaded_adapter:
            adapter_names.append(adapter_name)
            adapter_weights.append(lora_strength)

    transformer.set_adapters(adapter_names, adapter_weights)

    # Same strategy as the already-working local Krea2 generator: FP8 storage,
    # BF16 compute, one transformer block resident on the GPU at a time.
    transformer.enable_layerwise_casting(
        storage_dtype=torch.float8_e4m3fn,
        compute_dtype=torch.bfloat16,
    )
    transformer.enable_group_offload(
        onload_device=cuda,
        offload_device=cpu,
        offload_type="block_level",
        num_blocks_per_group=1,
        # precompute_ref_kv passes an empty list that attention mutates in place.
        # Group offload otherwise send_to_device()s kwargs and creates a copy,
        # leaving the caller's list empty and causing captured[0] to fail.
        exclude_kwargs=["kv_capture"],
    )

    print("Loading remaining local Krea 2 components...", flush=True)
    pipe = module.Krea2OstrisEditPipeline.from_pretrained(
        str(model_dir),
        transformer=transformer,
        torch_dtype=torch.bfloat16,
        local_files_only=True,
    )
    pipe.set_adapters(adapter_names, adapter_weights)

    reference = ImageOps.exif_transpose(Image.open(args.reference)).convert("RGB")
    ref_tensor = pipe._to_chw_tensor(reference)

    # Precompute the Qwen3-VL prompt/reference conditioning first, while only the
    # text encoder occupies CUDA. The processor cache is redirected above.
    print("Encoding identity reference...", flush=True)
    pipe.text_encoder.to(cuda)
    vl_images = pipe._prep_vl_images([ref_tensor.to(cuda)], 384 * 384)
    with torch.inference_mode():
        prompt_embeds, prompt_mask = pipe.encode_prompt(
            args.prompt,
            vl_images,
            num_images_per_prompt=1,
            max_sequence_length=512,
            device=cuda,
        )
    prompt_embeds = prompt_embeds.to(dtype=torch.bfloat16)
    if args.rebalance:
        print(f"Applying {args.rebalance} prompt rebalance...", flush=True)
        prompt_embeds = rebalance_prompt_embeds(torch, prompt_embeds, args.rebalance)
    pipe.text_encoder.to(cpu)
    del vl_images
    torch.cuda.empty_cache()

    # The VAE is small enough to remain resident while the transformer itself is
    # block-offloaded. Prompt encoding is already finished, so Qwen3-VL stays on CPU.
    pipe.vae.to(cuda)

    base_seed = args.seed if args.seed is not None else random.SystemRandom().randrange(0, 2**31)
    for index in range(1, args.queue + 1):
        seed = base_seed + index - 1
        print(f"Generating {index} / {args.queue} - seed {seed}", flush=True)
        generator = torch.Generator(device="cpu").manual_seed(seed)
        with torch.inference_mode():
            result = pipe(
                prompt=None,
                image=reference,
                width=args.width,
                height=args.height,
                num_inference_steps=args.steps,
                guidance_scale=0.0,
                generator=generator,
                prompt_embeds=prompt_embeds,
                prompt_embeds_mask=prompt_mask,
                reference_max_pixels=384 * 384,
                vl_image_max_pixels=384 * 384,
                encode_reference_in_prompt=False,
                kv_cache=True,
            ).images[0]
        path = output_path(out_dir, seed, index)
        result.save(path)
        print(f"Saved {path}", flush=True)

    pipe.vae.to(cpu)
    torch.cuda.empty_cache()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
