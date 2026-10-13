# Server Torch Refill

A true server-side Valheim BepInEx add-on for [ServersideQoL](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL/).

It consumes the real fuel item accepted by each refillable `Fireplace` and moves fuel from nearby storage into lights without requiring any client mod.

## Defaults

- Range: 10 m
- Refill at or below: 25%
- Refill toward: 100%
- Leave in source container: 0
- Check interval: 30 s, plus immediate wakeups when nearby container contents change
- Log successful fuel transfers: yes (one server log entry per transfer)
- Diagnostic logs for skipped refills: off unless enabled in the mod config
- Lighting only by default; campfires/hearths/bonfires are excluded unless `IncludeAllFireplaces` is enabled
- Chests currently open by a player are never modified
- Fuel is never hardcoded; `Fireplace.m_fuelItem` is authoritative

## Runtime dependency

- BepInEx 5
- ServersideQoL 2.3.1 or newer (compiled against 2.3.1)
- The dependencies required by ServersideQoL itself (currently including ValheimModding YamlDotNet)

Install only on the dedicated server. Vanilla/console clients do not need this DLL.

## Checking that refills work

The default threshold is **25%**, not "keep topped up." For example, a new resin
`piece_groundtorch` starts at **2/6 = 33%**, so **it will not immediately refill**.
Put resin in a closed, non-private storage chest within 10 m, then use an empty
(or nearly empty) ground torch. The default 30-second check is a fallback;
chest changes can trigger an earlier check.

For a quick test, temporarily set `RefillBelowPercent = 1` in
`BepInEx/config/claire.valheim.servertorchrefill.cfg` and leave room in the
torch for fuel. A successful transfer appears in the server's BepInEx log.
Set `DiagnosticLogs = true` in the same config to see a one-time explanation
for each light that was skipped, including missing fuel or container ownership.
Settings may instead be in ServersideQoL's unified/per-world config if enabled.

The mod only runs on the **server/host**, requires ServersideQoL's **core** plus
its own dependencies, and does not require Jötunn or any client-side plugin.
Candle_resin is **not** refillable in vanilla Valheim (`m_canRefill = false`),
so the mod intentionally ignores it even though its name contains "candle".

## Fixes in v1.1.0

- Rebuilt against ServersideQoL 2.3.1 and the current privately supplied
  Valheim managed-assembly snapshot, instead of the old 2.1.1 dependency.
- Account for fractional remaining fuel with a whole-item refill: a torch with
  capacity 1 and fuel 0.2 now receives its one fuel item instead of being
  skipped by truncation. Refill is clamped to the configured target.
- Added startup, success, and optional skip diagnostics for easier testing.
- Added deterministic fuel arithmetic checks to the GitHub Actions build.

## Testing

`dotnet run --project tests/FuelPolicy.Tests.csproj -c Release` tests the
fuel arithmetic without needing the game installed. The CI workflow builds
the plugin against the managed Valheim snapshot and latest pinned SQoL DLL;
only an actual hosted Valheim session can verify in-game transfer behavior.
