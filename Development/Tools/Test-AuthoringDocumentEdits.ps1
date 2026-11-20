[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/AuthoringDocumentEdits/Probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Document edit probe build failed: $output/Build.log"}
foreach($group in @('Core','Baselines','Workbench')){
 $folder=Join-Path $output $group
 New-Item -ItemType Directory -Force -Path $folder | Out-Null
 & dotnet "$output/Build/bin/Probe/release/AuthoringDocumentEditProbe.dll" $group $folder $LegacyBinaries *> "$folder/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Document edit $group tests failed: $folder/Tests.log"}
 Get-Content "$folder/Result.json" -Raw
}
