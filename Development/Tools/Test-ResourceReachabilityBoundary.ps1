[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/ResourceReachabilityBoundary/Probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Resource reachability test compilation failed: $output/Build.log"}
foreach($group in @('Core','Integration')) {
 & dotnet "$output/Build/bin/Probe/release/ResourceReachabilityProbe.dll" $output $group *> "$output/$group.log"
 if($LASTEXITCODE -ne 0){throw "Resource reachability $group failed: $output/$group.log"}
 Get-Content "$output/$group/Result.json" -Raw
}
