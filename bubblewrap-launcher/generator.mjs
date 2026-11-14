// Client-side Bubblewrap launcher generator. No data leaves the browser.
export const TOOL_GROUPS = Object.freeze({
  basic: ["ls", "cat", "cp", "mv", "rm", "mkdir", "find", "grep", "sed", "awk", "readlink", "realpath", "tar"],
  development: ["git", "make", "gcc", "g++", "pkg-config", "cmake", "ninja", "clang"],
  languages: ["python3", "node", "java", "javac", "ruby"],
  network: ["curl", "wget", "ssh", "rsync"],
  gpu: ["nvidia-smi", "nvcc"]
});

const BASE = {
  name: "My sandbox",
  network: false,
  runtime: "full",
  shell: "bash",
  workdir: "/workspace",
  home: "/home/sandbox",
  gpu: "none",
  cudaToolkit: false,
  wayland: false,
  pid: true,
  ipc: true,
  uts: true,
  clearenv: true,
  mounts: [],
  tools: ["ls", "cat", "git", "python3"],
  extraTools: "",
  env: []
};

export function makePreset(id = "empty") {
  const p = structuredClone(BASE);
  if (id === "bridge") {
    p.name = "Gentoo shell bridge";
    p.network = true;
    p.mounts = [
      {source: "$HOME/Documents/gentoo-shell-mcp", target: "/workspace", access: "rw", optional: false}
    ];
    p.tools = [...TOOL_GROUPS.basic, ...TOOL_GROUPS.development, "python3", "node"];
  } else if (id === "minecraft") {
    p.name = "Minecraft + CUDA";
    p.network = true;
    p.gpu = "nvidia";
    p.cudaToolkit = true;
    p.mounts = [
      {source: "$HOME/Documents/minecraft-dev", target: "/workspace", access: "rw", optional: false},
      {source: "$HOME/.local/share/PrismLauncher", target: "/minecraft/prism/native", access: "rw", optional: true}
    ];
    p.tools = [...TOOL_GROUPS.basic, ...TOOL_GROUPS.development, "python3", "java", "javac", "nvcc", "nvidia-smi"];
  } else if (id === "developer") {
    p.name = "Development workspace";
    p.network = true;
    p.mounts = [
      {source: "$HOME/Documents/sandbox-work", target: "/workspace", access: "rw", optional: false}
    ];
    p.tools = [...TOOL_GROUPS.basic, ...TOOL_GROUPS.development, ...TOOL_GROUPS.languages];
  } else if (id !== "empty") {
    throw new Error("Unknown preset: " + id);
  }
  return p;
}

const hasControls = (s) => /[\x00-\x1f\x7f]/.test(s);
const cleanPath = (s) => s === "/" ? s : s.replace(/\/+$/, "");
function isTargetPath(s) {
  return s.startsWith("/") && !hasControls(s) &&
    !s.split("/").some((part) => part === "." || part === "..") && !s.includes("//");
}
function isHostPath(s) {
  return (s.startsWith("/") || s === "~" || s.startsWith("~/") ||
    s === "$HOME" || s.startsWith("$HOME/") ||
    s === "${HOME}" || s.startsWith("${HOME}/")) && !hasControls(s);
}
const depth = (path) => path.split("/").filter(Boolean).length;
const shellQuote = (s) => "'" + String(s).replace(/'/g, "'\\''") + "'";

export function validateProfile(input) {
  const errors = [];
  const warnings = [];
  const p = {...structuredClone(BASE), ...(input || {})};
  p.mounts = Array.isArray(p.mounts) ? p.mounts : [];
  p.tools = Array.isArray(p.tools) ? p.tools : [];
  p.env = Array.isArray(p.env) ? p.env : [];

  if (typeof p.name !== "string" || !p.name.trim() || hasControls(p.name)) errors.push("Give this launcher a one-line name.");
  if (!["full", "selected"].includes(p.runtime)) errors.push("Choose a valid system tool mode.");
  if (!["bash", "sh"].includes(p.shell)) errors.push("Choose Bash or sh.");
  if (!["none", "dri", "nvidia"].includes(p.gpu)) errors.push("Choose a valid GPU mode.");
  if (!isTargetPath(p.workdir) || p.workdir === "/") errors.push("Working directory must be an absolute sandbox path.");
  if (!isTargetPath(p.home) || p.home === "/") errors.push("HOME must be an absolute sandbox path.");
  p.workdir = cleanPath(String(p.workdir || ""));
  p.home = cleanPath(String(p.home || ""));
  if (p.workdir === "/proc" || p.workdir === "/dev" || p.home === "/proc" || p.home === "/dev") errors.push("HOME and workdir cannot be /proc or /dev.");
  if (p.mounts.length > 150) errors.push("Too many mounts (maximum 150).");

  const destinations = new Set();
  const reserved = new Set(["/", "/usr", "/bin", "/sbin", "/lib", "/lib64", "/lib32", "/etc", "/dev", "/proc", "/sys", "/tmp"]);
  const normalized = [];
  p.mounts.forEach((raw, i) => {
    const m = {...raw};
    m.target = typeof m.target === "string" ? cleanPath(m.target.trim()) : "";
    m.access = m.access || "ro";
    m.optional = Boolean(m.optional);
    if (!["rw", "ro", "mask"].includes(m.access)) errors.push("Mount " + (i + 1) + ": invalid permission.");
    if (!isTargetPath(m.target)) errors.push("Mount " + (i + 1) + ": sandbox path must be absolute, without . or .. segments.");
    if (reserved.has(m.target)) errors.push("Mount " + (i + 1) + ": " + m.target + " is reserved for the sandbox runtime.");
    if (destinations.has(m.target)) errors.push("Mount " + (i + 1) + ": duplicate sandbox destination " + m.target + ".");
    destinations.add(m.target);
    if (m.access !== "mask") {
      m.source = typeof m.source === "string" ? m.source.trim() : "";
      if (!isHostPath(m.source)) errors.push("Mount " + (i + 1) + ": host path must be absolute or start with ~/ or $HOME/.");
      if (m.access === "rw" && (m.source === "$HOME" || m.source === "~" || m.source === "/home")) {
        warnings.push("Mount " + (i + 1) + " gives writable access to a very broad home directory.");
      }
    } else {
      m.source = "";
      m.optional = false;
    }
    normalized.push(m);
  });
  p.mounts = normalized.sort((a, b) => depth(a.target) - depth(b.target));

  const tools = [...p.tools, ...String(p.extraTools || "").split(/[,\s]+/).filter(Boolean)];
  const toolSet = new Set();
  for (const tool of tools) {
    if (typeof tool !== "string" || !/^[A-Za-z0-9][A-Za-z0-9_.+-]*$/.test(tool)) {
      errors.push("Tool names must be simple command names (letters, numbers, +, -, _, .).");
    } else toolSet.add(tool);
  }
  p.tools = [...toolSet].sort();

  const envKeys = new Set();
  p.env = p.env.map((raw, i) => {
    const e = {name: String(raw?.name || "").trim(), value: String(raw?.value ?? "")};
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(e.name)) errors.push("Environment row " + (i + 1) + ": invalid variable name.");
    if (hasControls(e.value)) errors.push("Environment row " + (i + 1) + ": values must be one line.");
    if (envKeys.has(e.name)) errors.push("Environment variable " + e.name + " is duplicated.");
    envKeys.add(e.name);
    if (["HOME", "PATH", "PWD"].includes(e.name)) warnings.push("Overriding " + e.name + " can break shell setup.");
    return e;
  });

  if (p.runtime === "selected") warnings.push("Selected tools mode is best-effort, not a strict execution whitelist: interpreters, shared libraries, and scripts can still execute other code. Complex tools may need full runtime.");
  if (p.network) warnings.push("Network ON shares the host network namespace; it is not an outbound-only firewall.");
  if (p.gpu !== "none") warnings.push("GPU device access exposes real host hardware interfaces and increases sandbox privileges.");
  if (p.cudaToolkit && p.gpu !== "nvidia") warnings.push("CUDA toolkit access does not itself grant NVIDIA GPU device access.");
  if (p.wayland) warnings.push("Wayland socket access permits interaction with the desktop session, not merely GPU compute.");
  if (p.mounts.length === 0) warnings.push("No host folders are shared. /workspace exists but is temporary.");
  if (p.mounts.some((m) => m.access === "rw")) warnings.push("Read/write mounts can change or delete real host files, including their contents.");
  return {profile: p, errors, warnings};
}

export function buildLauncher(input) {
  const {profile: p, errors, warnings} = validateProfile(input);
  if (errors.length) throw new Error(errors.join("\n"));
  const lines = [
    "#!/usr/bin/env bash",
    "# Generated by the gpages Bubblewrap Launcher Generator.",
    "# Profile: " + p.name,
    "# Inspect this file before running it. It executes with your host user privileges.",
    "set -Eeuo pipefail",
    "",
    "command -v bwrap >/dev/null 2>&1 || { echo 'Error: install bubblewrap (bwrap) first.' >&2; exit 1; }",
    ": \"@@{HOME:?HOME must be set on the host}\"",
    "",
    "bwrap_args=(",
    "  --die-with-parent",
    p.pid ? "  --unshare-pid" : "",
    p.ipc ? "  --unshare-ipc" : "",
    p.uts ? "  --unshare-uts" : "",
    p.network ? "" : "  --unshare-net",
    "  --proc /proc",
    "  --dev /dev",
    "  --tmpfs /tmp",
    "  --dir /workspace",
    "  --dir /home",
    ")",
    "",
    "# Keep host system tools read-only; they are NOT a per-command whitelist.",
    "if [[ " + shellQuote(p.runtime) + " == 'full' ]]; then",
    "  for path in /usr /bin /sbin /lib /lib64 /lib32; do",
    "    if [[ -L \"$path\" ]]; then",
    "      bwrap_args+=(--symlink \"$(readlink \"$path\")\" \"$path\")",
    "    elif [[ -d \"$path\" ]]; then",
    "      bwrap_args+=(--ro-bind \"$path\" \"$path\")",
    "    fi",
    "  done",
    "else",
    "  bwrap_args+=(--dir /usr --dir /usr/bin --dir /usr/sbin)",
    "  for path in /usr/lib /usr/lib64 /lib /lib64 /lib32; do",
    "    if [[ -L \"$path\" ]]; then",
    "      bwrap_args+=(--symlink \"$(readlink \"$path\")\" \"$path\")",
    "    elif [[ -d \"$path\" ]]; then",
    "      bwrap_args+=(--ro-bind \"$path\" \"$path\")",
    "    fi",
    "  done",
    "  # /usr/share is needed by many ordinary utilities, interpreters and compilers.",
    "  [[ ! -d /usr/share ]] || bwrap_args+=(--ro-bind /usr/share /usr/share)",
    "  # Gentoo and other merged-/usr distros rely on these compatibility paths.",
    "  for path in /bin /sbin; do",
    "    if [[ -L \"$path\" ]]; then",
    "      bwrap_args+=(--symlink \"$(readlink \"$path\")\" \"$path\")",
    "    else",
    "      bwrap_args+=(--dir \"$path\")",
    "    fi",
    "  done",
    "  mount_tool() {",
    "    local tool=\"$1\" src",
    "    if ! src=\"$(type -P \"$tool\")\" || [[ ! -f \"$src\" ]]; then",
    "      echo \"Error: selected tool '$tool' is not installed on the host.\" >&2",
    "      exit 1",
    "    fi",
    "    bwrap_args+=(--ro-bind \"$src\" \"/usr/bin/$tool\")",
    "  }",
    "  # Core interpreters are always needed to launch a shell and shebang scripts.",
    "  mount_tool bash",
    "  mount_tool sh",
    "  mount_tool env"
  ].filter(Boolean);
  if (p.runtime === "selected") {
    // Only the selected-mode shell path runs this loop, not the full-mode path.
    for (const tool of p.tools.filter((t) => !["bash", "sh", "env"].includes(t))) {
      lines.push("  mount_tool " + shellQuote(tool));
    }
  }
  lines.push(
    "fi",
    "",
    "# Only a small group of non-secret /etc configuration files is visible.",
    "for path in /etc/passwd /etc/group /etc/hosts /etc/hostname /etc/resolv.conf /etc/nsswitch.conf /etc/host.conf /etc/gai.conf /etc/localtime /etc/ld.so.cache /etc/ld.so.conf; do",
    "  [[ ! -e \"$path\" ]] || bwrap_args+=(--ro-bind \"$path\" \"$path\")",
    "done",
    "for path in /etc/ssl /etc/ca-certificates /etc/pki /etc/ld.so.conf.d; do",
    "  [[ ! -d \"$path\" ]] || bwrap_args+=(--ro-bind \"$path\" \"$path\")",
    "done",
    "",
    "expand_host_path() {",
    "  local path=\"$1\"",
    "  case \"$path\" in",
    "    '~') printf '%s' \"$HOME\" ;;",
    "    '~/'*) printf '%s/%s' \"$HOME\" \"@@{path:2}\" ;;",
    "    '$HOME') printf '%s' \"$HOME\" ;;",
    "    '$HOME/'*) printf '%s/%s' \"$HOME\" \"@@{path:6}\" ;;",
    "    '@@{HOME}') printf '%s' \"$HOME\" ;;",
    "    '@@{HOME}/'*) printf '%s/%s' \"$HOME\" \"@@{path:8}\" ;;",
    "    /*) printf '%s' \"$path\" ;;",
    "    *) echo \"Invalid host path: $path\" >&2; return 1 ;;",
    "  esac",
    "}",
    "add_mount() {",
    "  local source target access optional",
    "  source=\"$(expand_host_path \"$1\")\"; target=\"$2\"; access=\"$3\"; optional=\"$4\"",
    "  if [[ ! -e \"$source\" && ! -L \"$source\" ]]; then",
    "    if [[ \"$optional\" == 1 ]]; then",
    "      printf 'Skipping optional missing path: %s\\n' \"$source\" >&2",
    "      return 0",
    "    fi",
    "    printf 'Error: required host path does not exist: %s\\n' \"$source\" >&2",
    "    exit 1",
    "  fi",
    "  if [[ \"$access\" == rw ]]; then",
    "    bwrap_args+=(--bind \"$source\" \"$target\")",
    "  else",
    "    bwrap_args+=(--ro-bind \"$source\" \"$target\")",
    "  fi",
    "}",
    "",
    "# Your mounts, ordered parent-first so child overrides work correctly."
  );
  for (const m of p.mounts) {
    if (m.access === "mask") lines.push("bwrap_args+=(--tmpfs " + shellQuote(m.target) + ") # empty temporary directory");
    else lines.push("add_mount " + shellQuote(m.source) + " " + shellQuote(m.target) + " " + shellQuote(m.access) + " " + (m.optional ? "1" : "0"));
  }
  lines.push("");
  if (p.gpu === "dri" || p.gpu === "nvidia") {
    lines.push(
      "# Direct Rendering Infrastructure nodes (actual host GPU devices).",
      "if [[ -d /dev/dri ]]; then bwrap_args+=(--dev-bind /dev/dri /dev/dri); fi",
      "bwrap_args+=(--ro-bind-try /sys /sys)"
    );
  }
  if (p.gpu === "nvidia") {
    lines.push(
      "# Include all detected NVIDIA device nodes for the installed driver.",
      "for node in /dev/nvidia[0-9]* /dev/nvidiactl /dev/nvidia-uvm /dev/nvidia-uvm-tools /dev/nvidia-modeset; do",
      "  [[ ! -e \"$node\" ]] || bwrap_args+=(--dev-bind \"$node\" \"$node\")",
      "done",
      "if [[ -d /dev/nvidia-caps ]]; then bwrap_args+=(--dev-bind /dev/nvidia-caps /dev/nvidia-caps); fi",
      "[[ -e /dev/nvidiactl ]] || echo 'Warning: NVIDIA GPU devices not found on host.' >&2"
    );
  }
  if (p.cudaToolkit) {
    lines.push(
      "# CUDA compiler / toolkit directories. Libraries under /usr are already visible.",
      "cuda_root=''",
      "for root in /opt/cuda /opt/cuda-* /usr/local/cuda /usr/local/cuda-*; do",
      "  if [[ -d \"$root\" ]]; then",
      "    bwrap_args+=(--ro-bind \"$root\" \"$root\")",
      "    [[ -n \"$cuda_root\" ]] || cuda_root=\"$root\"",
      "  fi",
      "done",
      "if [[ -n \"$cuda_root\" ]]; then",
      "  bwrap_args+=(--setenv CUDA_HOME \"$cuda_root\" --setenv CUDA_PATH \"$cuda_root\")",
      "  bwrap_args+=(--setenv PATH \"$cuda_root/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin\")",
      "  bwrap_args+=(--setenv LD_LIBRARY_PATH \"$cuda_root/lib64:$cuda_root/targets/x86_64-linux/lib:/usr/lib64:/usr/lib\")",
      "else",
      "  echo 'Warning: CUDA toolkit not found under /opt/cuda* or /usr/local/cuda*.' >&2",
      "fi"
    );
  }
  if (p.wayland) {
    lines.push(
      "# A desktop socket is powerful: it is not required for headless CUDA.",
      "if [[ -n \"@@{XDG_RUNTIME_DIR:-}\" && -n \"@@{WAYLAND_DISPLAY:-}\" && -S \"$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY\" ]]; then",
      "  bwrap_args+=(--bind \"$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY\" \"$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY\")",
      "  bwrap_args+=(--setenv XDG_RUNTIME_DIR \"$XDG_RUNTIME_DIR\" --setenv WAYLAND_DISPLAY \"$WAYLAND_DISPLAY\")",
      "else",
      "  echo 'Warning: no Wayland session socket found.' >&2",
      "fi"
    );
  }
  lines.push(
    "",
    "# Sandbox environment, working directory, and startup command.",
    p.clearenv ? "bwrap_args+=(--clearenv)" : "# Inheriting host environment (less isolated).",
    "bwrap_args+=(--setenv HOME " + shellQuote(p.home) + ")",
    "bwrap_args+=(--setenv PATH '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin')",
    "bwrap_args+=(--setenv LANG 'C.UTF-8')",
    "bwrap_args+=(--setenv TERM \"@@{TERM:-xterm-256color}\")"
  );
  // CUDA overrides PATH after the base environment, so its compiler is discoverable.
  if (p.cudaToolkit) {
    lines.push("if [[ -n \"@@{cuda_root:-}\" ]]; then bwrap_args+=(--setenv PATH \"$cuda_root/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin\"); fi");
  }
  for (const e of p.env) lines.push("bwrap_args+=(--setenv " + shellQuote(e.name) + " " + shellQuote(e.value) + ")");
  lines.push(
    "bwrap_args+=(--dir " + shellQuote(p.home) + ")",
    "bwrap_args+=(--dir " + shellQuote(p.workdir) + ")",
    "bwrap_args+=(--chdir " + shellQuote(p.workdir) + ")",
    "",
    "# Pass an optional command to the launcher; otherwise open an interactive shell.",
    "if (( $# )); then",
    "  cmd=(\"$@\")",
    "else",
    p.shell === "bash" ? "  cmd=(/bin/bash --noprofile --norc -i)" : "  cmd=(/bin/sh -i)",
    "fi",
    "exec bwrap \"@@{bwrap_args[@]}\" -- \"@@{cmd[@]}\"",
    ""
  );
  return {script: lines.join("\n").replaceAll("@@{", "\${"), warnings};
}
