[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,[Parameter(Mandatory=$true)][string]$OutputDirectory,
      [string]$BrandingDirectory = (Join-Path $PSScriptRoot '../../src/Packages/Graphics/Resources/Icons'))
$ErrorActionPreference='Stop'
foreach($probe in @('Core','Graphics')) {
 $output=[IO.Path]::GetFullPath((Join-Path $OutputDirectory $probe))
 New-Item -ItemType Directory -Force -Path $output | Out-Null
 & dotnet build "$PSScriptRoot/Tests/PlayerStagingBoundary/$probe/$probe.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" -p:NuGetAudit=false *> "$output/Build.log"
 if($LASTEXITCODE -ne 0){throw "Player staging $probe build failed: $output/Build.log"}
 & dotnet "$output/Build/bin/$probe/release/PlayerStaging$probe.dll" $output ([IO.Path]::GetFullPath($BrandingDirectory)) *> "$output/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Player staging $probe failed: $output/Tests.log"}
 Get-Content "$output/Result.json" -Raw
}
