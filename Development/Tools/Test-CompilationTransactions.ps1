[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
foreach($group in @('Core','Facade','Workbench')) {
    $folder=Join-Path $output $group
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    & dotnet build "$PSScriptRoot/Tests/CompilationTransactions/$group/$group.csproj" -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$folder/Build.log"
    if($LASTEXITCODE -ne 0){throw "Compilation transaction $group consumer build failed: $folder/Build.log"}
    & dotnet "$folder/Build/bin/$group/release/CompilationTransaction${group}Probe.dll" "$folder/Result.json" *> "$folder/Tests.log"
    if($LASTEXITCODE -ne 0){throw "Compilation transaction $group failed: $folder/Tests.log"}
    Get-Content "$folder/Result.json" -Raw
}
