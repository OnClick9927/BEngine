[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
foreach($probe in @('Mechanisms','Integration')){
 $output=[IO.Path]::GetFullPath((Join-Path $OutputDirectory $probe))
 New-Item -ItemType Directory -Force -Path $output | Out-Null
 & dotnet build "$PSScriptRoot/Tests/EditorBuildMechanisms/$probe/$probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
 if($LASTEXITCODE -ne 0){throw "Editor build $probe build failed: $output/Build.log"}
 $name=if($probe -eq 'Mechanisms'){'EditorBuildMechanismsProbe'}else{'EditorBuildIntegrationProbe'}
 & dotnet "$output/Build/bin/$probe/release/$name.dll" $output *> "$output/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Editor build $probe failed: $output/Tests.log"}
 Get-Content "$output/Result.json" -Raw
}
