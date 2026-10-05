#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import os
import random
import sys
import urllib.request
from datetime import datetime
from pathlib import Path

BACKEND_VERSION = "2026-10-05.2"
UPSTREAM_REVISION = "121fb0183944f1befeb712d92e9ca07d0e282088"
UPSTREAM_BASE = f"https://huggingface.co/yijunwang2/krea2-reid/resolve/{UPSTREAM_REVISION}"

ASSETS = {
    "pipeline.py": "5bb41a473c717c94dc5823af6775f78b8d8062d942798265c29e2302c0c4934e",
    "krea2_reid_rank32.safetensors": "a80349faee4a2d80eff9a83820cd523c74cd0bbc6039cee21fa34b084d967944",
    "PIPELINE_LICENSE": None,
    "NOTICE": None,
    "LICENSE.pdf": None,
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

    for directory in (cache, tmp, out_dir):
        directory.mkdir(parents=True, exist_ok=True)

    # Keep everything ReID-specific under ~/ai/krea2/reid.
    os.environ["HF_HOME"] = str(cache / "huggingface")
    os.environ["HF_HUB_CACHE"] = str(cache / "huggingface" / "hub")
    os.environ["HF_ASSETS_CACHE"] = str(cache / "huggingface" / "assets")
    os.environ["HUGGINGFACE_HUB_CACHE"] = str(cache / "huggingface" / "hub")
    os.environ["TRANSFORMERS_CACHE"] = str(cache / "huggingface" / "transformers")
    os.environ["TORCH_HOME"] = str(cache / "torch")
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
    transformer.set_adapters(["reid"], [args.identity_strength])
    del state_dict

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
    )

    print("Loading remaining local Krea 2 components...", flush=True)
    pipe = module.Krea2OstrisEditPipeline.from_pretrained(
        str(model_dir),
        transformer=transformer,
        torch_dtype=torch.bfloat16,
        local_files_only=True,
    )
    pipe.set_adapters(["reid"], [args.identity_strength])

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
