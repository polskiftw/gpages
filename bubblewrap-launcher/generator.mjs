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
  launchMode: "mcp",
  mcpHostDir: "$HOME/Documents/gentoo-shell-mcp",
  workdir: "/workspace",
  home: "/home/sandbox",
  gpu: "none",
  cudaToolkit: false,
  wayland: false,
  pid: true,
  ipc: true,
  uts: true,
  clearenv: true,
  privateIdentity: true,
  mounts: [],
  tools: ["ls", "cat", "git", "python3"],
  extraTools: "",
  env: []
};

export function makePreset(id = "empty") {
  const p = structuredClone(BASE);
  if (id === "empty") {
    p.launchMode = "shell";
    p.privateIdentity = false;
  } else if (id === "bridge") {
    p.home = "/workspace";
    p.name = "Gentoo shell bridge";
    p.network = true;
    p.mounts = [
      {source: "$HOME/Documents/gentoo-shell-mcp", target: "/workspace", access: "rw", optional: false}
    ];
    p.tools = [...TOOL_GROUPS.basic, ...TOOL_GROUPS.development, "python3", "node"];
  } else if (id === "minecraft") {
    p.home = "/workspace";
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
    p.home = "/workspace";
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
  return typeof s === "string" && s.startsWith("/") && !hasControls(s) &&
    !s.split("/").some((part) => part === "." || part === "..") && !s.includes("//");
}
function isHostPath(s) {
  return typeof s === "string" && (s.startsWith("/") || s === "~" || s.startsWith("~/") ||
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
  p.privateIdentity = Boolean(p.privateIdentity);

  if (typeof p.name !== "string" || !p.name.trim() || hasControls(p.name)) errors.push("Give this launcher a one-line name.");
  if (!["full", "selected"].includes(p.runtime)) errors.push("Choose a valid system tool mode.");
  if (!["bash", "sh"].includes(p.shell)) errors.push("Choose Bash or sh.");
  if (!["shell", "mcp"].includes(p.launchMode)) errors.push("Choose an MCP bridge or interactive-shell startup mode.");
  if (p.launchMode === "mcp" && !isHostPath(p.mcpHostDir)) errors.push("MCP server host folder must be an absolute path, ~/path, or $HOME/path.");
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
    m.target = typeof m.target === "string" ? cleanPath(m.target) : "";
    m.access = m.access || "ro";
    m.optional = Boolean(m.optional);
    if (!["rw", "ro", "mask"].includes(m.access)) errors.push("Mount " + (i + 1) + ": invalid permission.");
    if (!isTargetPath(m.target)) errors.push("Mount " + (i + 1) + ": sandbox path must be absolute, without . or .. segments.");
    if (reserved.has(m.target)) errors.push("Mount " + (i + 1) + ": " + m.target + " is reserved for the sandbox runtime.");
    if (destinations.has(m.target)) errors.push("Mount " + (i + 1) + ": duplicate sandbox destination " + m.target + ".");
    if (p.launchMode === "mcp" && (m.target === "/opt" || m.target === "/opt/mcp")) errors.push("Mount " + (i + 1) + ": /opt/mcp is reserved for the automatically mounted MCP server.");
    destinations.add(m.target);
    if (m.access !== "mask") {
      m.source = typeof m.source === "string" ? m.source : "";
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

  if (p.launchMode === "mcp" && p.runtime === "selected") warnings.push("MCP mode works best with the full host toolset; selected-tools mode may omit Python interpreter helpers or program dependencies.");
  if (p.runtime === "selected") warnings.push("Selected tools mode is best-effort, not a strict execution whitelist: interpreters, shared libraries, and scripts can still execute other code. Complex tools may need full runtime.");
  if (p.network) warnings.push("Network ON shares the host network namespace; it is not an outbound-only firewall.");
  if (p.gpu !== "none") warnings.push("GPU device access exposes real host hardware interfaces and increases sandbox privileges.");
  if (p.cudaToolkit && p.gpu !== "nvidia") warnings.push("CUDA toolkit access does not itself grant NVIDIA GPU device access.");
  if (p.wayland) warnings.push("Wayland socket access permits interaction with the desktop session, not merely GPU compute.");
  if (!p.clearenv) warnings.push("Inherited host environment variables may expose credentials and private settings.");
  const homeMount = p.mounts.filter((m) => p.home === m.target || p.home.startsWith(m.target + "/")).at(-1);
  if (homeMount?.access === "ro") {
    warnings.push("HOME (" + p.home + ") is under a read-only mount (" + homeMount.target + "). Programs cannot create files directly in HOME; choose read/write for this mount or use a separate writable HOME.");
  }
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
    "  --new-session",
    "  --unshare-user",
    p.clearenv ? "  --clearenv" : "",
    p.pid ? "  --unshare-pid" : "",
    p.ipc ? "  --unshare-ipc" : "",
    p.uts ? "  --unshare-uts" : "",
    p.network ? "" : "  --unshare-net",
    "  --proc /proc",
    "  --dev /dev",
    "  --tmpfs /tmp",
    "  --dir /etc",
    ")",
    "",
  ].filter(Boolean);
  if (p.runtime === "full") {
    lines.push(
      "# Host system executables are available read-only.",
      "for path in /usr /bin /sbin /lib /lib64 /lib32; do",
      "  if [[ -L \"$path\" ]]; then",
      "    bwrap_args+=(--symlink \"$(readlink \"$path\")\" \"$path\")",
      "  elif [[ -d \"$path\" ]]; then",
      "    bwrap_args+=(--ro-bind \"$path\" \"$path\")",
      "  fi",
      "done"
    );
  } else {
    lines.push(
      "# Selected executables (best-effort, not a security whitelist).",
      "bwrap_args+=(--dir /usr --dir /usr/bin --dir /usr/sbin)",
      "for path in /usr/lib /usr/lib64 /lib /lib64 /lib32; do",
      "  if [[ -L \"$path\" ]]; then",
      "    bwrap_args+=(--symlink \"$(readlink \"$path\")\" \"$path\")",
      "  elif [[ -d \"$path\" ]]; then",
      "    bwrap_args+=(--ro-bind \"$path\" \"$path\")",
      "  fi",
      "done",
      "# /usr/share is needed by many utilities, interpreters, and compilers.",
      "[[ ! -d /usr/share ]] || bwrap_args+=(--ro-bind /usr/share /usr/share)",
      "# Gentoo and other merged-/usr distros use compatibility paths.",
      "for path in /bin /sbin; do",
      "  if [[ -L \"$path\" ]]; then",
      "    bwrap_args+=(--symlink \"$(readlink \"$path\")\" \"$path\")",
      "  else",
      "    bwrap_args+=(--dir \"$path\")",
      "  fi",
      "done",
      "mount_tool() {",
      "  local tool=\"$1\" src",
      "  if ! src=\"$(type -P \"$tool\")\" || [[ ! -f \"$src\" ]]; then",
      "    echo \"Error: selected tool '$tool' is not installed on the host.\" >&2",
      "    exit 1",
      "  fi",
      "  bwrap_args+=(--ro-bind \"$src\" \"/usr/bin/$tool\")",
      "}",
      "# Core interpreters are always needed for shells and shebang scripts.",
      "mount_tool bash",
      "mount_tool sh",
      "mount_tool env"
    );
    for (const tool of p.tools.filter((t) => !["bash", "sh", "env"].includes(t))) {
      lines.push("mount_tool " + shellQuote(tool));
    }
  }
  if (p.privateIdentity) {
    const loginShell = p.runtime === "selected"
      ? (p.shell === "bash" ? "/usr/bin/bash" : "/usr/bin/sh")
      : (p.shell === "bash" ? "/bin/bash" : "/bin/sh");
    lines.push(
      "# Generate private /etc/passwd, /etc/group, and /etc/hosts in memory.",
      "# --ro-bind-data reads inherited file descriptors; no host files are created.",
      "sandbox_user=\"$(id -un)\"",
      "sandbox_group=\"$(id -gn)\"",
      "sandbox_uid=\"$(id -u)\"",
      "sandbox_gid=\"$(id -g)\"",
      "if [[ \"$sandbox_user\" == *:* || \"$sandbox_group\" == *:* ]]; then",
      "  echo 'Error: host username or group contains a colon; cannot create a safe passwd/group entry.' >&2",
      "  exit 1",
      "fi",
      "exec {sandbox_passwd_fd}< <(printf '%s:x:%s:%s:Sandbox User:%s:%s\\n' \"$sandbox_user\" \"$sandbox_uid\" \"$sandbox_gid\" " + shellQuote(p.home) + " " + shellQuote(loginShell) + ")",
      "exec {sandbox_group_fd}< <(printf '%s:x:%s:\\n' \"$sandbox_group\" \"$sandbox_gid\")",
      "exec {sandbox_hosts_fd}< <(printf '127.0.0.1 localhost\\n::1 localhost\\n')",
      "bwrap_args+=(--ro-bind-data \"$sandbox_passwd_fd\" /etc/passwd)",
      "bwrap_args+=(--ro-bind-data \"$sandbox_group_fd\" /etc/group)",
      "bwrap_args+=(--ro-bind-data \"$sandbox_hosts_fd\" /etc/hosts)",
      ""
    );
  }
  lines.push(
    "",
    p.privateIdentity
      ? "# Expose limited system settings, but NOT the host's passwd/group/hosts files."
      : "# Expose limited host system settings, including read-only passwd/group/hosts.",
    "for path in " +
      (p.privateIdentity ? "" : "/etc/passwd /etc/group /etc/hosts ") +
      "/etc/hostname /etc/resolv.conf /etc/nsswitch.conf /etc/host.conf /etc/gai.conf /etc/localtime /etc/ld.so.cache /etc/ld.so.conf; do",
    "  [[ ! -e \"$path\" ]] || bwrap_args+=(--ro-bind \"$path\" \"$path\")",
    "done",
    "for path in /etc/ssl /etc/ca-certificates /etc/pki /etc/ld.so.conf.d /etc/java-config-2; do",
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
    "# Prepare each sandbox directory once, before applying user mounts."
  );
  for (const dir of new Set(["/workspace", ...(p.launchMode === "mcp" || p.cudaToolkit ? ["/opt"] : []), ...(p.home.startsWith("/home/") || p.home === "/home" ? ["/home"] : []), p.home, p.workdir])) {
    lines.push("bwrap_args+=(--dir " + shellQuote(dir) + ")");
  }
  lines.push(
    "",
    "# Your mounts, ordered parent-first so child overrides work correctly."
  );
  for (const m of p.mounts) {
    if (m.access === "mask") lines.push("bwrap_args+=(--tmpfs " + shellQuote(m.target) + ") # empty temporary directory");
    else lines.push("add_mount " + shellQuote(m.source) + " " + shellQuote(m.target) + " " + shellQuote(m.access) + " " + (m.optional ? "1" : "0"));
  }
  if (p.launchMode === "mcp") {
    lines.push(
      "",
      "# Mount the existing Gentoo Shell MCP server read-only.",
      "# This is separate from /workspace, which contains your project files.",
      "mcp_server_dir=\"$(expand_host_path " + shellQuote(p.mcpHostDir) + ")\"",
      "if [[ ! -f \"$mcp_server_dir/server.py\" || ! -x \"$mcp_server_dir/.venv/bin/python\" ]]; then",
      "  echo \"Error: MCP server.py or .venv/bin/python is missing in $mcp_server_dir\" >&2",
      "  echo 'Install the existing Gentoo Shell MCP server and venv first.' >&2",
      "  exit 1",
      "fi",
      "bwrap_args+=(--ro-bind \"$mcp_server_dir\" /opt/mcp)"
    );
  }
  lines.push("");
  if (p.gpu === "dri" || p.gpu === "nvidia") {
    lines.push(
      "# Direct Rendering Infrastructure nodes (actual host GPU devices).",
      "if [[ -d /dev/dri ]]; then bwrap_args+=(--dev-bind /dev/dri /dev/dri); fi",
      "# Share only GPU-relevant sysfs metadata (not the entire host /sys).",
      "bwrap_args+=(--dir /sys)",
      "for path in /sys/module /sys/bus/pci /sys/class/drm /sys/devices /sys/dev/char; do",
      "  [[ ! -d \"$path\" ]] || bwrap_args+=(--ro-bind \"$path\" \"$path\")",
      "done"
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
      "# Expose one CUDA toolkit, preferring the unversioned host installation.",
      "cuda_root=''",
      "cuda_real=''",
      "for candidate in /opt/cuda /usr/local/cuda /opt/cuda-* /usr/local/cuda-*; do",
      "  if [[ -d \"$candidate\" ]]; then",
      "    cuda_root=\"$candidate\"",
      "    cuda_real=\"$(readlink -e -- \"$candidate\")\"",
      "    break",
      "  fi",
      "done",
      "if [[ -n \"$cuda_root\" ]]; then",
      "  # Mount the resolved target only once; do not duplicate versioned installs.",
      "  bwrap_args+=(--ro-bind \"$cuda_real\" \"$cuda_real\")",
      "  if [[ \"$cuda_root\" != \"$cuda_real\" ]]; then",
      "    case \"$cuda_root\" in",
      "      /usr/*)",
      p.runtime === "selected"
        ? "        bwrap_args+=(--symlink \"$cuda_real\" \"$cuda_root\")"
        : "        # /usr is already mounted read-only, including this host symlink.",
      "        ;;",
      "      *) bwrap_args+=(--symlink \"$cuda_real\" \"$cuda_root\") ;;",
      "    esac",
      "  fi",
      "else",
      "  echo 'Warning: CUDA toolkit not found under /opt/cuda* or /usr/local/cuda*.' >&2",
      "fi"
    );
  }
  if (p.wayland) {
    lines.push(
      "# Mount exactly one Wayland socket; never expose the entire host runtime directory.",
      "uid=\"$(id -u)\"",
      "host_runtime_dir=\"@@{XDG_RUNTIME_DIR:-/run/user/$uid}\"",
      "host_wayland_socket=''",
      "if [[ -n \"@@{WAYLAND_DISPLAY:-}\" ]]; then",
      "  if [[ \"$WAYLAND_DISPLAY\" == /* ]]; then",
      "    host_wayland_socket=\"$WAYLAND_DISPLAY\"",
      "  else",
      "    host_wayland_socket=\"$host_runtime_dir/$WAYLAND_DISPLAY\"",
      "  fi",
      "else",
      "  # When the wrapper lacks WAYLAND_DISPLAY, use exactly one detected socket.",
      "  for candidate in \"$host_runtime_dir\"/wayland-*; do",
      "    [[ -S \"$candidate\" ]] || continue",
      "    if [[ -n \"$host_wayland_socket\" ]]; then",
      "      host_wayland_socket=''",
      "      break",
      "    fi",
      "    host_wayland_socket=\"$candidate\"",
      "  done",
      "fi",
      "if [[ -n \"$host_wayland_socket\" && -S \"$host_wayland_socket\" ]]; then",
      "  bwrap_args+=(--tmpfs /run --dir /run/user --perms 0700 --dir \"/run/user/$uid\")",
      "  bwrap_args+=(--bind \"$host_wayland_socket\" \"/run/user/$uid/wayland-0\")",
      "  bwrap_args+=(--setenv XDG_RUNTIME_DIR \"/run/user/$uid\" --setenv WAYLAND_DISPLAY wayland-0 --setenv XDG_SESSION_TYPE wayland)",
      "else",
      "  echo 'Warning: no unambiguous Wayland session socket found; GPU compute still works.' >&2",
      "fi"
    );
  }
  lines.push(
    "",
    "# Sandbox environment, working directory, and startup command.",
    "# --clearenv is applied before any --setenv entries in this script.",
    "bwrap_args+=(--setenv HOME " + shellQuote(p.home) + ")",
    "bwrap_args+=(--setenv LANG 'C.UTF-8')",
    "bwrap_args+=(--setenv TERM \"@@{TERM:-xterm-256color}\")",
    "sandbox_path='/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin'"
  );
  if (p.cudaToolkit) {
    lines.push(
      "if [[ -n \"$cuda_root\" ]]; then",
      "  sandbox_path=\"$cuda_root/bin:$sandbox_path\"",
      "  bwrap_args+=(--setenv CUDA_HOME \"$cuda_root\" --setenv CUDA_PATH \"$cuda_root\")",
      "  cuda_libs=''",
      "  for subdir in lib64 targets/x86_64-linux/lib lib; do",
      "    if [[ -d \"$cuda_root/$subdir\" ]]; then",
      "      cuda_libs=\"@@{cuda_libs:+$cuda_libs:}$cuda_root/$subdir\"",
      "    fi",
      "  done",
      "  [[ -z \"$cuda_libs\" ]] || bwrap_args+=(--setenv LD_LIBRARY_PATH \"$cuda_libs\")",
      "fi"
    );
  }
  lines.push("bwrap_args+=(--setenv PATH \"$sandbox_path\")");
  for (const e of p.env) lines.push("bwrap_args+=(--setenv " + shellQuote(e.name) + " " + shellQuote(e.value) + ")");
  lines.push(
    "bwrap_args+=(--chdir " + shellQuote(p.workdir) + ")",
    "",
    "# Command to execute after setting up the sandbox."
  );
  const shellCommand = p.shell === "bash"
    ? (p.runtime === "selected" ? "/usr/bin/bash --noprofile --norc -i" : "/bin/bash --noprofile --norc -i")
    : (p.runtime === "selected" ? "/usr/bin/sh -i" : "/bin/sh -i");
  if (p.launchMode === "mcp") {
    const testShell = p.runtime === "selected" ? "/usr/bin/bash" : "/bin/bash";
    const testScript = 'cd /opt/mcp; test -r server.py; /opt/mcp/.venv/bin/python -c \'import server; print("PASS: MCP server imports")\'; echo "PASS: MCP sandbox starts in $PWD (HOME=$HOME)"';
    lines.push(
      "# No arguments starts the MCP server over stdio, as the existing bridge expects.",
      "case \"@@{1:-}\" in",
      "  '') cmd=(/opt/mcp/.venv/bin/python /opt/mcp/server.py) ;;",
      "  --shell) cmd=(" + shellCommand + ") ;;",
      "  --test) cmd=(" + testShell + " -ec " + shellQuote(testScript) + ") ;;",
      "  --help|-h) echo 'Usage: start-isolated.sh [--shell|--test|COMMAND [ARGS...]]'; exit 0 ;;",
      "  *) cmd=(\"$@\") ;;",
      "esac"
    );
  } else {
    lines.push(
      "# General-purpose shell mode; pass a command to run it instead of a shell.",
      "if (( $# )); then",
      "  cmd=(\"$@\")",
      "else",
      "  cmd=(" + shellCommand + ")",
      "fi"
    );
  }
  lines.push(
    "exec bwrap \"@@{bwrap_args[@]}\" -- \"@@{cmd[@]}\"",
    ""
  );
  return {script: lines.join("\n").replaceAll("@@{", "\${"), warnings};
}
