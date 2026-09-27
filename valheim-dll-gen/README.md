# Valheim DLL Generator

This folder documents the GitHub-side backend for the browser-based Valheim mod compiler.

The compiler is implemented by:

- `.github/workflows/valheim-dll-gen.yml`
- the Pages frontend at `valheim/dll-gen/`
- a small external relay endpoint that will be added/configured separately

## Repository secret

Create this repository Actions secret before connecting the endpoint:

```text
VALHEIM_DLL_GEN_PASSWORD
```

Its value is the password the web form must submit.

The secret never goes into GitHub Pages JavaScript. The workflow receives the submitted password in a dispatch payload and compares it against the secret inside the Actions runner.

## Dispatch contract

The relay should create a GitHub `repository_dispatch` event with:

```text
event_type: valheim-dll-gen
```

and this `client_payload` shape:

```json
{
  "job_id": "32-lowercase-hex-or-similar",
  "mod_name": "ForceCoolThing",
  "password": "the text entered by the user",
  "source_b64": "UTF-8 C# source encoded as Base64"
}
```

Constraints enforced by the workflow:

- `job_id`: 8-64 lowercase letters, digits, `_`, or `-`
- `mod_name`: 1-64 ordinary letters/digits/spaces/dots/underscores/hyphens, beginning with a letter
- decoded source: valid UTF-8, maximum 40 KiB
- the caller cannot submit a project file, build script, package list, or workflow

## Build model

The workflow creates its own fixed `net472` project and references:

- BepInEx from the normal Valheim BepInEx pack
- Harmony
- every DLL from the repository's pinned current Valheim Managed snapshot

Only the submitted C# file is compiled.

The submitted source is never committed to the repository.

## Public result protocol

A request with job ID `abc12345` publishes:

```text
valheim/dll-gen/builds/abc12345/result.json
```

Possible states:

```text
success
failed
unauthorized
```

Successful jobs also publish the compiled DLL. Failed jobs publish `build.txt` containing compiler output.

The Pages frontend can poll:

```text
https://polskiftw.github.io/gpages/valheim/dll-gen/builds/<job_id>/result.json
```

until it exists.

## Self-test

Pushes that modify this folder or its workflow run a local smoke test instead of expecting an external request. The smoke test compiles a tiny BepInEx/Harmony plugin that directly references Valheim's `Player` type.

That gives the compiler scaffold a real CI check even before the relay endpoint exists.
