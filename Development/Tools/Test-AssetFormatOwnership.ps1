[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
foreach($probe in @('Registry','Workflow')){
 $output=[IO.Path]::GetFullPath((Join-Path $OutputDirectory $probe))
 New-Item -ItemType Directory -Force -Path $output | Out-Null
 & dotnet build "$PSScriptRoot/Tests/AssetFormatOwnership/$probe/$probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
 if($LASTEXITCODE -ne 0){throw "Asset format $probe build failed: $output/Build.log"}
 & dotnet "$output/Build/bin/$probe/release/AssetFormat${probe}Probe.dll" $output *> "$output/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Asset format $probe failed: $output/Tests.log"}
 Get-Content "$output/Result.json" -Raw
}
