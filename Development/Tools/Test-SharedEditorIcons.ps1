[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($mode in @('Legacy','Current')){
    $folder=Join-Path $output $mode
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $binaries=if($mode -eq 'Legacy'){$LegacyBinaries}else{$EngineBinaries}
    $legacy=if($mode -eq 'Legacy'){'true'}else{'false'}
    & dotnet build "$PSScriptRoot/Tests/SharedEditorIcons/Probe.csproj" -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$binaries" "-p:LegacyIcons=$legacy" -p:NuGetAudit=false *> "$folder/Build.log"
    if($LASTEXITCODE -ne 0){throw "Shared icon $mode build failed: $folder/Build.log"}
    $arguments=@(if($mode -eq 'Legacy'){$folder; 'legacy'}else{$folder})
    & dotnet "$folder/Build/bin/Probe/release/SharedEditorIconProbe.dll" @arguments *> "$folder/Tests.log"
    if($LASTEXITCODE -ne 0){throw "Shared icon $mode tests failed: $folder/Tests.log"}
}
$before=Get-Content "$output/Legacy/Snapshot.json" -Raw | ConvertFrom-Json -AsHashtable
$after=Get-Content "$output/Current/Snapshot.json" -Raw | ConvertFrom-Json -AsHashtable
if($before.Count -ne $after.Count){throw 'Icon snapshot entry count changed'}
foreach($key in $before.Keys){if($before[$key] -cne $after[$key]){throw "Icon behavior changed: $key"}}
@{Passed=$true;Checks=$before.Count;Comparison='Independent legacy/current metadata snapshots'} | ConvertTo-Json | Set-Content "$output/Parity.json" -Encoding utf8
Get-Content "$output/Current/Result.json" -Raw
