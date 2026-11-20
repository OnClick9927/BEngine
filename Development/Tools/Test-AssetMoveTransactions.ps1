[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path "$output/System","$output/DotnetHome" | Out-Null
$env:NUGET_PACKAGES='C:/Users/OnClick/.nuget/packages'
$env:DOTNET_CLI_HOME="$output/DotnetHome";$env:TEMP="$output/System";$env:TMP=$env:TEMP
& dotnet build "$PSScriptRoot/Tests/AssetMoveTransactions/AssetMoveTransactions.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$EngineBinaries" -p:NuGetAudit=false -p:BEnginePackageExportOwner=false -p:BEngineEngineExportOwner=false *> "$output/Build.log"
if($LASTEXITCODE){Get-Content "$output/Build.log" -Tail 25;throw 'Asset move test build failed'}
& dotnet "$output/Build/bin/AssetMoveTransactions/release/AssetMoveTransactions.dll" "$output/Result" *> "$output/Test.log"
if($LASTEXITCODE){Get-Content "$output/Test.log" -Tail 25;throw 'Asset move tests failed'}
Get-Content "$output/Result/Result.json" -Raw
