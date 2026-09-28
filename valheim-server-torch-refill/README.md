# Server Torch Refill

A true server-side Valheim BepInEx add-on for [ServersideQoL](https://thunderstore.io/c/valheim/p/ArgusMagnus/ServersideQoL/).

It consumes the real fuel item accepted by each refillable `Fireplace` and moves fuel from nearby storage into lights without requiring any client mod.

## Defaults

- Range: 10 m
- Refill at or below: 25%
- Refill toward: 100%
- Leave in source container: 0
- Check interval: 30 s, plus immediate wakeups when nearby container contents change
- Lighting only by default; campfires/hearths/bonfires are excluded unless `IncludeAllFireplaces` is enabled
- Chests currently open by a player are never modified
- Fuel is never hardcoded; `Fireplace.m_fuelItem` is authoritative

## Runtime dependency

- BepInEx 5
- ServersideQoL 2.1.1 or newer
- The dependencies required by ServersideQoL itself (currently including ValheimModding YamlDotNet)

Install only on the dedicated server. Vanilla/console clients do not need this DLL.
