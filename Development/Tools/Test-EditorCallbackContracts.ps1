[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$EngineBinaries,
    [string]$OutputDirectory=(Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/EditorCallbackContracts'),
    [switch]$CoreOnly
)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
foreach($name in $(if($CoreOnly){@('Core')}else{@('Core','Workflow')})){
    $folder=Join-Path $output $name
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    & dotnet build "$PSScriptRoot/Tests/EditorCallbackContracts/$name/$name.csproj" -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$EngineBinaries" -p:NuGetAudit=false *> "$folder/Build.log"
    if($LASTEXITCODE -ne 0){throw "Editor callback $name fixture build failed: $folder/Build.log"}
    & dotnet "$folder/Build/bin/$name/release/EditorCallback${name}Probe.dll" "$folder/Result.json" *> "$folder/Tests.log"
    if($LASTEXITCODE -ne 0){throw "Editor callback $name tests failed: $folder/Tests.log"}
    Get-Content "$folder/Result.json" -Raw
}
