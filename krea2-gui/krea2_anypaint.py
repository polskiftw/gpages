#!/usr/bin/env python3
from __future__ import annotations

import argparse
import gc
import importlib.util
import os
import re
import secrets
import sys
import time
from dataclasses import dataclass
from pathlib import Path

os.environ.setdefault("HF_HUB_OFFLINE", "1")
os.environ.setdefault("TRANSFORMERS_OFFLINE", "1")

import numpy as np
import torch
from diffusers import DiffusionPipeline
from PIL import Image, ImageFilter, ImageOps

DEFAULT_ROOT = Path.home() / "ai" / "krea2"
ROOT = Path(os.environ.get("KREA2_ROOT", str(DEFAULT_ROOT))).expanduser()
MODEL_DIR = Path(os.environ.get("KREA2_MODEL_DIR", str(ROOT / "models" / "Krea-2-Turbo"))).expanduser()
ANYPAINT_DIR = Path(os.environ.get("KREA2_ANYPAINT_DIR", str(ROOT / "models" / "krea2-anypaint"))).expanduser()
VLM_PROCESSOR_DIR = Path(
    os.environ.get("KREA2_VLM_PROCESSOR_DIR", str(ROOT / "models" / "Qwen3-VL-4B-Instruct-processor"))
).expanduser()
OUT_DIR = Path(os.environ.get("KREA2_OUT", str(ROOT / "out"))).expanduser()
WEIGHT_NAME = "krea2_anypaint_rank32.safetensors"
REFERENCE_MAX_EDGE = 384
SEAM_PX = 32


@dataclass(frozen=True)
class PreparedAnyPaint:
    condition: Image.Image
    known_image: Image.Image
    keep_mask: Image.Image
    generated_mask: Image.Image
    canvas_size: tuple[int, int]
    source_bbox: tuple[int, int, int, int]

    @property
    def reference_placement(self) -> dict[str, list[float]]:
        return {"bbox_normalized": [0.0, 0.0, 1.0, 1.0]}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Krea 2 AnyPaint backend for the local Krea2 GUI")
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--mask", type=Path, required=True)
    parser.add_argument("--prompt", required=True)
    parser.add_argument("--width", type=int, required=True)
    parser.add_argument("--height", type=int, required=True)
    parser.add_argument("--bbox", type=int, nargs=4, required=True, metavar=("X0", "Y0", "X1", "Y1"))
    parser.add_argument("--seed", type=int)
    parser.add_argument("-q", "--queue", type=int, default=1)
    parser.add_argument("--steps", type=int, default=8)
    parser.add_argument("--lora-scale", type=float, default=1.0)
    parser.add_argument("--out", type=Path, default=OUT_DIR)
    parser.add_argument(
        "--no-vlm-reference",
        action="store_true",
        help="Skip Qwen3-VL reference-image prompt encoding; VAE reference conditioning remains enabled.",
    )
    return parser.parse_args()


def round_down_16(value: int) -> int:
    return max(16, value // 16 * 16)


def resize_max_edge(image: Image.Image, max_edge: int) -> Image.Image:
    scale = min(1.0, max_edge / max(image.size))
    size = (
        round_down_16(int(round(image.width * scale))),
        round_down_16(int(round(image.height * scale))),
    )
    return image.resize(size, Image.Resampling.LANCZOS)


def median_color(values: np.ndarray) -> np.ndarray:
    if values.size == 0:
        return np.array([127, 127, 127], dtype=np.uint8)
    return np.median(values.reshape(-1, 3), axis=0).round().astype(np.uint8)


def edge_aware_keep_mask(generated_mask: Image.Image, seam_px: int) -> Image.Image:
    binary = generated_mask.convert("L").point(lambda value: 255 if value > 127 else 0)
    if seam_px > 0:
        # Pillow's square MaxFilter is equivalent to the rectangular binary dilation
        # AnyPaint uses here, without adding OpenCV to the user's environment.
        binary = binary.filter(ImageFilter.MaxFilter(seam_px * 2 + 1))
    return ImageOps.invert(binary)


def prepare_anypaint(
    source: Image.Image,
    generated_mask: Image.Image,
    canvas_size: tuple[int, int],
    source_bbox: tuple[int, int, int, int],
    *,
    reference_max_edge: int = REFERENCE_MAX_EDGE,
    seam_px: int = SEAM_PX,
) -> PreparedAnyPaint:
    width, height = canvas_size
    if width < 16 or height < 16 or width % 16 or height % 16:
        raise ValueError("Canvas dimensions must be positive multiples of 16")

    source = source.convert("RGB")
    x0, y0, x1, y1 = source_bbox
    if not (0 <= x0 < x1 <= width and 0 <= y0 < y1 <= height):
        raise ValueError(f"Source bbox is outside the canvas: {source_bbox}")

    box_width, box_height = x1 - x0, y1 - y0
    source_ratio = source.width / source.height
    box_ratio = box_width / box_height
    tolerance = max(0.025, 2.0 / min(box_width, box_height))
    if abs(box_ratio / source_ratio - 1.0) > tolerance:
        raise ValueError("Source bbox must preserve the source image aspect ratio")

    placed = source.resize((box_width, box_height), Image.Resampling.LANCZOS)
    placed_values = np.asarray(placed, dtype=np.uint8)
    fill = median_color(placed_values)
    known_values = np.empty((height, width, 3), dtype=np.uint8)
    known_values[:] = fill
    known_values[y0:y1, x0:x1] = placed_values
    known_image = Image.fromarray(known_values, mode="RGB")

    mask = generated_mask.convert("L")
    if mask.size == source.size:
        mask = mask.resize((box_width, box_height), Image.Resampling.NEAREST)
        canvas_mask = Image.new("L", canvas_size, 255)
        canvas_mask.paste(mask, (x0, y0))
        mask = canvas_mask
    elif mask.size != canvas_size:
        raise ValueError("Mask must match either the source image or the output canvas")
    mask = mask.point(lambda value: 255 if value > 127 else 0)

    outside = Image.new("L", canvas_size, 255)
    outside.paste(0, source_bbox)
    generated_values_u8 = np.maximum(
        np.asarray(mask, dtype=np.uint8),
        np.asarray(outside, dtype=np.uint8),
    )
    generated = Image.fromarray(generated_values_u8, mode="L")
    generated_values = generated_values_u8 > 0
    if not generated_values.any():
        raise ValueError("The generated mask has no white pixels")

    condition_values = known_values.copy()
    known_pixels = condition_values[~generated_values]
    condition_values[generated_values] = median_color(known_pixels)
    condition = resize_max_edge(Image.fromarray(condition_values, mode="RGB"), reference_max_edge)

    return PreparedAnyPaint(
        condition=condition,
        known_image=known_image,
        keep_mask=edge_aware_keep_mask(generated, seam_px),
        generated_mask=generated,
        canvas_size=canvas_size,
        source_bbox=source_bbox,
    )


def load_anypaint_module():
    path = ANYPAINT_DIR / "pipeline.py"
    if not path.is_file():
        raise FileNotFoundError(f"AnyPaint pipeline not found: {path}. Run krea2-anypaint-setup first.")
    spec = importlib.util.spec_from_file_location("krea2_anypaint_pipeline", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load AnyPaint pipeline module: {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def cleanup_cuda() -> None:
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.empty_cache()
        torch.cuda.ipc_collect()


def install_vae_paging(vae, device: torch.device) -> None:
    """Page the VAE onto CUDA only for each encode/decode call.

    The proven 12 GB Krea2 runtime cannot afford to keep the VAE resident beside
    the block-offloaded transformer. AnyPaint needs VAE encodes before denoising,
    so wrapping the calls keeps those phases mutually exclusive in VRAM.
    """
    original_encode = vae.encode
    original_decode = vae.decode

    def paged_encode(*args, **kwargs):
        vae.to(device)
        try:
            return original_encode(*args, **kwargs)
        finally:
            vae.to("cpu")
            cleanup_cuda()

    def paged_decode(*args, **kwargs):
        vae.to(device)
        try:
            return original_decode(*args, **kwargs)
        finally:
            vae.to("cpu")
            cleanup_cuda()

    vae.encode = paged_encode
    vae.decode = paged_decode


def require_files() -> None:
    required = [
        MODEL_DIR / "model_index.json",
        ANYPAINT_DIR / "pipeline.py",
        ANYPAINT_DIR / WEIGHT_NAME,
    ]
    missing = [str(path) for path in required if not path.exists()]
    if missing:
        joined = "\n  ".join(missing)
        raise FileNotFoundError(f"Missing required AnyPaint files:\n  {joined}\nRun krea2-anypaint-setup first.")


def slug(text: str, limit: int = 48) -> str:
    value = re.sub(r"[^A-Za-z0-9]+", "-", text).strip("-").lower()
    return (value[:limit].rstrip("-") or "anypaint")


def output_path(out_dir: Path, prompt: str, seed: int, index: int) -> Path:
    stamp = time.strftime("%Y%m%d-%H%M%S") + f"-{time.time_ns() % 1_000_000_000:09d}"
    return out_dir / f"{stamp}-anypaint-{slug(prompt)}-seed{seed}-{index:03d}.png"


def main() -> int:
    args = parse_args()
    if args.queue < 1:
        raise ValueError("Queue must be at least 1")
    if args.steps < 1:
        raise ValueError("Steps must be at least 1")
    if args.seed is not None and args.seed < 0:
        raise ValueError("Seed must be zero or greater")
    if not torch.cuda.is_available():
        raise RuntimeError("AnyPaint requires CUDA in this local setup")

    require_files()
    args.out.mkdir(parents=True, exist_ok=True)

    source = Image.open(args.source).convert("RGB")
    mask = Image.open(args.mask).convert("L")
    prepared = prepare_anypaint(
        source,
        mask,
        (args.width, args.height),
        tuple(args.bbox),
    )

    print("Loading AnyPaint pipeline shell and Qwen3-VL encoder...", flush=True)
    pipe = DiffusionPipeline.from_pretrained(
        str(MODEL_DIR),
        custom_pipeline=str(ANYPAINT_DIR),
        trust_remote_code=True,
        transformer=None,
        torch_dtype=torch.bfloat16,
        local_files_only=True,
        low_cpu_mem_usage=True,
    )

    encode_reference = not args.no_vlm_reference
    if encode_reference:
        if not VLM_PROCESSOR_DIR.is_dir():
            raise FileNotFoundError(
                f"Local Qwen3-VL processor files not found: {VLM_PROCESSOR_DIR}. "
                "Run krea2-anypaint-setup first or use --no-vlm-reference."
            )
        pipe.vl_processor_id = str(VLM_PROCESSOR_DIR)

    device = torch.device("cuda")
    pipe.text_encoder.to(device)

    print("Encoding prompt and reference image...", flush=True)
    vl_images = None
    if encode_reference:
        ref_tensor = pipe._to_chw_tensor(prepared.condition).to(device)
        vl_images = pipe._prep_vl_images([ref_tensor], REFERENCE_MAX_EDGE * REFERENCE_MAX_EDGE)
    prompt_embeds, prompt_mask = pipe.encode_prompt(
        args.prompt,
        vl_images,
        num_images_per_prompt=1,
        max_sequence_length=512,
        device=device,
    )
    prompt_embeds = prompt_embeds.to("cpu", dtype=torch.bfloat16)
    prompt_mask = prompt_mask.to("cpu")
    if encode_reference:
        del ref_tensor
    del vl_images

    pipe.text_encoder.to("cpu")
    pipe.text_encoder = None
    pipe._vl_processor = None
    cleanup_cuda()

    print("Loading Krea2 AnyPaint transformer with FP8 storage and block offload...", flush=True)
    module = load_anypaint_module()
    transformer = module.Krea2Transformer2DModel.from_pretrained(
        str(MODEL_DIR),
        subfolder="transformer",
        torch_dtype=torch.bfloat16,
        local_files_only=True,
        low_cpu_mem_usage=True,
    )
    transformer.enable_layerwise_casting(
        storage_dtype=torch.float8_e4m3fn,
        compute_dtype=torch.bfloat16,
    )
    pipe.transformer = transformer
    pipe.load_lora_weights(
        str(ANYPAINT_DIR),
        weight_name=WEIGHT_NAME,
        adapter_name="anypaint",
    )
    pipe.set_adapters(["anypaint"], weights=[args.lora_scale])

    pipe.enable_group_offload(
        onload_device=device,
        offload_device=torch.device("cpu"),
        offload_type="block_level",
        num_blocks_per_group=1,
        use_stream=False,
        exclude_modules=["vae"],
    )
    pipe.vae.enable_tiling()
    install_vae_paging(pipe.vae, device)

    base_seed = args.seed if args.seed is not None else secrets.randbits(63)
    for index in range(args.queue):
        seed = base_seed + index
        print(f"Generating {index + 1} / {args.queue} — seed {seed}", flush=True)
        generator = torch.Generator(device="cpu").manual_seed(seed)
        result = pipe(
            prompt=None,
            prompt_embeds=prompt_embeds.to(device),
            prompt_embeds_mask=prompt_mask.to(device),
            image=prepared.condition,
            height=args.height,
            width=args.width,
            num_inference_steps=args.steps,
            guidance_scale=0.0,
            generator=generator,
            reference_max_pixels=REFERENCE_MAX_EDGE * REFERENCE_MAX_EDGE,
            reference_placements=[prepared.reference_placement],
            known_image=prepared.known_image,
            known_mask=prepared.keep_mask,
            vl_image_max_pixels=REFERENCE_MAX_EDGE * REFERENCE_MAX_EDGE,
            encode_reference_in_prompt=False,
            kv_cache=True,
        ).images[0]
        path = output_path(args.out, args.prompt, seed, index + 1)
        result.save(path)
        print(f"Saved {path}", flush=True)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
