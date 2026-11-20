[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($project in @('Core','AssetBundles')){
 & dotnet build "$PSScriptRoot/Tests/BuildSessionBoundary/$project/$project.csproj" -c Release --artifacts-path "$output/$project/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/$project-Build.log"
 if($LASTEXITCODE -ne 0){throw "Build lifecycle contract build failed: $output/$project-Build.log"}
 & dotnet "$output/$project/Build/bin/$project/release/BuildSession$project.dll" "$output/$project" *> "$output/$project-Tests.log"
 if($LASTEXITCODE -ne 0){throw "Build lifecycle contract failed: $output/$project-Tests.log"}
 Get-Content "$output/$project/Result.json" -Raw
}
