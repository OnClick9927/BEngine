[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
foreach($mode in @('Headless','Legacy','Current')){
    $folder=Join-Path $output $mode
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $project=if($mode -eq 'Headless'){'Headless'}else{'Workflow'}
    $binaries=if($mode -eq 'Legacy'){$LegacyBinaries}else{$EngineBinaries}
    $legacy=if($mode -eq 'Legacy'){'true'}else{'false'}
    & dotnet build "$PSScriptRoot/Tests/AssetInspectorOwnership/$project/$project.csproj" -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$binaries" "-p:LegacyInspector=$legacy" -p:NuGetAudit=false *> "$folder/Build.log"
    if($LASTEXITCODE -ne 0){throw "Asset Inspector $mode build failed: $folder/Build.log"}
    $name=if($mode -eq 'Headless'){'AssetInspectorHeadless'}else{'AssetInspectorWorkflow'}
    & dotnet "$folder/Build/bin/$project/release/$name.dll" $folder *> "$folder/Tests.log"
    if($LASTEXITCODE -ne 0){throw "Asset Inspector $mode failed: $folder/Tests.log"}
    Get-Content "$folder/Result.json" -Raw
}
$before=[IO.File]::ReadAllText("$output/Legacy/Snapshot.json")
$after=[IO.File]::ReadAllText("$output/Current/Snapshot.json")
if($before -cne $after){throw 'Asset Inspector legacy/current drawing commands differ'}
if([IO.File]::ReadAllText("$output/Legacy/UndoSnapshot.json") -cne [IO.File]::ReadAllText("$output/Current/UndoSnapshot.json")){throw 'RenderTexture existing Undo behavior changed'}
@{Passed=$true;Checks=5;Comparison='Exact legacy/current GUI drawing commands for Texture, unavailable Texture, RenderTexture, Script and generic asset'} | ConvertTo-Json | Set-Content "$output/Parity.json" -Encoding utf8
