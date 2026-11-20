[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build "$PSScriptRoot/Tests/EditorSceneEditing/Core.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> "$output/Build.log"
if($LASTEXITCODE -ne 0){throw "Scene editing build failed: $output/Build.log"}
& dotnet "$output/Build/bin/Core/release/EditorSceneEditingProbe.dll" $output *> "$output/Tests.log"
if($LASTEXITCODE -ne 0){throw "Scene editing tests failed: $output/Tests.log"}
Get-Content -LiteralPath "$output/Result.json" -Raw
