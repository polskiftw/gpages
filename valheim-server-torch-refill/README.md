# Server Torch Refill v1.2.0

Server-only Valheim torch fuel refilling using the [ServersideQoL](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL/) framework. A chest must contain the correct fuel item. The server transfers real items into torches; it does not create fuel for free.

## Installation and exactly where the config is

1. Install BepInEx 5, ServersideQoL **2.3.1 or newer**, and ServersideQoL's required dependencies on the **server**.
2. Replace the old server `ServerTorchRefill.dll` in **`<Valheim dedicated server root>/BepInEx/plugins/`**.
3. Restart the server; wait for **`[Server Torch Refill v1.2.0] RUNNING`** in the server console or `BepInEx/LogOutput.log`. The startup warning now prints the **exact path** to the active configuration file.
4. By default this is **`<Valheim dedicated server root>/BepInEx/config/claire.valheim.servertorchrefill.cfg`**. All settings are in the **`[General]`** section. The file is generated when the plugin initializes. **Do not edit your local Valheim client's config** if the mod is on a separate dedicated server.
5. If ServersideQoL uses `UnifiedConfig = true`, the settings live in the framework config under `[M.General]`. If `ConfigPerWorld = true`, the config lives in the world's save folder instead. The printed startup path always takes precedence.

## Defaults (you do not need to edit anything)

- `RefillWheneverMissing = true`: **new**, refill as soon as fuel capacity has room. This is true even if your old config still contains `RefillBelowPercent = 0.25`.
- `Range = 10`: fuel source chest must be within ten metres.
- `RefillToPercent = 1`: fill the light to capacity.
- `CheckIntervalSeconds = 30`: minimum dark-light retry interval. Full lights sleep at least 180 seconds; partially-lit blocked lights at least 60 seconds.
- `LeaveFuelInContainer = 0`: use stored fuel when available.
- `LogRefills = true`, `AnnounceInGame = true`: default server console logs plus sparse vanilla in-game messages.
- `DiagnosticLogs` is **NOT required** to see the new warning/status information; basic diagnostics are always on.
- `IncludeAllFireplaces = false`: refillable torches, sconces, lanterns, braziers, and jack-o-turnips only. Campfires, hearths, bonfires are excluded unless enabled.

## Quick test

1. Put a regular ground torch `piece_groundtorch` close to a **normal, non-private** chest and place **resin** inside. Blue ground torches use greydwarf eyes, green ones use guck, braziers may need coal or greydwarf eyes. Different lights use their real `Fireplace.m_fuelItem`.
2. Close the chest and join the world. New ground torches start at roughly 2/6 fuel, so v1.2 should top up without needing to wait for it to go out.
3. Look for `[Server Torch Refill] REFILLED` in server console or LogOutput.log and the chest's fuel amount decreasing.
4. If nothing happens, watch for **`NOT REFILLED`** warnings, which tell you whether there is no eligible chest, no correct fuel, the chest is open, or ownership is pending. Warnings are capped to prevent console spam.
5. Every two minutes of eligible processing, the server prints a **workload summary**: counts of checks, chests seen, inventory reads, refills, failures, and time spent scanning. If you see **no startup line at all**, the DLL was not initialized and the problem occurs before our refill code can run.

The original `RefillBelowPercent` option still works as an advanced mode if you set `RefillWheneverMissing = false`. This is no longer the default.

## Performance changes since v1.1

- Replaced global **per-item chest inventory indexing** with a light **spatial chest index**. Only inventories inside the ten-metre radius of a torch that currently needs fuel are read.
- Removed the container-changed callback that indiscriminately rescheduled nearby lights.
- Fixed a tight client-owned chest loop: older builds accepted ServersideQoL's 0.1s ownership retry as the *whole torch's* polling interval. Now all ownership retries use a minimum 30s by default.
- Removed sorting/LINQ over every candidate chest inventory, retaining a single pass.
- Full lights wait at least 180 seconds between inexpensive checks, while changes to ZDOs can wake processing through ServersideQoL.
- Bounded default console warnings and in-game notices (at most one globally every 90 seconds).

## Limitations

The mod does not make fuel out of thin air, does not access private/personal chests, and does not refill vanilla resin candles because their `m_canRefill` is false. It cannot make an obstructed/wet/switched-off fireplace burn merely by adding fuel. A dedicated server never instantiates all fires; the server updates authoritative ZDO fuel and the client renders the result. Vanilla and console clients do **not** need the DLL.

## Developer tests

The .NET fuel policy tests can run without the game:

`dotnet run --project valheim-server-torch-refill/tests/FuelPolicy.Tests.csproj -c Release`

GitHub Actions compiles the real plugin against Valheim managed binaries from the private draft release and the pinned ServersideQoL 2.3.1 dependency. An actual in-world test is still required to verify end-to-end operation.
