[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/StartupTemplateShaderBoundary/Probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Startup/template policy test build failed: $output/Build.log"}
foreach($group in @('Core','Application','Platform')) {
 & dotnet "$output/Build/bin/Probe/release/StartupTemplateShaderProbe.dll" $output $group ([IO.Path]::GetFullPath($LegacyBinaries)) *> "$output/$group.log"
 if($LASTEXITCODE -ne 0){throw "Startup/template policy $group failed: $output/$group.log"}
 Get-Content "$output/$group/Result.json" -Raw
}
