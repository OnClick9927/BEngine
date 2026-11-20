[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/ProjectToolSession/Probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Tool session consumer build failed: $output/Build.log"}
foreach($group in @('Lifecycle','Isolation','Failure','Importer','Collection','Determinism')){
 $folder=Join-Path $output $group
 New-Item -ItemType Directory -Force -Path $folder | Out-Null
 & dotnet "$output/Build/bin/Probe/release/ProjectToolProbe.dll" $group $folder *> "$folder/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Tool session $group tests failed: $folder/Tests.log"}
 Get-Content "$folder/Result.json" -Raw
}
