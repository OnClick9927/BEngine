[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/HubPackageDistribution'))
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$fixture = Join-Path $output ('Fixture-' + [Guid]::NewGuid().ToString('N'))
& dotnet run --project "$PSScriptRoot/Tests/HubPackageDistribution/Probe.csproj" -c Release `
    --artifacts-path "$output/Build" -p:NuGetAudit=false -p:BEngineEngineExportOwner=false `
    -p:BEnginePackageExportOwner=false -- $fixture *> "$output/Tests.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$output/Tests.log" -Tail 30; throw "Hub provenance tests failed: $output/Tests.log" }
Get-Content "$output/Tests.log" -Tail 1

# A source-owned release with a vendor ID is bundled; a later addition with a BEngine ID is not.
$source = Join-Path $output 'ExportFixture/Source/Packages/EngineModule'
$destination = Join-Path $output 'ExportFixture/Packages/EngineModule/EngineModule-1.0.0'
$external = Join-Path $output 'ExportFixture/Packages/External/External-1.0.0'
[void][IO.Directory]::CreateDirectory($source)
[void][IO.Directory]::CreateDirectory($destination)
[void][IO.Directory]::CreateDirectory($external)
$definition = "format: BEngine.Package`nversion: 2`nid: com.vendor.bundled`npackageVersion: 1.0.0`nminimumEngineVersion: 0.1.0`n"
[IO.File]::WriteAllText((Join-Path $source 'package.yaml'), $definition)
[IO.File]::WriteAllText((Join-Path $destination 'package.yaml'), $definition)
[IO.File]::WriteAllText((Join-Path $external 'package.yaml'), $definition.Replace('com.vendor.bundled', 'com.bengine.external'))
$manifestPath = Join-Path $output 'ExportFixture/bundled-packages.json'
& (Join-Path $PSScriptRoot '../Scripts/ExportBundledPackages.ps1') `
    -SourceRoot (Join-Path $output 'ExportFixture/Source') -PackagesRoot (Join-Path $output 'ExportFixture/Packages') `
    -EngineVersion '0.1.0' -OutputPath $manifestPath
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.packages.Count -ne 1 -or $manifest.packages[0].id -ne 'com.vendor.bundled') {
    throw 'Export incorrectly included an external package.'
}
'HUB_BUNDLED_EXPORT_OK|packages=1|external=excluded'
