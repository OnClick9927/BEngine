param([Parameter(Mandatory=$true)][string]$EngineBinaries, [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
$output=[IO.Path]::GetFullPath($OutputDirectory)
$tests=Join-Path $PSScriptRoot 'Tests/EditorBootstrapBoundary'
$fixtures=Join-Path $output Fixtures
New-Item -ItemType Directory -Force -Path $output,$fixtures | Out-Null
foreach($name in @('Entry','Policy')) {
    & dotnet publish (Join-Path $tests "Fixtures/$name/$name.csproj") -c Release --artifacts-path (Join-Path $output BuildFixtures) -o $fixtures "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $output "$name.Build.log")
    if($LASTEXITCODE -ne 0){throw "Bootstrap fixture $name failed; see $output/$name.Build.log"}
}
& dotnet build (Join-Path $tests EditorBootstrapBoundary.csproj) -c Release --artifacts-path (Join-Path $output Build) "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if($LASTEXITCODE -ne 0){throw "Bootstrap test build failed; see $output/Build.log"}
& dotnet (Join-Path $output Build/bin/EditorBootstrapBoundary/release/EditorBootstrapBoundary.dll) $binaries $output *> (Join-Path $output Tests.log)
if($LASTEXITCODE -ne 0){throw "Bootstrap tests failed; see $output/Tests.log"}
Get-Content (Join-Path $output Result.json) -Raw
$shader=Join-Path $output Shader
New-Item -ItemType Directory -Force -Path $shader | Out-Null
& dotnet build (Join-Path $tests Shader/Shader.csproj) -c Release --artifacts-path (Join-Path $shader Build) "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $shader Build.log)
if($LASTEXITCODE -ne 0){throw "Independent shader probe build failed; see $shader/Build.log"}
& dotnet (Join-Path $shader Build/bin/Shader/release/Shader.dll) $binaries $shader *> (Join-Path $shader Tests.log)
if($LASTEXITCODE -ne 0){throw "Independent shader probe failed; see $shader/Tests.log"}
Get-Content (Join-Path $shader Result.json) -Raw
