# Bubblewrap Launcher Generator

A standalone, browser-only editor for generating Linux Bubblewrap launch scripts. Open this page under the gpages site at /bubblewrap-launcher/. No account, build tools, cloud service, or server is required. All configuration happens locally inside the browser; editing paths does not upload anything. Browser localStorage remembers the last profile.

## Quick start

1. Choose an empty, bridge, development, or Minecraft/CUDA preset. Edit all real host paths to match your own Linux machine.
2. Add each required folder (or file), assign its sandbox destination, and choose read-only (default), read/write, or "Hide (empty)" for sandbox directories.
3. Select network sharing, system runtime mode, and optional GPU/CUDA/Wayland exposure.
4. Check the live warnings, download **start-isolated.sh**, review its contents, then on Linux run:
   \`\`\`bash
   chmod +x start-isolated.sh
   ./start-isolated.sh
   \`\`\`
   Or pass a command: \`./start-isolated.sh /bin/bash -lc 'echo hello'\`.
5. Export/import a JSON profile for repeat use. The browser cannot enumerate real Linux folders, so it does not provide a native folder picker with absolute host paths.

**Requirements:** Linux with Bubblewrap (\`bwrap\`), Bash on the host, user namespaces / appropriate Bubblewrap setup, and any selected applications installed on the host. This is a launcher generator, not an installer or an MCP-server/tunnel configuration tool.

## What each toggle really does

- **Host folders:** The default is no host workspace mounts. \`--ro-bind SRC DEST\` provides read-only access to the directory and its descendants; \`--bind\` provides real, persistent read/write access to all contents. Nested mounts are applied parent-first, and narrower mounts override the broader path at that location. \`--tmpfs DEST\` hides a directory with an empty, temporary filesystem; it is not a universal path-deny mechanism. Source paths accept absolute paths, \`~/...\`, \`$HOME/...\`, or \`\${HOME}/...\`, and they are expanded in Bash *without* \`eval\`. Missing required sources fail; optional ones are skipped.
- **Program visibility:** Full mode exposes host \`/usr\` and related compatibility paths read-only. This means *all* host executables in those paths are available. Selected mode mounts named executable files, shared library directories, and support data; it is a **best-effort convenience** for command availability, not a security-grade execution allowlist. It can fail for scripts and tools requiring unmounted private helpers or tool-specific assets.
- **Networking:** OFF uses \`--unshare-net\`, providing no usable network by default. ON retains the **host network namespace**. This does **not** limit traffic to outbound connections or block listening sockets.
- **GPU:** The DRI option exposes \`/dev/dri\`. NVIDIA also shares detected \`/dev/nvidia*\` devices and NVIDIA capability nodes. Kernel driver versions and device permissions remain those of the host. CUDA toolkit is a separate read-only mount for \`/opt/cuda*\` / \`/usr/local/cuda*\`, and host runtime/driver libraries are still required. GPU passthrough is powerful and broadens the attack surface.
- **Wayland:** Optional desktop socket access; **not needed for headless CUDA**. Exposing a session socket gives code access to desktop-level interactions.
- **Environment:** Clear inherited environment by default. User-specified variables are embedded in plaintext in the launcher *and* the profile (including local browser storage), so don't include credentials.

The launcher includes its own \`/proc\`, a minimal \`/dev\`, temporary \`/tmp\`, clean \`HOME\` and \`/workspace\` directories, and isolated PID, IPC, and UTS namespaces by default. It inherits the host user's permissions and is **not a VM, security boundary against kernel exploits, or a guarantee of safe execution for hostile code**.

Mount destinations \`/\`, \`/usr\`, \`/bin\`, \`/sbin\`, \`/lib\`, \`/lib64\`, \`/lib32\`, \`/etc\`, \`/dev\`, \`/proc\`, \`/sys\`, and \`/tmp\` are reserved to keep the generated base runtime coherent. A custom HOME or workdir path can be chosen freely apart from \`/dev\` and \`/proc\`.

## Testing

\`\`\`bash
node --test bubblewrap-launcher/tests/*.test.mjs
\`\`\`

The tests check Bash syntax, path validation/quoting, mount order, system-tool modes, network isolation flags, and generated launcher execution against a fake \`bwrap\` that records arguments instead of creating namespaces. For a real sandbox, host-side smoke tests remain necessary (especially NVIDIA/CUDA and exotic symlink layouts).
