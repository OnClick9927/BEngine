[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyFixtures,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build (Join-Path $PSScriptRoot 'Tests/AtlasBuildOwnership/Producer/Producer.csproj') -c Release --artifacts-path (Join-Path $output AtlasCompilerBuild) "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> (Join-Path $output AtlasCompilerBuild.log)
if($LASTEXITCODE -ne 0){throw 'Editor atlas fixture compiler build failed'}
& dotnet (Join-Path $output 'AtlasCompilerBuild/bin/Producer/release/Producer.dll') (Join-Path $output AtlasCompilerFixtures) (Join-Path ([IO.Path]::GetFullPath($LegacyFixtures)) 'atlas.yaml') *> (Join-Path $output AtlasCompilerTests.log)
if($LASTEXITCODE -ne 0){throw 'Editor atlas fixture compilation failed'}
& dotnet build (Join-Path $PSScriptRoot 'Tests/ObjectStateContracts/ObjectStateContracts.csproj') -c Release --artifacts-path (Join-Path $output Build) "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if($LASTEXITCODE -ne 0){throw "Object-state contract build failed: $output/Build.log"}
& dotnet (Join-Path $output 'Build/bin/ObjectStateContracts/release/ObjectStateContractProbe.dll') $output ([IO.Path]::GetFullPath($LegacyFixtures)) (Join-Path $output 'AtlasCompilerFixtures/atlas.runtime') *> (Join-Path $output Tests.log)
if($LASTEXITCODE -ne 0){throw "Object-state tests failed: $output/Tests.log"}
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
