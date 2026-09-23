param(
    [string]$TerrariaDirectory,

    [string]$OutputDirectory,

    [string]$WorkspaceDirectory = (Join-Path $env:LOCALAPPDATA "gloader\x64-runtime-workspace"),

    [switch]$KeepGeneratedSource,

    [switch]$PrepareToolchainOnly
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Terraria Unified v0.3.3 is the tagged 1.4.5.8 workspace we audited.
# We use only its Terraria -> TerrariaNetCore patch stages. We deliberately do
# NOT apply its Unified gameplay/QoL patches or its tModLoader patches.
$UpstreamRepository = "https://github.com/gold-meridian/terraria-unified.git"
$UpstreamTag = "v0.3.3"
$UpstreamCommit = "f98c9a42a59c15022cea3f6ad3750d1f85578f61"

# The build toolchain is private to gloader and cached outside the Terraria
# directory. Nothing is installed system-wide and no PATH/registry changes are
# persisted. These exact upstream archives are verified before extraction.
$DotnetSdkVersion = "10.0.400"
$DotnetSdkUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.400/dotnet-sdk-10.0.400-win-x64.zip"
$DotnetSdkSha512 = "9b8b88590e4da131bfd0da7aa089d0fc04d5418d5f8607ec13d55dc5a17b4399afd54d496c12657fa05c6c6546dc5eab930f26ac6c50f2d3a7712c0fb378c366"
$MinGitVersion = "2.55.0.5"
$MinGitDisplayVersion = "2.55.0.windows.5"
$MinGitUrl = "https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.5/MinGit-2.55.0.5-64-bit.zip"
$MinGitSha256 = "56d7b226b7693196cfc71fef26568f536c4a021ab6c37ff2db4287bed908e96e"

# TerrariaNetCore v0.3.3 references this package directly. Stage its managed
# Windows asset and x64 Steam native library into the private runtime instead
# of assuming the upstream install target copied every NuGet runtime asset.
$SteamworksPackageId = "steamworks.net.anycpu"
$SteamworksPackageDisplayName = "Steamworks.NET.AnyCPU"
$SteamworksPackageVersion = "2025.162.4"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @(),

        [string]$WorkingDirectory
    )

    $pushedLocation = $false
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Push-Location $WorkingDirectory
            $pushedLocation = $true
        }

        Write-Host "> $FilePath $($Arguments -join ' ')"
        & $FilePath @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($pushedLocation) {
            Pop-Location
        }
    }

    if ($exitCode -ne 0) {
        throw "Command failed with exit code ${exitCode}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

function Install-VerifiedZip {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [ValidateSet("SHA256", "SHA512")]
        [string]$HashAlgorithm,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedHash,

        [Parameter(Mandatory = $true)]
        [string]$Destination,

        [Parameter(Mandatory = $true)]
        [string]$RequiredRelativePath
    )

    $requiredPath = Join-Path $Destination $RequiredRelativePath
    if (Test-Path $requiredPath -PathType Leaf) {
        return $requiredPath
    }

    $destinationParent = Split-Path -Parent $Destination
    $downloadDirectory = Join-Path $ToolchainRoot "downloads"
    New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
    New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null

    $archiveName = [System.IO.Path]::GetFileName(([Uri]$Url).AbsolutePath)
    $archivePath = Join-Path $downloadDirectory $archiveName
    $partialDirectory = "$Destination.partial-$PID"

    Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
    Remove-Item $partialDirectory -Recurse -Force -ErrorAction SilentlyContinue

    try {
        Write-Host "Downloading $Name..."
        Invoke-WebRequest -Uri $Url -OutFile $archivePath -UseBasicParsing

        $actualHash = (Get-FileHash -Algorithm $HashAlgorithm -Path $archivePath).Hash.ToLowerInvariant()
        if ($actualHash -ne $ExpectedHash.ToLowerInvariant()) {
            throw "$Name download hash mismatch. Expected $ExpectedHash, got $actualHash."
        }

        Expand-Archive -LiteralPath $archivePath -DestinationPath $partialDirectory -Force
        $partialRequiredPath = Join-Path $partialDirectory $RequiredRelativePath
        if (-not (Test-Path $partialRequiredPath -PathType Leaf)) {
            throw "$Name archive did not contain '$RequiredRelativePath'."
        }

        if (Test-Path $Destination) {
            Remove-Item $Destination -Recurse -Force
        }
        Move-Item -LiteralPath $partialDirectory -Destination $Destination

        if (-not (Test-Path $requiredPath -PathType Leaf)) {
            throw "$Name extraction did not produce '$requiredPath'."
        }

        Write-Host "$Name ready: $Destination"
        return $requiredPath
    }
    finally {
        Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
        Remove-Item $partialDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-NuGetGlobalPackagesDirectory {
    param([Parameter(Mandatory = $true)][string]$DotnetPath)

    if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        return [System.IO.Path]::GetFullPath($env:NUGET_PACKAGES)
    }

    $lines = @(& $DotnetPath nuget locals global-packages --list)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not determine the NuGet global-packages directory."
    }

    foreach ($line in $lines) {
        if ($line -match '^\s*global-packages:\s*(.+?)\s*$') {
            return [System.IO.Path]::GetFullPath($Matches[1].Trim())
        }
    }

    throw "dotnet did not report a NuGet global-packages directory."
}

function Get-DefaultOutputDirectory {
    # Installed layout: walk upward until we find the one public gloader.exe,
    # then put the private game runtime under its gdeps folder.
    $cursor = $PSScriptRoot
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path (Join-Path $cursor "gloader.exe") -PathType Leaf) {
            return (Join-Path $cursor "gdeps\x64-runtime")
        }

        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) {
            break
        }
        $cursor = $parent
    }

    # Source-tree fallback. After build.ps1 has run, prefer the package under
    # dist/gloader so testing the builder does not dirty the repository root.
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $distLoader = Join-Path $repoRoot "dist\gloader"
    if (Test-Path (Join-Path $distLoader "gloader.exe") -PathType Leaf) {
        return (Join-Path $distLoader "gdeps\x64-runtime")
    }

    return (Join-Path $repoRoot "gdeps\x64-runtime")
}

if (-not $PrepareToolchainOnly) {
    if ([string]::IsNullOrWhiteSpace($TerrariaDirectory)) {
        throw "TerrariaDirectory is required unless -PrepareToolchainOnly is used."
    }

    $TerrariaDirectory = [System.IO.Path]::GetFullPath($TerrariaDirectory)
    $TerrariaExe = Join-Path $TerrariaDirectory "Terraria.exe"
    if (-not (Test-Path $TerrariaExe -PathType Leaf)) {
        throw "Terraria.exe was not found in '$TerrariaDirectory'."
    }
}

$ToolchainRoot = if ([string]::IsNullOrWhiteSpace($env:GLOADER_TOOLCHAIN_ROOT)) {
    Join-Path $env:LOCALAPPDATA "gloader\toolchain"
}
else {
    [System.IO.Path]::GetFullPath($env:GLOADER_TOOLCHAIN_ROOT)
}
$DotnetRoot = Join-Path $ToolchainRoot "dotnet-$DotnetSdkVersion"
$MinGitRoot = Join-Path $ToolchainRoot "mingit-$MinGitVersion"

New-Item -ItemType Directory -Force -Path $ToolchainRoot | Out-Null

Write-Host "Preparing private gloader build toolchain..."
$DotnetExe = Install-VerifiedZip `
    -Name ".NET SDK $DotnetSdkVersion (win-x64)" `
    -Url $DotnetSdkUrl `
    -HashAlgorithm "SHA512" `
    -ExpectedHash $DotnetSdkSha512 `
    -Destination $DotnetRoot `
    -RequiredRelativePath "dotnet.exe"
$GitExe = Install-VerifiedZip `
    -Name "MinGit $MinGitDisplayVersion (64-bit)" `
    -Url $MinGitUrl `
    -HashAlgorithm "SHA256" `
    -ExpectedHash $MinGitSha256 `
    -Destination $MinGitRoot `
    -RequiredRelativePath "cmd\git.exe"

$env:DOTNET_ROOT = $DotnetRoot
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$privatePathParts = @(
    $DotnetRoot,
    (Join-Path $MinGitRoot "cmd"),
    (Join-Path $MinGitRoot "mingw64\bin"),
    (Join-Path $MinGitRoot "usr\bin")
)
$env:PATH = (($privatePathParts + @($env:PATH)) -join [System.IO.Path]::PathSeparator)

$dotnetVersion = (& $DotnetExe --version).Trim()
if ($LASTEXITCODE -ne 0 -or $dotnetVersion -ne $DotnetSdkVersion) {
    throw "Private .NET SDK validation failed. Expected '$DotnetSdkVersion', got '$dotnetVersion'."
}
$gitVersion = (& $GitExe --version).Trim()
if ($LASTEXITCODE -ne 0 -or $gitVersion -ne "git version $MinGitDisplayVersion") {
    throw "Private MinGit validation failed. Expected 'git version $MinGitDisplayVersion', got '$gitVersion'."
}

Write-Host "Private .NET SDK: $dotnetVersion"
Write-Host "Private Git:      $gitVersion"
Write-Host "Toolchain cache:  $ToolchainRoot"

if ($PrepareToolchainOnly) {
    Write-Host "Private gloader build toolchain is ready."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Get-DefaultOutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$WorkspaceDirectory = [System.IO.Path]::GetFullPath($WorkspaceDirectory)

Write-Host ""
Write-Host "gloader x64 Terraria runtime builder"
Write-Host "Terraria source: $TerrariaExe"
Write-Host "Output:          $OutputDirectory"
Write-Host "Workspace:       $WorkspaceDirectory"
Write-Host "Upstream:        terraria-unified $UpstreamTag @ $UpstreamCommit"
Write-Host "Patch ceiling:   TerrariaNetCore only (no Unified gameplay patches, no tML)"
Write-Host "Steamworks:      $SteamworksPackageDisplayName $SteamworksPackageVersion"
Write-Host ""

if (-not (Test-Path (Join-Path $WorkspaceDirectory ".git"))) {
    if (Test-Path $WorkspaceDirectory) {
        Remove-Item $WorkspaceDirectory -Recurse -Force
    }

    $parent = Split-Path -Parent $WorkspaceDirectory
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Invoke-Checked -FilePath $GitExe -Arguments @(
        "clone", "--recursive", "--branch", $UpstreamTag, "--depth", "1",
        $UpstreamRepository, $WorkspaceDirectory)
}
else {
    Invoke-Checked -FilePath $GitExe -Arguments @(
        "-C", $WorkspaceDirectory, "fetch", "origin", "tag", $UpstreamTag, "--depth", "1")
}

Invoke-Checked -FilePath $GitExe -Arguments @(
    "-C", $WorkspaceDirectory, "checkout", "--detach", $UpstreamCommit)

$actualCommit = (& $GitExe -C $WorkspaceDirectory rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $UpstreamCommit) {
    throw "Pinned upstream verification failed. Expected $UpstreamCommit, got '$actualCommit'."
}

Invoke-Checked -FilePath $GitExe -Arguments @(
    "-C", $WorkspaceDirectory, "submodule", "sync", "--recursive")
Invoke-Checked -FilePath $GitExe -Arguments @(
    "-C", $WorkspaceDirectory, "submodule", "update", "--init", "--recursive", "--depth", "1")

$SetupProject = Join-Path $WorkspaceDirectory "setup\CLI\Setup.CLI.csproj"
if (-not (Test-Path $SetupProject -PathType Leaf)) {
    throw "The pinned upstream workspace does not contain setup/CLI/Setup.CLI.csproj."
}

function Invoke-UpstreamSetup {
    param([string[]]$CommandArguments)

    # The pinned v0.3.3 Windows setup-cli.bat has an upstream cwd bug: it
    # changes into setup/ even though the CLI itself expects paths such as
    # setup/user.settings and src/WorkspaceInfo.targets to be relative to the
    # repository root. Its trailing `cd ..` also masks dotnet's non-zero exit
    # code. Invoke the CLI project directly from the workspace root instead.
    $setupArguments = @(
        "run",
        "--project", $SetupProject,
        "-c", "Release",
        "-p:WarningLevel=0",
        "-v", "q",
        "--"
    ) + $CommandArguments

    Invoke-Checked -FilePath $DotnetExe -WorkingDirectory $WorkspaceDirectory -Arguments $setupArguments
}

# Generate source from the user's own installed 1.4.5.8 executable. If the
# matching TerrariaServer.exe is absent, the pinned upstream setup retrieves
# that exact server version from Re-Logic's terraria.org dedicated-server API.
Invoke-UpstreamSetup -CommandArguments @(
    "decompile", "--no-prompts", "--plain-progress",
    "--terraria-steam-dir", $TerrariaDirectory)

# Apply only the vanilla cleanup stage and platform/runtime port.
Invoke-UpstreamSetup -CommandArguments @(
    "patch", "terraria", "--no-prompts", "--strict", "--plain-progress")
Invoke-UpstreamSetup -CommandArguments @(
    "patch", "netcore", "--no-prompts", "--strict", "--plain-progress")

$Project = Join-Path $WorkspaceDirectory "src\TerrariaNetCore\Terraria\Terraria.csproj"
if (-not (Test-Path $Project -PathType Leaf)) {
    throw "TerrariaNetCore project was not generated at '$Project'."
}

if (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

# TerrariaNetCore's project has an AfterBuild install target. Override
# TerrariaSteamPath so it installs into gloader's private runtime directory
# rather than touching the real Steam Terraria installation.
Invoke-Checked -FilePath $DotnetExe -Arguments @(
    "build", $Project,
    "-c", "Release",
    "-p:TerrariaSteamPath=$OutputDirectory",
    "-p:PlatformTarget=AnyCPU")

$ManagedTarget = Join-Path $OutputDirectory "TerrariaRelease.dll"
if (-not (Test-Path $ManagedTarget -PathType Leaf)) {
    throw "Build completed but TerrariaRelease.dll was not installed into '$OutputDirectory'."
}

# The real Terraria client immediately initializes Steam and therefore needs
# both the managed Steamworks.NET wrapper and the x64 Steam API native DLL.
# Explicitly stage the exact package referenced by the pinned TerrariaNetCore
# project so a successful build cannot produce an incomplete launch runtime.
$NuGetPackagesDirectory = Get-NuGetGlobalPackagesDirectory -DotnetPath $DotnetExe
$SteamworksSource = Join-Path $NuGetPackagesDirectory "$SteamworksPackageId\$SteamworksPackageVersion"
if (-not (Test-Path $SteamworksSource -PathType Container)) {
    throw "$SteamworksPackageDisplayName $SteamworksPackageVersion was not found in the NuGet package cache at '$SteamworksSource'."
}

$SteamworksDestination = Join-Path $OutputDirectory "Libraries\$SteamworksPackageId\$SteamworksPackageVersion"
if (Test-Path $SteamworksDestination) {
    Remove-Item $SteamworksDestination -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $SteamworksDestination | Out-Null
Get-ChildItem -Path $SteamworksSource -Force | Copy-Item -Destination $SteamworksDestination -Recurse -Force

$SteamworksManaged = Join-Path $SteamworksDestination "runtimes\win\lib\net8.0\Steamworks.NET.dll"
$SteamworksManagedFallback = Join-Path $SteamworksDestination "lib\net8.0\Steamworks.NET.dll"
$SteamworksNative = Join-Path $SteamworksDestination "runtimes\win-x64\native\steam_api64.dll"

if (-not (Test-Path $SteamworksManaged -PathType Leaf)) {
    throw "Staged Steamworks package is missing its Windows managed assembly: '$SteamworksManaged'."
}
if (-not (Test-Path $SteamworksManagedFallback -PathType Leaf)) {
    throw "Staged Steamworks package is missing its generic managed assembly: '$SteamworksManagedFallback'."
}
if (-not (Test-Path $SteamworksNative -PathType Leaf)) {
    throw "Staged Steamworks package is missing its win-x64 native library: '$SteamworksNative'."
}

# Keep the organized package tree for .deps.json/RID-aware resolution, but also
# put the selected Windows assets beside TerrariaRelease.dll. This gives the
# normal CLR and Windows native probing rules a simple, deterministic fallback.
$SteamworksManagedFlat = Join-Path $OutputDirectory "Steamworks.NET.dll"
$SteamworksNativeFlat = Join-Path $OutputDirectory "steam_api64.dll"
Copy-Item $SteamworksManaged $SteamworksManagedFlat -Force
Copy-Item $SteamworksNative $SteamworksNativeFlat -Force

if (-not (Test-Path $SteamworksManagedFlat -PathType Leaf)) {
    throw "Steamworks.NET.dll was not placed beside TerrariaRelease.dll."
}
if (-not (Test-Path $SteamworksNativeFlat -PathType Leaf)) {
    throw "steam_api64.dll was not placed beside TerrariaRelease.dll."
}

Write-Host "Steamworks managed: $SteamworksManagedFlat"
Write-Host "Steamworks x64:     $SteamworksNativeFlat"

$manifest = [ordered]@{
    format = 3
    terraria_sha256 = Get-Sha256 $TerrariaExe
    terraria_file_version = (Get-Item $TerrariaExe).VersionInfo.FileVersion
    upstream_repository = $UpstreamRepository
    upstream_tag = $UpstreamTag
    upstream_commit = $UpstreamCommit
    patch_stage = "TerrariaNetCore"
    unified_gameplay_patches = $false
    tmodloader_patches = $false
    target = "TerrariaRelease.dll"
    architecture = "x64-hosted AnyCPU"
    runtime = ".NET 10 / FNA"
    dotnet_sdk = $DotnetSdkVersion
    git_for_windows = $MinGitDisplayVersion
    steamworks_package = $SteamworksPackageDisplayName
    steamworks_version = $SteamworksPackageVersion
    steamworks_managed = "Steamworks.NET.dll"
    steamworks_native = "steam_api64.dll"
    steamworks_package_root = "Libraries/$SteamworksPackageId/$SteamworksPackageVersion"
    generated_utc = [DateTime]::UtcNow.ToString("o")
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory "gloader-x64-runtime.json")

if (-not $KeepGeneratedSource) {
    $generated = Join-Path $WorkspaceDirectory "src\decompiled"
    if (Test-Path $generated) {
        Remove-Item $generated -Recurse -Force
    }
}

Write-Host ""
Write-Host "DONE."
Write-Host "Built managed target: $ManagedTarget"
Write-Host "Verified Steamworks managed and win-x64 native dependencies."
Write-Host "gloader will prefer this runtime automatically when it is under gdeps\x64-runtime."
