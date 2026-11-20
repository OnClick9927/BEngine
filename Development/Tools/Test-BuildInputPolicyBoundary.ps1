[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/BuildInputPolicyBoundary/Probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Build input policy test compilation failed: $output/Build.log"}
foreach($group in @('Core','Formats','Platform')) {
 & dotnet "$output/Build/bin/Probe/release/BuildInputPolicyProbe.dll" $output $group *> "$output/$group.log"
 if($LASTEXITCODE -ne 0){throw "Build input policy $group failed: $output/$group.log"}
 Get-Content "$output/$group/Result.json" -Raw
}
