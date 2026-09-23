# gloader

Tiny raw-C# runtime mod loader for Terraria, now hosted as a native 64-bit .NET 10 process.

gloader does not load stock 32-bit/XNA `Terraria.exe` into its process anymore. Instead, it builds a private user-derived Terraria 1.4.5.8 runtime on modern CoreCLR/FNA, compiles each source mod in memory against that exact rebuilt game assembly, applies Harmony patches, and then invokes Terraria normally. The original Steam `Terraria.exe` is left untouched and gloader does not distribute a rebuilt Terraria binary.

## Install layout

Copy the built gloader files directly into the Terraria installation folder. `gloader.exe` remains the one public launcher beside the user's original `Terraria.exe`; `gmods/` contains only mod folders, and `gdeps/` contains gloader's private runtime/support files, logs, and the locally generated 64-bit Terraria runtime.

```text
Terraria/
  Terraria.exe
  gloader.exe
  gmods/
    InfiniteAngler/
    NoLiquidDupe/
    Radio/
    DVDLogo/
  gdeps/
    gloader.dll
    coreclr.dll
    clrjit.dll
    [other gloader/.NET runtime files]
    tools/
      x64-runtime/
        Build-X64Runtime.ps1
    x64-runtime/
      TerrariaRelease.dll
      Libraries/
      [private Terraria CoreCLR/FNA runtime files]
    logs/
```

The root stays intentionally clean: the published package has exactly one root file, `gloader.exe`, plus the `gmods/` and `gdeps/` folders. The public apphost points at `gdeps/gloader.dll`, so CoreCLR and Harmony can use normal physical runtime modules such as `clrjit.dll` without creating a second user-facing launcher executable.

## gmods and gdeps folder contract

`gmods/` is for mods only. Each immediate subfolder containing enabled `.cs` files is treated as one mod. There should be no gloader dependency DLLs or log files loose in this directory.

```text
gmods/
  InfiniteAngler/
    Main.cs
    InfiniteAngler.ini
  NoLiquidDupe/
    Main.cs
  Radio/
    [Radio source files]
    README.md
  DVDLogo/
    Main.cs
    DVDLogo.ini
    dvd-logo.png
```

`gdeps/` is gloader's runtime/support directory. The self-contained .NET 10 host files, the private Terraria x64 runtime, the x64 runtime builder, and gloader's client/server/build logs live there.

```text
gdeps/
  [gloader dependency/support files]
  tools/
    x64-runtime/
  x64-runtime/
    TerrariaRelease.dll
    Libraries/
  logs/
    gloader-client.log
    gloader-server.log
    x64-runtime-build.log
```

Everything belonging specifically to a mod stays inside that mod's folder: `.cs` source, `.ini`/other configuration, images, data files, and any mod-specific documentation. All `.cs` files beneath one mod folder are compiled together as one in-memory assembly.

Disable one mod by renaming its folder:

```text
Thing/ -> Thing.disabled/
```

A source file inside a mod may also be individually disabled with the `.disabled.cs` suffix, although the normal unit of organization is the whole mod folder.

There is no manifest, custom scripting language, required base class, or gloader-specific inheritance tree. Mods are normal C# and may use Terraria types, reflection, unsafe code, Harmony, and managed dependencies available to the loader.

Each compile receives:

```text
GLOADER
GLOADER_CLIENT   // compiling for a client-mode run
GLOADER_SERVER   // compiling for a dedicated/Host & Play server-mode run
```

Optional one-time initialization is:

```csharp
public static class Mod
{
    public static void Load()
    {
        // setup
    }
}
```

Harmony attributes are discovered and applied automatically.

## Launcher GUI

Run `gloader.exe` with no arguments to open the native Windows launcher. The launcher is built into the same public `gloader.exe`; there is no second launcher/helper executable to launch manually.

The launcher is intentionally just a friendly front end for the existing filesystem contract:

- checking or unchecking a mod renames `Thing/` <-> `Thing.disabled/` immediately;
- **Configure** appears when a mod has one or more top-level `.ini` files; one file opens directly and multiple files are offered in a small menu;
- **Mods Folder** opens `gmods/` and **Refresh** rescans it;
- **Build x64 Runtime** appears when the private Terraria CoreCLR/FNA runtime has not been generated yet;
- while that first-run build is running, the normal launch buttons remain disabled and the full build output is saved to `gdeps/logs/x64-runtime-build.log`;
- **Logs** can open the newest game log, the client log, the server log, the x64 runtime build log, or the logs folder;
- **Show console** is a one-run debug option and is not persisted;
- **Launch Vanilla** launches the 64-bit Terraria runtime with gloader mods disabled for that run without changing the checkboxes;
- **Launch Terraria** starts the 64-bit Terraria runtime normally with the checked mods.

If both `Thing/` and `Thing.disabled/` exist at the same time, the launcher shows a conflict and refuses to rename either one instead of guessing which folder should win.

To bypass the GUI and launch directly with the current mod state after the private x64 runtime exists:

```powershell
.\gloader.exe --run
```

## Private 64-bit Terraria runtime

Stock Terraria 1.4.5.8 is a legacy 32-bit .NET Framework/XNA process. Merely changing its PE architecture flag does not remove that runtime/native dependency chain, so gloader uses the same class of platform conversion that modern tModLoader uses: modern CoreCLR plus FNA and 64-bit native libraries.

The included builder pins `gold-meridian/terraria-unified` tag `v0.3.3` at commit:

```text
f98c9a42a59c15022cea3f6ad3750d1f85578f61
```

That workspace targets Terraria 1.4.5.8. gloader deliberately stops after these upstream stages:

```text
vanilla decompile
    -> patch terraria
    -> patch netcore
    -> build
```

It does **not** apply Terraria Unified's later `patch unified` gameplay/QoL stage and it does **not** apply tModLoader's mod-loader patches. The result is the vanilla Terraria codebase with the modern .NET/FNA platform port, which gloader then hosts and patches itself.

On first run, click **Build x64 Runtime** in the launcher. The builder uses the owned `Terraria.exe` beside gloader, downloads and caches its own pinned portable .NET 10 SDK and MinGit under `%LOCALAPPDATA%\gloader\toolchain`, retrieves the matching official 1.4.5.8 dedicated-server executable from `terraria.org` when the local install does not contain it, generates the patched source in a workspace under `%LOCALAPPDATA%`, builds Release, and installs the private result under:

```text
gdeps/x64-runtime/
```

The important managed target is:

```text
gdeps/x64-runtime/TerrariaRelease.dll
```

The builder records the source Terraria SHA-256 and exact upstream revision in:

```text
gdeps/x64-runtime/gloader-x64-runtime.json
```

The original Steam `Terraria.exe` is not overwritten.

The same operation can be invoked manually if needed:

```powershell
powershell -ExecutionPolicy Bypass -File .\gdeps\tools\x64-runtime\Build-X64Runtime.ps1 -TerrariaDirectory .
```

The one-time private runtime build requires internet access, but it does **not** require Git or the .NET 10 SDK to be installed on the machine. gloader downloads exact portable copies of .NET SDK 10.0.400 and MinGit 2.55.0.windows.5 from their official upstream sources, verifies their published hashes, and reuses the cached copies on later builds. Nothing is installed system-wide and no persistent PATH changes are made. See `tools/x64-runtime/README.md` for the builder details.

## Host & Play server support

Run the visible client through gloader normally from the Terraria folder:

```powershell
.\gloader.exe
```

Terraria's Host & Play code still asks to start `TerrariaServer.exe`. gloader intercepts that launch and routes it through another x64 `gloader.exe` process using the same private `TerrariaRelease.dll`, the same `gmods` folder, and Terraria's `-server` mode. The original server arguments, working directory, Steam environment, and process relationship are preserved.

That means the modern Terraria build itself is used for both client and dedicated-server execution; a second rebuilt server executable is not required. Server-authoritative gloader mods can therefore work for Host & Play without rewriting the original Terraria executables and without requiring joining players to install the server-side mod.

Dedicated server:

```powershell
.\gloader.exe --server
```

Explicit modern Terraria target:

```powershell
.\gloader.exe --target "C:\Games\Terraria\gdeps\x64-runtime\TerrariaRelease.dll"
```

Explicit mods folder override:

```powershell
.\gloader.exe --mods "C:\Games\Terraria\gmods"
```

Disable all mods for one run:

```powershell
.\gloader.exe --no-mods
```

Arguments after `--` are passed to Terraria's entry point.

Client and server logs are separate:

```text
gdeps/logs/gloader-client.log
gdeps/logs/gloader-server.log
```

## Included mods

### Infinite Angler

`gmods/InfiniteAngler/Main.cs` is a server-authoritative shared endless Angler quest mod for multiplayer. Joining clients can remain completely vanilla. Vanilla's dawn quest rollover is suppressed, so the current quest stays active until every required, fully connected player has completed it. Each finisher is kept locked out of repeating that same quest. When the whole required group is finished, the server performs one normal Angler quest swap and broadcasts the next quest immediately. Players who join count immediately; players who disconnect stop counting.

Participation commands are enabled by default:

```ini
# gmods/InfiniteAngler/InfiniteAngler.ini
EnableParticipationCommands=true
```

Completely vanilla clients can control whether they are required for the shared-round quorum with normal chat commands:

- `!fish out` — stay connected and keep full Angler functionality, but stop blocking the next shared quest.
- `!fish in` — count toward the current shared quest again.
- `!fish` or `!fish status` — privately show your IN/OUT state plus who is waiting, finished, or opted out.

Set `EnableParticipationCommands=false` if you want the original strict behavior where every connected player is always required and `!fish` messages are left as ordinary chat.

Participation and completion are separate states. An OUT player may still catch and turn in the current quest fish, receives normal rewards, and is marked finished so they cannot repeat that quest. Their completion simply is not required for the round to advance. If they use `!fish in` later during the same round, a completion earned while OUT is preserved.

OUT state lasts across quest swaps for that connection. Disconnecting clears it, so a reconnect starts IN. If everybody opts OUT, the current quest is parked; an empty required group never causes automatic repeated quest swaps.

### No Liquid Dupe

`gmods/NoLiquidDupe/Main.cs` is a server-authoritative fix for the regular-bucket water/lava/honey duplication loop. It keeps the liquid volume conserved for partial regular-bucket scoops while leaving full scoops, Bottomless Buckets, pumps, and normal liquid simulation alone. Joining clients can remain vanilla.

### Radio

`gmods/Radio/` is the general-purpose client-side internet-radio mod. Its browser lives in Terraria's pause/options UI; there are no radio items, tiles, NPCs, accessories, furniture objects, or other in-world mechanics.

Radio deliberately has exactly two content sources:

- **RadioMonster.fm** — 10 built-in public channels using the provider's official streams, preferring 320 kbps MP3 with official lower-bitrate fallbacks;
- **Rainwave** — all 6 official stations using Rainwave's tune-in playlists.

The UI is intentionally small: RadioMonster.fm, Rainwave, and Favorites, plus playback, volume, station favorites, a persistent now-playing strip, and optional song-change popups. There is no Radio Browser, live-directory discovery, provider scraping, custom-station file, taxonomy/search layer, or third-party station augmentation.

Metadata follows the audio path where possible. RadioMonster uses ICY metadata embedded in its stream. Rainwave prefers usable ICY metadata from the actual MP3 stream; if stream metadata is unavailable, Radio falls back to Rainwave's anonymous schedule API and delays a changed schedule title by 10 seconds so metadata does not jump ahead of buffered playback.

Existing saved favorites/recents from removed providers are pruned automatically while normal playback, volume, favorites, and popup settings are preserved. The old VGMRadio Rainwave selection and now-playing preference can still migrate on first run.

See `gmods/Radio/README.md` for the exact station list, stream-quality policy, metadata behavior, persistence rules, and live-smoke coverage.

**VGMRadio is retired and is no longer shipped as a separate mod.** If an older install still has `gmods/VGMRadio/`, the new Radio mod can read its `VGMRadio.ini` on first migration. While Radio is installed, gloader deliberately ignores that leftover legacy source folder so an overlay upgrade cannot start two radio clients. The old folder can be deleted after migration.

### DVD Logo

`gmods/DVDLogo/` is client-only. It loads `dvd-logo.png` directly at runtime, bounces it around the screen, and changes to a different bright color on each wall hit.

Its size setting lives beside the mod:

```ini
# gmods/DVDLogo/DVDLogo.ini
Width=192
```

`Width` is the rendered width in pixels; height keeps the PNG's aspect ratio. With the current 2:1 logo, the default renders at 192x96.

## Updates

Source mods are still compiled on every launch against the exact private Terraria assembly gloader is about to execute, so there is no separately versioned precompiled mod DLL layer.

The platform port itself is version-specific. The current x64 builder is pinned to Terraria 1.4.5.8 and its audited TerrariaNetCore patch set. When Re-Logic ships a new Terraria build, the x64 runtime baseline must be updated/audited before gloader should target that new version. Individual mods may also need source edits if game methods or fields they patch change.

## Build

Requirements:

- Windows
- .NET 10 SDK

From the `gloader` source folder:

```powershell
.\build.ps1
```

Output staging folder:

```text
dist/gloader/
```

Copy the **contents** of `dist/gloader/` directly into the Terraria installation folder. The package adds exactly one root executable, `gloader.exe`, plus two sibling folders: `gmods/` for mods and `gdeps/` for the loader/CoreCLR runtime, private Terraria runtime builder, and support files.

Raw source mods execute with the same privileges as Terraria. Only use code you trust.

## License and third-party software

Original code and assets in this repository are licensed to the public under the PolyForm Noncommercial License 1.0.0 unless a file says otherwise; see `LICENSE.md`.

**Re-Logic, Inc. is separately granted broad commercial and proprietary-use rights to the original contributions covered by `RELOGIC-LICENSE.md`.** That special grant permits use, modification, incorporation, sublicensing, relicensing, and commercialization without payment, attribution, source disclosure, or further permission.

Third-party components bundled with gloader or Gelatin remain under their own licenses, which are collected in `THIRD-PARTY-NOTICES.txt` and are not replaced by either the PolyForm terms or the Re-Logic special grant.
