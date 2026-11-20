[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path "$output/OldProvider" | Out-Null
& dotnet build "$PSScriptRoot/Tests/EditorPreviewContracts/Provider/Provider.csproj" -c Release --artifacts-path "$output/OldProvider/Build" "-p:EngineBinaries=$LegacyBinaries" -p:NuGetAudit=false *> "$output/OldProvider/Build.log"
if($LASTEXITCODE -ne 0){throw 'Legacy preview provider build failed'}
$provider="$output/OldProvider/Build/bin/Provider/release/CollectiblePreviewProvider.dll"
foreach($mode in @('Headless','Legacy','Current')){
 $folder=Join-Path $output $mode
 New-Item -ItemType Directory -Force -Path $folder | Out-Null
 $project=if($mode -eq 'Headless'){'Headless'}else{'Workflow'}
 $binaries=if($mode -eq 'Legacy'){$LegacyBinaries}else{$EngineBinaries}
 $legacy=if($mode -eq 'Legacy'){'true'}else{'false'}
 & dotnet build "$PSScriptRoot/Tests/AssetPreviewOwnership/$project/$project.csproj" -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$binaries" "-p:LegacyInspector=$legacy" -p:NuGetAudit=false *> "$folder/Build.log"
 if($LASTEXITCODE -ne 0){throw "Preview ownership $mode build failed: $folder/Build.log"}
 $name=if($mode -eq 'Headless'){'AssetPreviewHeadless'}else{'AssetPreviewWorkflow'}
 & dotnet "$folder/Build/bin/$project/release/$name.dll" $folder "$output/Common" $provider *> "$folder/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Preview ownership $mode failed: $folder/Tests.log"}
 Get-Content "$folder/Result.json" -Raw
}
if([IO.File]::ReadAllText("$output/Legacy/Snapshot.json") -cne [IO.File]::ReadAllText("$output/Current/Snapshot.json")){throw 'Preview legacy/current GUI commands or metadata differ'}
@{Passed=$true;Checks=23;Comparison='Exact legacy/current preview raster/metadata and GUI commands at three sizes for 23 asset cases'} | ConvertTo-Json | Set-Content "$output/Parity.json" -Encoding utf8
