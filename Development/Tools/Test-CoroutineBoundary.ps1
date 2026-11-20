param([Parameter(Mandatory=$true)][string]$EngineBinaries, [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
$output=[IO.Path]::GetFullPath($OutputDirectory)
$sdk=if((Split-Path $binaries -Leaf) -eq 'BuildRuntime'){Split-Path $binaries -Parent}else{$binaries}
New-Item -ItemType Directory -Force -Path $output|Out-Null
& dotnet build (Join-Path $PSScriptRoot 'Tests/CoroutineBoundary/CoroutineBoundary.csproj') -c Release --artifacts-path (Join-Path $output Build) "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if($LASTEXITCODE -ne 0){throw "Coroutine test build failed; see $output/Build.log"}
& dotnet (Join-Path $output Build/bin/CoroutineBoundary/release/CoroutineBoundary.dll) (Join-Path $output Result.json) *> (Join-Path $output Tests.log)
if($LASTEXITCODE -ne 0){throw "Coroutine tests failed; see $output/Tests.log"}
Get-Content (Join-Path $output Result.json) -Raw
