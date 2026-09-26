#!/bin/sh
set -eu

root=${KREA2_ROOT:-"$HOME/ai/krea2"}
python="$root/.venv/bin/python"
backend="$HOME/.local/lib/krea2-gui/krea2_anypaint.py"
anypaint_dir="$root/models/krea2-anypaint"
processor_dir="$root/models/Qwen3-VL-4B-Instruct-processor"
wrapper="$root/bin/krea2-anypaint"

if [ ! -x "$python" ]; then
    printf 'Missing Krea2 virtualenv Python: %s\n' "$python" >&2
    exit 1
fi
if [ ! -f "$backend" ]; then
    printf 'Missing installed AnyPaint backend: %s\n' "$backend" >&2
    printf 'Run the krea2-gui install-user.sh first.\n' >&2
    exit 1
fi
if [ ! -f "$root/models/Krea-2-Turbo/model_index.json" ]; then
    printf 'Missing local Krea-2-Turbo model under %s\n' "$root/models/Krea-2-Turbo" >&2
    exit 1
fi

mkdir -p "$root/models" "$root/bin" "$root/cache/huggingface" "$root/cache/torch" "$root/cache/uv"

HF_HOME="$root/cache/huggingface" "$python" - "$anypaint_dir" "$processor_dir" <<'PY'
from pathlib import Path
import sys
from huggingface_hub import snapshot_download

anypaint_dir = Path(sys.argv[1])
processor_dir = Path(sys.argv[2])
anypaint_revision = "1a9fb37a304c27523939c44fc2b770c11472451b"

print("Downloading Krea2 AnyPaint pipeline + adapter...")
snapshot_download(
    repo_id="yijunwang2/krea2-anypaint",
    revision=anypaint_revision,
    local_dir=anypaint_dir,
    allow_patterns=[
        "pipeline.py",
        "anypaint.py",
        "krea2_anypaint_rank32.safetensors",
        "README.md",
        "NOTICE",
        "PIPELINE_LICENSE",
        "LICENSE.pdf",
        "SHA256SUMS",
    ],
)

print("Downloading Qwen3-VL processor/tokenizer metadata (no model weights)...")
snapshot_download(
    repo_id="Qwen/Qwen3-VL-4B-Instruct",
    local_dir=processor_dir,
    allow_patterns=[
        "*.json",
        "*.txt",
        "*.model",
        "*.jinja",
        "*.tiktoken",
    ],
    ignore_patterns=[
        "*.safetensors",
        "*.bin",
        "*.pt",
        "*.pth",
        "*.onnx",
        "*.gguf",
    ],
)
PY

cat > "$wrapper" <<EOF
#!/bin/sh
set -eu
export KREA2_ROOT="$root"
export HF_HOME="$root/cache/huggingface"
export TORCH_HOME="$root/cache/torch"
export UV_CACHE_DIR="$root/cache/uv"
export PYTORCH_ALLOC_CONF=expandable_segments:True
export HF_HUB_OFFLINE=1
export TRANSFORMERS_OFFLINE=1
exec "$python" "$backend" "\$@"
EOF
chmod 0755 "$wrapper"

printf '\nAnyPaint installed.\n'
printf '  runtime: %s\n' "$anypaint_dir"
printf '  processor metadata: %s\n' "$processor_dir"
printf '  command: %s\n' "$wrapper"
