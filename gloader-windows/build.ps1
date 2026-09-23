$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "src\GLoader\GLoader.csproj"
$DistRoot = Join-Path $Root "dist"
$Dist = Join-Path $DistRoot "gloader"
$Publish = Join-Path $DistRoot "publish"
$Deps = Join-Path $Dist "gdeps"
$ModsOut = Join-Path $Dist "gmods"
$Mods = Join-Path $Root "gmods"
$ExpandedWorlds = Join-Path $Mods "ExpandedWorlds"
$RuntimeBuilder = Join-Path $Root "tools\x64-runtime"
$RuntimeBuilderOut = Join-Path $Deps "tools\x64-runtime"

function Set-AppHostManagedPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AppHostPath,

        [Parameter(Mandatory = $true)]
        [string]$OldManagedPath,

        [Parameter(Mandatory = $true)]
        [string]$NewManagedPath
    )

    $bytes = [System.IO.File]::ReadAllBytes($AppHostPath)
    $oldBytes = [System.Text.Encoding]::UTF8.GetBytes($OldManagedPath)
    $newBytes = [System.Text.Encoding]::UTF8.GetBytes($NewManagedPath)

    if ($newBytes.Length -ge 1024) {
        throw "Apphost managed path exceeds the .NET apphost 1024-byte path buffer."
    }

    $matches = New-Object System.Collections.Generic.List[int]
    $maxStart = $bytes.Length - $newBytes.Length - 1

    for ($index = 0; $index -le $maxStart; $index++) {
        $same = $true
        for ($offset = 0; $offset -lt $oldBytes.Length; $offset++) {
            if ($bytes[$index + $offset] -ne $oldBytes[$offset]) {
                $same = $false
                break
            }
        }

        if (-not $same) {
            continue
        }

        # AppBinaryName lives at the start of a zero-filled 1024-byte buffer in
        # the .NET apphost. Verify enough zero padding exists before expanding it.
        $hasRoom = $true
        for ($offset = $oldBytes.Length; $offset -le $newBytes.Length; $offset++) {
            if ($bytes[$index + $offset] -ne 0) {
                $hasRoom = $false
                break
            }
        }

        if ($hasRoom) {
            $matches.Add($index)
        }
    }

    if ($matches.Count -ne 1) {
        throw "Expected exactly one patchable apphost managed-path slot for '$OldManagedPath'; found $($matches.Count)."
    }

    $start = $matches[0]
    for ($offset = 0; $offset -lt $newBytes.Length; $offset++) {
        $bytes[$start + $offset] = $newBytes[$offset]
    }
    $bytes[$start + $newBytes.Length] = 0

    [System.IO.File]::WriteAllBytes($AppHostPath, $bytes)
    Write-Host "Patched public apphost: $OldManagedPath -> $NewManagedPath"
}

if (Test-Path $DistRoot) {
    Remove-Item $DistRoot -Recurse -Force
}

if (-not (Test-Path (Join-Path $ExpandedWorlds "Main.cs") -PathType Leaf)) {
    throw "Expanded Worlds is missing from gmods/ExpandedWorlds."
}

New-Item $Dist -ItemType Directory -Force | Out-Null
New-Item $Deps -ItemType Directory -Force | Out-Null
New-Item $ModsOut -ItemType Directory -Force | Out-Null
New-Item $RuntimeBuilderOut -ItemType Directory -Force | Out-Null

dotnet publish $Project -c Release -o $Publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$PublishedAppHost = Join-Path $Publish "gloader.exe"
$PublicAppHost = Join-Path $Dist "gloader.exe"
if (-not (Test-Path $PublishedAppHost -PathType Leaf)) {
    throw "Publish did not produce the expected gloader.exe apphost."
}

Copy-Item $PublishedAppHost $PublicAppHost -Force
Set-AppHostManagedPath `
    -AppHostPath $PublicAppHost `
    -OldManagedPath "gloader.dll" `
    -NewManagedPath "gdeps\gloader.dll"
Remove-Item $PublishedAppHost -Force

Get-ChildItem $Publish -Force | Move-Item -Destination $Deps -Force
Copy-Item $ExpandedWorlds $ModsOut -Recurse -Force
Copy-Item (Join-Path $RuntimeBuilder "*") $RuntimeBuilderOut -Recurse -Force
Copy-Item (Join-Path $Root "LICENSE.md") (Join-Path $Deps "LICENSE.md") -Force
Copy-Item (Join-Path $Root "THIRD-PARTY-NOTICES.txt") (Join-Path $Deps "THIRD-PARTY-NOTICES.txt") -Force
Remove-Item $Publish -Recurse -Force

foreach ($required in @("gloader.dll", "gloader.runtimeconfig.json", "gloader.deps.json", "coreclr.dll", "clrjit.dll")) {
    if (-not (Test-Path (Join-Path $Deps $required) -PathType Leaf)) {
        throw "Published x64 CoreCLR layout is missing required gdeps file '$required'."
    }
}

$bundledMods = @(Get-ChildItem $ModsOut -Directory | Select-Object -ExpandProperty Name | Sort-Object)
if (($bundledMods -join ',') -ne "ExpandedWorlds") {
    throw "Release package must contain only ExpandedWorlds under gmods. Found: $($bundledMods -join ', ')"
}

Write-Host ""
Write-Host "Built: $Dist"
Write-Host "Copy the contents of that folder directly into the Terraria installation folder."
Write-Host "gloader.exe is the only root file; its managed host and x64 CoreCLR runtime live under gdeps."
Write-Host "Expanded Worlds is the only bundled gmod. The required private Terraria x64 runtime builder is under gdeps\tools\x64-runtime."
