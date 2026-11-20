[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineBinaries,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/RuntimeServiceBoundary')
)
$ErrorActionPreference = 'Stop'
$binaries = [IO.Path]::GetFullPath($EngineBinaries)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $binaries BEngine.dll))) { throw 'EngineBinaries must contain BEngine.dll.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$project = Join-Path $PSScriptRoot 'Tests/RuntimeServiceBoundary/RuntimeServiceBoundary.csproj'
$artifacts = Join-Path $output Build
& dotnet build $project -c Release --artifacts-path $artifacts "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if ($LASTEXITCODE -ne 0) { throw "Runtime service test build failed; see $output/Build.log" }
& dotnet (Join-Path $artifacts 'bin/RuntimeServiceBoundary/release/CoreServiceContractProbe.dll') (Join-Path $output Result.json) *> (Join-Path $output Tests.log)
if ($LASTEXITCODE -ne 0) { throw "Runtime service tests failed; see $output/Tests.log" }
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
