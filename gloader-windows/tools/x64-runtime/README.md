# gloader x64 Terraria runtime

`gloader.exe` is a 64-bit .NET 10 host. Stock Terraria 1.4.5.8 is still a legacy 32-bit/XNA managed executable, so it cannot be loaded into that process directly.

This tool builds a **private, user-derived 64-bit Terraria runtime** from the Terraria installation you already own. gloader does not ship Terraria source or a rebuilt Terraria binary.

## What it borrows

The builder pins `gold-meridian/terraria-unified` tag `v0.3.3` at commit:

```text
f98c9a42a59c15022cea3f6ad3750d1f85578f61
```

That tag targets Terraria 1.4.5.8.

The builder stops after the upstream patch stages:

```text
vanilla decompile
    -> patch terraria
    -> patch netcore
    -> build
```

It intentionally does **not** run:

```text
patch unified
patch tml
```

So we are borrowing the modern .NET/FNA platform port, not Terraria Unified gameplay/QoL changes and not tModLoader's mod system.

## Result

The private runtime is installed under:

```text
gdeps\x64-runtime\
```

The important managed target is:

```text
gdeps\x64-runtime\TerrariaRelease.dll
```

When present, gloader selects it automatically. The same managed Terraria build acts as client or dedicated server; server mode is selected with Terraria's `-server` launch parameter.

## One-time build

Requirements on the Windows machine performing the build:

- your Steam Terraria 1.4.5.8 installation with `Terraria.exe`
- internet access

You do **not** need Git or the .NET 10 SDK installed. The builder downloads exact private portable copies on demand, verifies their upstream hashes before extraction, and caches them under:

```text
%LOCALAPPDATA%\gloader\toolchain\
```

The pinned toolchain is:

- .NET SDK `10.0.400` for Windows x64, downloaded from Microsoft's official .NET build host and verified with Microsoft's published SHA-512
- MinGit `2.55.0.windows.5` x64, downloaded from the official Git for Windows GitHub release and verified with its published SHA-256

Nothing is installed system-wide. gloader does not persist PATH changes, write installer registry entries, or require administrator privileges for the toolchain.

From the Terraria directory after extracting gloader:

```powershell
powershell -ExecutionPolicy Bypass -File .\gdeps\tools\x64-runtime\Build-X64Runtime.ps1 -TerrariaDirectory .
```

The script:

1. verifies the local Terraria client executable;
2. prepares or reuses the pinned private .NET/MinGit toolchain under `%LOCALAPPDATA%\gloader\toolchain`;
3. clones the exact pinned upstream workspace into `%LOCALAPPDATA%\gloader\x64-runtime-workspace`;
4. decompiles your own Terraria executable; the pinned setup fetches the exact 1.4.5.8 server executable from `terraria.org` when needed;
5. applies only the vanilla cleanup and TerrariaNetCore platform patches;
6. builds Release with the private .NET 10 SDK/FNA;
7. redirects the upstream install target into `gdeps\x64-runtime` instead of overwriting Steam Terraria;
8. writes `gloader-x64-runtime.json` with the source Terraria SHA-256, exact upstream revision, and toolchain versions used.

The original Steam `Terraria.exe` is left in place and untouched.

## Why not just mark Terraria.exe x64?

The old executable is built against the legacy .NET Framework/XNA stack. The memory ceiling is not fixed by changing a PE bit. The real port replaces that platform layer with modern CoreCLR/FNA and matching native libraries, which is the same class of conversion that made modern tModLoader 64-bit.
