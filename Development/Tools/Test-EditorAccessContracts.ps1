[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
foreach($probe in @('Core','Workflow')){
 $output=[IO.Path]::GetFullPath((Join-Path $OutputDirectory $probe))
 New-Item -ItemType Directory -Force -Path $output | Out-Null
 & dotnet build "$PSScriptRoot/Tests/EditorAccessContracts/$probe/$probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
 if($LASTEXITCODE -ne 0){throw "Editor access $probe build failed: $output/Build.log"}
 & dotnet "$output/Build/bin/$probe/release/EditorAccess${probe}Probe.dll" $output *> "$output/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Editor access $probe failed: $output/Tests.log"}
 Get-Content "$output/Result.json" -Raw
}
