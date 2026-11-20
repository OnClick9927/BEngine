[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [switch]$CoreOnly)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$probes=@('Core')
if(-not $CoreOnly){$probes+='Workflow'}
foreach($probe in $probes){
    $folder=Join-Path $output $probe
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    & dotnet build "$PSScriptRoot/Tests/PrefabEditingContracts/$probe/$probe.csproj" -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> "$folder/Build.log"
    if($LASTEXITCODE -ne 0){throw "Prefab $probe build failed: $folder/Build.log"}
    & dotnet "$folder/Build/bin/$probe/release/PrefabEditing$($probe)Probe.dll" $folder *> "$folder/Tests.log"
    if($LASTEXITCODE -ne 0){throw "Prefab $probe tests failed: $folder/Tests.log"}
    Get-Content -LiteralPath "$folder/Result.json" -Raw
}
