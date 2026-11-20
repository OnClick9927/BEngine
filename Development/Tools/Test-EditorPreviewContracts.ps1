[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
foreach($probe in @('Core','Provider','Workflow')){
 $output=[IO.Path]::GetFullPath((Join-Path $OutputDirectory $probe))
 New-Item -ItemType Directory -Force -Path $output | Out-Null
 & dotnet build "$PSScriptRoot/Tests/EditorPreviewContracts/$probe/$probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> "$output/Build.log"
 if($LASTEXITCODE -ne 0){throw "Preview $probe build failed: $output/Build.log"}
 if($probe -eq 'Provider'){$provider="$output/Build/bin/Provider/release/CollectiblePreviewProvider.dll";continue}
 $name=if($probe -eq 'Core'){'PreviewCoreContracts'}else{'PreviewWorkflowContracts'}
 & dotnet "$output/Build/bin/$probe/release/$name.dll" $output $provider *> "$output/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Preview $probe failed: $output/Tests.log"}
 Get-Content "$output/Result.json" -Raw
}
