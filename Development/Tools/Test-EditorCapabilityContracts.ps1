[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$EngineBinaries,
    [string]$OutputDirectory=(Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/EditorCapabilityContracts')
)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/EditorCapabilityContracts/EditorCapabilityContracts.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$EngineBinaries" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Editor capability fixture build failed: $output/Build.log"}
& dotnet "$output/Build/bin/EditorCapabilityContracts/release/EditorCapabilityContracts.dll" "$output/Result.json" *> "$output/Tests.log"
if($LASTEXITCODE -ne 0){throw "Editor capability tests failed: $output/Tests.log"}
Get-Content "$output/Result.json" -Raw
