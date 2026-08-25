[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $projectRoot "artifacts"
$publish = Join-Path $artifacts "publish"
$packageRoot = Join-Path $artifacts "GDupe-$Runtime"
$zip = Join-Path $artifacts "GDupe-$Runtime.zip"

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $publish, $packageRoot -Force | Out-Null

dotnet restore (Join-Path $projectRoot "GDupe.sln")
dotnet test (Join-Path $projectRoot "GDupe.sln") --configuration $Configuration --no-restore
dotnet publish (Join-Path $projectRoot "src/GDupe.App/GDupe.App.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $publish

$smoke = Start-Process -FilePath (Join-Path $publish "GDupe.exe") -ArgumentList "--smoke-test" -Wait -PassThru
if ($smoke.ExitCode -ne 0) { throw "Published GDupe.exe smoke test failed with exit code $($smoke.ExitCode)." }

Copy-Item (Join-Path $publish "*") $packageRoot -Recurse
Get-ChildItem $packageRoot -Filter "*.pdb" | Remove-Item -Force
Copy-Item (Join-Path $projectRoot "README.md") $packageRoot
Copy-Item (Join-Path $projectRoot "THIRD-PARTY-NOTICES.txt") $packageRoot
Copy-Item (Join-Path (Split-Path -Parent $projectRoot) "LICENSE") $packageRoot
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host "Package created: $zip"
