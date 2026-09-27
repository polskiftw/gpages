# Assembly Inspector

Current plugin version: **1.0.0**.

A BepInEx 5 + Harmony Valheim development/debug tool for browsing the live `assembly_valheim` managed assembly and forcing selected scalar method/property return values.

## What it does

Press **F9** in game to open a three-column browser:

1. classes from `assembly_valheim`
2. methods / properties / fields on the selected class
3. exact member details and return-override tools

The browser shows:

- class and base-class names
- method signatures
- parameter names/types
- exact return types
- visibility, static/virtual/abstract/generic metadata
- property types and their getter methods
- field types and static field values
- filtering by class name, member name, and return type
- optional inherited members
- optional property getter/setter methods in the method list
- copyable C#-style signatures
- copyable reflection targeting information

## Live return overrides

For scalar return types (bool, strings, chars, enums, decimal, and primitive numeric types), Assembly Inspector can apply a live Harmony **postfix**.

Example:

```text
CoolThing.IsAllowed() -> bool
```

Selecting it exposes:

```text
Force TRUE live
Force FALSE live
```

The original Valheim method still runs normally. After it returns, Assembly Inspector replaces the value the caller receives.

That is deliberately the default because it preserves side effects in the original method.

Every live override can be removed individually, and **Clear all live overrides** removes all patches owned by Assembly Inspector.

## Generate a standalone override mod

For supported scalar return types, the selected member also has:

- **Copy postfix mod** — complete BepInEx/Harmony source that lets the original run, then replaces the return value
- **Copy hard override mod** — complete source that sets the return value and skips the original method entirely

Generated code finds the target class and exact overloaded method by reflection, so it does not require the target Valheim class to be public.

This makes the inspector useful as a mod-discovery workbench: find the method in game, force it experimentally, then copy a dedicated standalone mod once you know you want the behavior permanently.

## Assembly selection

The default assembly is:

```text
assembly_valheim
```

The assembly name field is editable if you want to inspect another already-loaded managed assembly.

## Safety / scope

Version 1.0 is deliberately focused on **return-value inspection and override**. It does not provide arbitrary method invocation or arbitrary memory/field editing.

Live return forcing is limited to scalar values that can be converted safely in the inspector UI.

## Build

The dedicated `.github/workflows/build-valheim-assembly-inspector.yml` workflow builds this project against the repository's pinned current Valheim Unity managed assemblies and publishes:

```text
valheim/assembly-inspector/AssemblyInspector.dll
```

Install the DLL into:

```text
Valheim/BepInEx/plugins/
```

Then enter a world and press **F9**.
