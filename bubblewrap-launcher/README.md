# Bubblewrap Launcher Generator

A standalone, browser-only editor for generating Linux Bubblewrap launch scripts. Open this page under the gpages site at /bubblewrap-launcher/. No account, build tools, cloud service, or server is required. All configuration happens locally inside the browser; editing paths does not upload anything. Browser localStorage remembers the last profile.

## Quick start

1. Choose an empty, bridge, development, or Minecraft/CUDA preset. Edit all real host paths to match your own Linux machine.
2. Add each required folder (or file), assign its sandbox destination, and choose read-only (default), read/write, or "Hide (empty)" for sandbox directories.
3. Select network sharing, system runtime mode, and optional GPU/CUDA/Wayland exposure.
4. Check the live warnings, download **start-isolated.sh**, review its contents, then on Linux run:
   ```bash
   chmod +x start-isolated.sh
   ./start-isolated.sh
   ```
   Or pass a command: `./start-isolated.sh /bin/bash -lc 'echo hello'`.
5. Export/import a JSON profile for repeat use. The browser cannot enumerate real Linux folders, so it does not provide a native folder picker with absolute host paths. When MCP bridge mode is selected, no-argument execution starts the server; use `--shell` to open a terminal.

**Requirements:** Linux with Bubblewrap (`bwrap`), Bash on the host, user namespaces / appropriate Bubblewrap setup, and any selected applications installed on the host. This is a launcher generator, not an installer or an MCP-server/tunnel configuration tool.

## Gentoo Shell MCP bridge mode (compatible with start-gentoo-shell)

**This is the primary startup mode for a Gentoo Shell Bridge.** It wraps the existing MCP server instead of dropping into an interactive shell. The generator does not install or modify the separate tunnel client / connector, which is what the user's $HOME/.local/bin/start-gentoo-shell convenience command normally starts.

1. Under **What happens when this launcher starts?**, select **Start Gentoo Shell MCP server**. Existing profiles saved before the mode was added automatically select MCP bridge mode; the Empty/minimal shell preset explicitly keeps plain shell mode.
2. **MCP server host folder** defaults to `$HOME/Documents/gentoo-shell-mcp`, which must contain `server.py` and an executable `.venv/bin/python`. This host directory is exposed **read-only** at `/opt/mcp`; it is **not** the same as the project folder.
3. Choose a project directory (e.g. `$HOME/Documents/ff9`) and mount it at `/workspace`. Set HOME and working directory to `/workspace` if you want the same arrangement as the original Minecraft dev bridge.
4. Download `start-isolated.sh` and back up your existing MCP launcher before replacing it at the location your existing bridge wrapper already references. Do not overwrite `server.py`, `.venv`, or your tunnel configuration.
5. Run `bash start-isolated.sh --test` on the host to check the server files/imports **inside the sandbox**, then use your normal `$HOME/.local/bin/start-gentoo-shell` wrapper to start the existing tunnel. `--shell` opens an interactive troubleshooting shell inside the same sandbox.

With no arguments, the script runs the existing MCP stdio entrypoint:

```bash
/opt/mcp/.venv/bin/python /opt/mcp/server.py
```

This matches the **default startup** of the existing Minecraft launcher but does *not* recreate Minecraft-specific operations like `--setup` (fetch Fabric template), `--build`, `--run-client`, `--run-server`, `--prism-paths`, automatic Prism discovery, or the Minecraft-specific comprehensive integration test. Those are specialized automation in the original project launcher, separate from the MCP connection contract. Add explicit mounts for the Minecraft, Prism and game directories you actually want instead of expecting automatic discovery.

Shell-only mode is still available, and keeps the old behavior of opening a shell with no arguments. **Shell-only mode cannot serve the MCP connection** just by being named `start-isolated.sh`. The wrapper's executable path and command-line invocation must match the generated script; this site cannot inspect a workstation's actual wrapper configuration.

## What each toggle really does

- **Host folders:** The default is no host workspace mounts. `--ro-bind SRC DEST` provides read-only access to the directory and its descendants; `--bind` provides real, persistent read/write access to all contents. Nested mounts are applied parent-first, and narrower mounts override the broader path at that location. `--tmpfs DEST` hides a directory with an empty, temporary filesystem; it is not a universal path-deny mechanism. Source paths accept absolute paths, `~/...`, `$HOME/...`, or `\${HOME}/...`, and they are expanded in Bash *without* `eval`. Missing required sources fail; optional ones are skipped.
- **Program visibility:** Full mode exposes host `/usr` and related compatibility paths read-only. This means *all* host executables in those paths are available. Selected mode mounts named executable files, shared library directories, and support data; it is a **best-effort convenience** for command availability, not a security-grade execution allowlist. It can fail for scripts and tools requiring unmounted private helpers or tool-specific assets.
- **Networking:** OFF uses `--unshare-net`, providing no usable network by default. ON retains the **host network namespace**. This does **not** limit traffic to outbound connections or block listening sockets.
- **GPU:** The DRI option exposes `/dev/dri`. NVIDIA also shares detected `/dev/nvidia*` devices and NVIDIA capability nodes. Kernel driver versions and device permissions remain those of the host. The launcher chooses one CUDA toolkit from `/opt/cuda`, `/usr/local/cuda`, or their versioned alternatives, then read-only mounts its resolved directory. Host runtime/driver libraries under `/usr` are still required. GPU passthrough is powerful and broadens the attack surface.
- **Wayland:** Optional desktop socket access; **not needed for headless CUDA**. Exposing a session socket gives code access to desktop-level interactions.
- **Private sandbox identity files:** The **Use private sandbox identity files** checkbox creates minimal read-only `/etc/passwd`, `/etc/group`, and `/etc/hosts` inside Bubblewrap, using the real user's username/UID/GID, the configured sandbox HOME and selected shell, and the `localhost` addresses. It uses Bubblewrap's `--ro-bind-data` and inherited Bash file descriptors to construct these entries **in memory**, without creating host-side files or changing real account information. With the checkbox off, the host versions remain available read-only as before. This improves consistency for `getpwuid()` lookups (including Python/OpenSSH) but does not grant or remove kernel privileges. Enabled by default in MCP bridge, Minecraft, and developer presets; the empty shell preset starts with it disabled. Previously saved profiles missing the option get it enabled automatically. Requires a version of Bubblewrap supporting `--ro-bind-data`.
- **Environment:** Clear inherited environment by default. User-specified variables are embedded in plaintext in the launcher *and* the profile (including local browser storage), so don't include credentials.

The launcher includes its own `/proc`, a minimal `/dev`, temporary `/tmp`, clean `HOME` and `/workspace` directories, a dedicated user namespace, `--new-session`, and isolated PID, IPC, and UTS namespaces by default. GPU support mounts only selected sysfs metadata subtrees, rather than binding all of `/sys`. When environment clearing is enabled, it happens before CUDA, Wayland, and custom environment variables are set. It inherits the host user's permissions and is **not a VM, security boundary against kernel exploits, or a guarantee of safe execution for hostile code**.

Mount destinations `/`, `/usr`, `/bin`, `/sbin`, `/lib`, `/lib64`, `/lib32`, `/etc`, `/dev`, `/proc`, `/sys`, and `/tmp` are reserved to keep the generated base runtime coherent. A custom HOME or workdir path can be chosen freely apart from `/dev` and `/proc`.

## Testing

```bash
node --test bubblewrap-launcher/tests/*.test.mjs
```

The tests check Bash syntax, path validation/quoting, mount order, system-tool modes, network isolation flags, and generated launcher execution against a fake `bwrap` that records arguments instead of creating namespaces. For a real sandbox, host-side smoke tests remain necessary (especially NVIDIA/CUDA and exotic symlink layouts).
