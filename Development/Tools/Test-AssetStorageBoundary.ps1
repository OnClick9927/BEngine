[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build (Join-Path $PSScriptRoot 'Tests/AssetStorageBoundary/AssetStorageBoundary.csproj') -c Release --artifacts-path (Join-Path $output Build) "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if($LASTEXITCODE -ne 0){throw "Asset storage contract build failed: $output/Build.log"}
& dotnet (Join-Path $output 'Build/bin/AssetStorageBoundary/release/AssetStorageContractProbe.dll') $output *> (Join-Path $output Tests.log)
if($LASTEXITCODE -ne 0){throw "Asset storage contract tests failed: $output/Tests.log"}
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
