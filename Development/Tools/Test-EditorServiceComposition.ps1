[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineBinaries,
    [string]$EditorAssembly,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/EditorServiceComposition')
)

$ErrorActionPreference = 'Stop'
$binaries = [IO.Path]::GetFullPath($EngineBinaries)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $binaries BEngine.Core.dll))) { throw 'EngineBinaries must contain an exported engine.' }
if ([string]::IsNullOrWhiteSpace($EditorAssembly)) { $EditorAssembly = Join-Path $binaries BEngine.Core.Editor.dll }
$editor = [IO.Path]::GetFullPath($EditorAssembly)
if (-not (Test-Path -LiteralPath $editor)) { throw "Missing Editor assembly: $editor" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$project = Join-Path $PSScriptRoot 'Tests/EditorServiceComposition/EditorServiceComposition.csproj'
$artifacts = Join-Path $output Build
& dotnet build $project -c Release --artifacts-path $artifacts "-p:EngineBinaries=$binaries" "-p:EditorAssembly=$editor" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if ($LASTEXITCODE -ne 0) { throw "Service composition test build failed; see $output/Build.log" }
& dotnet (Join-Path $artifacts 'bin/EditorServiceComposition/release/EditorServiceComposition.dll') (Join-Path $output Result.json) *> (Join-Path $output Tests.log)
if ($LASTEXITCODE -ne 0) { throw "Service composition tests failed; see $output/Tests.log" }
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
