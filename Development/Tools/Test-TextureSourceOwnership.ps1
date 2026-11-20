[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build (Join-Path $PSScriptRoot 'Tests/TextureSourceOwnership/CallbackDriver/CallbackDriver.csproj') -c Release --artifacts-path "$output/DriverBuild" -p:NuGetAudit=false *> "$output/DriverBuild.log"
if($LASTEXITCODE -ne 0){throw 'Callback fixture build failed'}
& dotnet build (Join-Path $PSScriptRoot 'Tests/TextureSourceOwnership/TextureSourceOwnership.csproj') -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw 'Texture source contract build failed'}
& dotnet "$output/Build/bin/TextureSourceOwnership/release/TextureSourceOwnership.dll" $output "$output/DriverBuild/bin/CallbackDriver/release/CallbackDriver.dll" *> "$output/Tests.log"
if($LASTEXITCODE -ne 0){throw 'Texture source contracts failed'}
Get-Content -LiteralPath "$output/Result.json" -Raw
