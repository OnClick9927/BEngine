[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineBinaries,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/PlayerContentExecution')
)
$ErrorActionPreference = 'Stop'
$binaries = [IO.Path]::GetFullPath($EngineBinaries)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $binaries BEngine.Player.dll))) { throw 'EngineBinaries must contain a completed engine export.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$project = Join-Path $PSScriptRoot 'Tests/PlayerContentExecution/PlayerContentExecution.csproj'
$artifacts = Join-Path $output Build
& dotnet build $project -c Release --artifacts-path $artifacts "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if ($LASTEXITCODE -ne 0) { throw "Content execution test build failed; see $output/Build.log" }
& dotnet (Join-Path $artifacts 'bin/PlayerContentExecution/release/PlayerContentExecution.dll') $output *> (Join-Path $output Tests.log)
if ($LASTEXITCODE -ne 0) { throw "Content execution tests failed; see $output/Tests.log" }
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
