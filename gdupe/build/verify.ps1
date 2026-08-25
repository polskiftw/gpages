$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
dotnet restore (Join-Path $projectRoot "GDupe.sln")
dotnet build (Join-Path $projectRoot "GDupe.sln") --configuration Release --no-restore
dotnet test (Join-Path $projectRoot "GDupe.sln") --configuration Release --no-build --no-restore --logger "trx;LogFileName=release.trx"
