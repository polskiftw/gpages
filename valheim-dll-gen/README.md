# Valheim DLL Generator

A browser-to-GitHub Valheim mod compiler.

There are only two moving parts:

1. the static Pages frontend at `valheim/dll-gen/`
2. the GitHub Actions compiler at `.github/workflows/valheim-dll-gen.yml`

There is **no relay endpoint and no build-password repository secret**.

## Browser authentication

The Pages UI asks the user for a **fine-grained GitHub personal access token**.

For this personal tool, the token should be restricted to:

- repository access: only `polskiftw/gpages`
- repository permission: **Actions — Read and write**

The browser sends that token directly to `api.github.com` to:

- verify access to the DLL Generator workflow
- call the workflow-dispatch endpoint

The token is never committed to the repository and is never sent to a third-party relay.

By default the UI stores it in `sessionStorage`. If **Remember token on this device** is selected, it is moved to `localStorage`. The UI also has a button to delete both copies.

## Workflow-dispatch contract

The page starts `valheim-dll-gen.yml` with:

```json
{
  "ref": "main",
  "inputs": {
    "job_id": "client-generated-lowercase-id",
    "mod_name": "ForceCoolThing",
    "source_b64": "UTF-8 C# source encoded as Base64"
  }
}
```

Constraints enforced by both the page and workflow:

- `job_id`: 8-64 lowercase letters, digits, `_`, or `-`
- `mod_name`: 1-64 ordinary letters/digits/spaces/dots/underscores/hyphens, beginning with a letter
- decoded source: valid UTF-8, maximum **32 KiB**
- the caller cannot submit a project file, build script, package list, workflow, or MSBuild options

The workflow reads the potentially large Base64 source from GitHub's event JSON file rather than passing it through a Windows environment variable.

## Build model

The workflow creates its own fixed `net472` project and references:

- BepInEx from the normal Valheim BepInEx pack
- Harmony
- every DLL from the repository's pinned current Valheim Managed snapshot

Only the submitted C# file is compiled.

The submitted source itself is never committed to the repository.

## Result protocol

A request with job ID `abc12345` publishes:

```text
valheim/dll-gen/builds/abc12345/result.json
```

Successful jobs also publish the compiled DLL:

```text
valheim/dll-gen/builds/abc12345/ForceCoolThing.dll
```

Failed compiles publish:

```text
valheim/dll-gen/builds/abc12345/build.txt
```

The Pages frontend polls:

```text
https://polskiftw.github.io/gpages/valheim/dll-gen/builds/<job_id>/result.json
```

until the result has been published.

Because `gpages` is public, published DLLs and compiler logs are public.

## Self-test

Pushes that modify this folder or its workflow run a smoke test instead of a user build. A manual workflow run with all inputs left blank does the same thing.

The smoke test compiles a tiny BepInEx/Harmony plugin that directly references Valheim's `Player` type. This verifies the complete compiler/reference scaffold without creating a public build-result folder.
