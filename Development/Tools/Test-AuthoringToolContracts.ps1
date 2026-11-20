[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/AuthoringToolContracts/Probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Authoring tool consumer build failed: $output/Build.log"}
foreach($group in @('Paths','Templates','References','Compilation')){
 $folder=Join-Path $output $group
 New-Item -ItemType Directory -Force -Path $folder | Out-Null
 if($group -eq 'Paths'){
  $target=Join-Path $folder 'LinkTarget';$link=Join-Path $folder 'LinkedBuild'
  New-Item -ItemType Directory -Force -Path $target | Out-Null
  if(!(Test-Path -LiteralPath $link)){New-Item -ItemType Junction -Path $link -Target $target | Out-Null}
 }
 & dotnet "$output/Build/bin/Probe/release/AuthoringToolProbe.dll" $group $folder ([IO.Path]::GetFullPath($LegacyBinaries)) *> "$folder/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Authoring tool $group tests failed: $folder/Tests.log"}
 Get-Content "$folder/Result.json" -Raw
}
