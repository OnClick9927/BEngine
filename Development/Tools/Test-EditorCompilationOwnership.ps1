[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/EditorCompilationOwnership/Probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Compilation ownership consumer build failed: $output/Build.log"}
foreach($group in @('Core','Factory','Graphics')){
    $folder=Join-Path $output $group
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    & dotnet "$output/Build/bin/Probe/release/EditorCompilationOwnershipProbe.dll" $group $folder *> "$folder/Tests.log"
    if($LASTEXITCODE -ne 0){throw "Compilation ownership $group tests failed: $folder/Tests.log"}
    Get-Content "$folder/Result.json" -Raw
}
