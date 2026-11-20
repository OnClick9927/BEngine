[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/HubPackageSearch'),
    [string]$InstalledPackagesRoot
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$fixture = Join-Path $output ('Fixture-' + [Guid]::NewGuid().ToString('N'))
$probeArguments = @($fixture)
if ($InstalledPackagesRoot) { $probeArguments += [IO.Path]::GetFullPath($InstalledPackagesRoot) }
& dotnet run --project "$PSScriptRoot/Tests/HubPackageSearch/Probe.csproj" -c Release `
    --artifacts-path "$output/Build" -p:NuGetAudit=false -p:BEngineEngineExportOwner=false `
    -p:BEnginePackageExportOwner=false -- @probeArguments *> "$output/Tests.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$output/Tests.log" -Tail 25; throw "Hub search tests failed: $output/Tests.log" }
Get-Content "$output/Tests.log" -Tail 1
