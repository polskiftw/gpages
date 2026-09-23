# gloader Windows maintenance snapshot

This directory is the Windows gloader 0.2.x maintenance line moved from `polskiftw/gloader`.

Source snapshot: `713c95edb299029f154d35c891b23511142f4f7f`

The Linux-native implementation now lives in the `polskiftw/gloader` repository. This Windows tree intentionally preserves the established CoreCLR/FNA x64 runtime design rather than being refactored to match Linux.

## Build

From PowerShell in this directory:

```powershell
./build.ps1
```

The build produces the Windows package under `dist/gloader`. Expanded Worlds is bundled from `gmods/ExpandedWorlds`, matching the source layout expected by `build.ps1`.

Do not treat this directory as the architecture template for Linux gloader.
