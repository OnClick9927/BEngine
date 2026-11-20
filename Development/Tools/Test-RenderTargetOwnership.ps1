[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [switch]$Legacy,
      [switch]$Headless)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build (Join-Path $PSScriptRoot 'Tests/RenderTargetOwnership/RenderTargetOwnership.csproj') -c Release --artifacts-path (Join-Path $output Build) "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" "-p:Legacy=$($Legacy.IsPresent.ToString().ToLowerInvariant())" "-p:SceneChecks=$((!$Headless.IsPresent).ToString().ToLowerInvariant())" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if($LASTEXITCODE -ne 0){throw "Target ownership build failed: $output/Build.log"}
& dotnet (Join-Path $output 'Build/bin/RenderTargetOwnership/release/RenderTargetOwnership.dll') $output *> (Join-Path $output Tests.log)
if($LASTEXITCODE -ne 0){throw "Target ownership tests failed: $output/Tests.log"}
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
