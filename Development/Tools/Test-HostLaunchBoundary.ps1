[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$EngineBinaries,
    [Parameter(Mandatory=$true)][string]$NodeExecutable,
    [string]$OutputDirectory=(Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/HostLaunchBoundary')
)
$ErrorActionPreference='Stop'
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$project=Join-Path $PSScriptRoot 'Tests/HostLaunchBoundary/HostLaunchBoundary.csproj'
& dotnet build $project -c Release --artifacts-path (Join-Path $output Build) "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if($LASTEXITCODE -ne 0){throw "Launch boundary test build failed: $output/Build.log"}
& (Join-Path $output 'Build/bin/HostLaunchBoundary/release/HostLaunchContractProbe.exe') $output (Join-Path $PSScriptRoot '../../src/Platform/Resources/Web/platform.js') *> (Join-Path $output Tests.log)
if($LASTEXITCODE -ne 0){throw "Launch boundary tests failed: $output/Tests.log"}
& $NodeExecutable (Join-Path $PSScriptRoot 'Tests/HostLaunchBoundary/WebLaunchTests.mjs') (Join-Path $PSScriptRoot '../../src/Platform/Resources/Web/platform.js') $output *> (Join-Path $output WebTests.log)
if($LASTEXITCODE -ne 0){throw "Web launch boundary tests failed: $output/WebTests.log"}
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
Get-Content -LiteralPath (Join-Path $output WebResult.json) -Raw
