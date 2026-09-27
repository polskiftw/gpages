# Assembly Inspector

Current plugin version: **1.2.0**.

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

Version 1.1.1 routes generated typed `DynamicMethod` postfixes through Harmony's patch-factory mechanism instead of registering a dynamic method directly. CI smoke-tests the factory contract and a generated bool postfix that changes `false` to `true`.

## Generate standalone override mods

For supported scalar return types, the selected member has:

- **Copy postfix mod** — complete BepInEx/Harmony source that lets the original run, then replaces the return value
- **Copy hard override mod** — complete source that sets the return value and skips the original method entirely

Version 1.2.0 also adds **Copy live bundle (N)** to the top bar. You can force several methods/properties live at once, then copy one complete postfix mod containing every active override and its current forced value.

Example:

```text
ThingA.IsAllowed() = true
ThingB.CanBuild() = true
ThingC.MaxCount() = 99

Copy live bundle (3)
```

The generated bundle uses one shared reflection resolver plus one small typed postfix per override, keeping the source compact enough for the repository's browser DLL Generator workflow.

Generated code finds each target class and exact overloaded method by reflection, so the target Valheim class itself does not need to be public.

This makes the inspector useful as a mod-discovery workbench: experiment with a whole combination in game, then export that combination as one standalone mod once you like the behavior.

## Assembly selection

The default assembly is:

```text
assembly_valheim
```

The assembly name field is editable if you want to inspect another already-loaded managed assembly.

## Safety / scope

Assembly Inspector is deliberately focused on **return-value inspection and override**. It does not provide arbitrary method invocation or arbitrary memory/field editing.

Live return forcing is limited to scalar values that can be converted safely in the inspector UI.

## Build

The dedicated `.github/workflows/build-valheim-assembly-inspector.yml` workflow builds this project against the repository's pinned current Valheim Unity managed assemblies. CI also creates a live bundle from a test override and compiles the generated bundle source as a standalone `net472` mod before publishing:

```text
valheim/assembly-inspector/AssemblyInspector.dll
```

Install the DLL into:

```text
Valheim/BepInEx/plugins/
```

Then enter a world and press **F9**.
