[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/HubGitHubPackages'))
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$fixture = Join-Path $output ('Fixture-' + [Guid]::NewGuid().ToString('N'))
& dotnet run --project "$PSScriptRoot/Tests/HubGitHubPackages/Probe.csproj" -c Release `
    --artifacts-path "$output/Build" -p:NuGetAudit=false -p:BEngineEngineExportOwner=false `
    -p:BEnginePackageExportOwner=false -- $fixture *> "$output/Tests.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$output/Tests.log" -Tail 30; throw "Hub GitHub package tests failed: $output/Tests.log" }
Get-Content "$output/Tests.log" -Tail 1
