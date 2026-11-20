[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$EngineBinaries,
    [Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$binaries = [IO.Path]::GetFullPath($EngineBinaries)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $PSScriptRoot 'Tests/ResourceCodecBoundary/ResourceCodecBoundary.csproj'
foreach ($mode in @('Headless', 'Graphics')) {
    $target = Join-Path $output $mode
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    & dotnet build $project -c Release --artifacts-path (Join-Path $target Build) "-p:EngineBinaries=$binaries" "-p:GraphicsChecks=$($mode -eq 'Graphics')" -p:NuGetAudit=false *> (Join-Path $target Build.log)
    if ($LASTEXITCODE -ne 0) { throw "Resource codec $mode build failed; see $target/Build.log" }
    & dotnet (Join-Path $target 'Build/bin/ResourceCodecBoundary/release/ResourceCodecBoundary.dll') $target *> (Join-Path $target Tests.log)
    if ($LASTEXITCODE -ne 0) { throw "Resource codec $mode tests failed; see $target/Tests.log" }
    Get-Content (Join-Path $target Result.json) -Raw
}
