[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/PlayerPublishBoundary/Core.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Player publication contract build failed: $output/Build.log"}
& dotnet "$output/Build/bin/Core/release/PlayerPublishCore.dll" $output *> "$output/Tests.log"
if($LASTEXITCODE -ne 0){throw "Player publication contract failed: $output/Tests.log"}
Get-Content "$output/Result.json" -Raw
