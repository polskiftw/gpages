# Assembly Inspector

Current plugin version: **1.5.0**.

A BepInEx 5 + Harmony Valheim development/debug tool for browsing the live `assembly_valheim` managed assembly, forcing scalar return/argument values, and applying targeted scalar field mutations around selected methods.

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
- assembly-wide global member search without selecting a class first
- optional inherited members
- optional property getter/setter methods in the method list
- copyable C#-style signatures
- copyable reflection targeting information
- live scalar argument overrides for selected method parameters
- live prefix/postfix field mutations using `this.field` or `argument.field` targets

## Assembly-wide member search

Version 1.3.0 added a **Global search** box above the browser. It searches declared methods, properties, and fields across every loaded class at once, so you do not need to know or select the owning class first.

Version 1.5.0 makes that search substantially lighter on the game thread. Search results are cached instead of rescanning all indexed members on every IMGUI layout/repaint pass, input is debounced for 120 ms while you type, and the 1,000+ class-button list is paused while a global search is active. The result cap is 200 buttons, with the full match count still shown so broad searches do not create hundreds of unnecessary GUI controls every frame.

Plain search terms are ANDed together and can match:

- class names / full type names
- method, property, and field names
- method signatures
- return/property/field types
- method and indexer parameter names/types

Useful filters can be mixed with ordinary terms:

```text
method:CanBuild type:bool
field:stamina class:Player
property:health
param:ItemDrop method:
class:Inventory name:Add
```

Supported filter prefixes are `method:`, `property:`, `field:`, `class:`, `name:`, `type:` (or `return:`), `param:` (or `parameter:`), and `kind:`.

Selecting a global result immediately selects its owning class and opens the normal detail/edit panel. Property accessor methods are omitted from the global *method* index to avoid duplicate results because the property itself is indexed directly; the existing per-class **Show getters/setters** toggle still exposes them when needed.

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

Every live override can be removed individually, and **Clear all live patches** removes all patches owned by Assembly Inspector.

Version 1.1.1 routes generated typed `DynamicMethod` postfixes through Harmony's patch-factory mechanism instead of registering a dynamic method directly. CI smoke-tests the factory contract and a generated bool postfix that changes `false` to `true`.

## Argument overrides

Version 1.4.0 adds scalar argument overrides. For each supported parameter on the selected method, enter the forced value and choose **Apply live**. Assembly Inspector installs a Harmony prefix and replaces that argument in `__args` before the original Valheim method receives it.

This is useful for signatures such as:

```text
ItemDrop.OnCreateNew(ItemDrop, bool cheated)
CharacterDrop.DropItems(..., bool cheated)
```

For example, forcing the `cheated` argument to `false` changes the incoming flag without skipping the original method.

**Copy argument mod** produces a standalone generated BepInEx/Harmony patch for that exact overloaded method and argument index.

## Field mutation patches

Version 1.4.0 added targeted scalar field mutation patches tied to a selected method. Enter a one-level target path:

```text
this.m_cheated
item.m_cheated
arg0.m_cheated
```

- `this.field` targets the selected method's instance.
- `parameterName.field` targets an object passed to that method.
- `argN.field` is an index-based fallback when parameter names are inconvenient.
- Static fields are detected automatically after the path resolves.

Choose whether the mutation runs as a **prefix** (before the original method) or **postfix** (after it). Prefix is useful for sanitizing an object before save/consumption; postfix is useful for clearing a field after a load/create method sets it.

Only writable scalar fields are synthesized automatically: primitive values, strings, chars, enums, and decimals. Readonly/constants and instance fields on value-type arguments are rejected instead of generating a patch that would silently fail.

**Copy field-mutation mod** produces a standalone generated patch using reflection for the field write, so private fields and private declaring types do not need to be referenced directly in generated source.

Version 1.5.0 also makes the **field result itself editable from the Fields detail panel**. Select a writable scalar field such as `ItemData.m_cheated`, enter the value, choose prefix/postfix timing, then search/select a hook method on that class. From the field panel you can **Apply live**, **Copy field-mutation mod**, or **Open hook method** with the target prefilled. Instance fields only offer instance hook methods because each object owns its own copy of the field.

Static fields can use the explicit `static.fieldName` target form in the method-side editor.


## Generate standalone override mods

For supported scalar return types, the selected member has:

- **Copy postfix mod** — complete BepInEx/Harmony source that lets the original run, then replaces the return value
- **Copy hard override mod** — complete source that sets the return value and skips the original method entirely

Version 1.2.0 added **Copy live bundle (N)** to the top bar. In 1.4.0 that bundle now includes every active return override, argument override, and field mutation, not just postfix return overrides.

Example:

```text
ThingA.IsAllowed() = true
ThingB.CanBuild() = true
ThingC.MaxCount() = 99

Copy live bundle (3)
```

The generated bundle uses one shared reflection resolver plus compact Harmony prefix/postfix methods for the active patches, keeping the source suitable for the repository's browser DLL Generator workflow.

Version 1.2.1 gives each bundle a deterministic content-derived identity. The sorted patch kind, exact target method/parameter identities, field target/timing where applicable, and forced values are hashed with SHA-256; the first 16 lowercase hex characters become the bundle suffix.

For example:

```text
PluginGuid: claire.valheim.generated.livebundle.a83f21d4c4e51290
Class:      AssemblyInspectorLiveBundle_a83f21d4c4e51290
Name:       Assembly Inspector Bundle a83f21d4c4e51290
```

The same exact bundle exports with the same suffix. Changing a target or forced value produces a different suffix, allowing multiple generated bundle DLLs to coexist without sharing one BepInEx plugin GUID.

Generated code finds each target class and exact overloaded method by reflection, so the target Valheim class itself does not need to be public.

This makes the inspector useful as a mod-discovery workbench: experiment with a whole combination in game, then export that combination as one standalone mod once you like the behavior.

## Assembly selection

The default assembly is:

```text
assembly_valheim
```

The assembly name field is editable if you want to inspect another already-loaded managed assembly.

## Safety / scope

Assembly Inspector does not provide arbitrary method invocation or unrestricted memory editing. Its mutation tools are deliberately narrow and declarative:

- return overrides are limited to supported scalar return values
- argument overrides are limited to supported scalar parameters
- field mutation patches require an explicit selected method plus a one-level `this.field` / `argument.field` target
- field writes are limited to writable scalar fields

That keeps generated patches inspectable and deterministic while covering common Harmony prefix/postfix modding patterns.

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
