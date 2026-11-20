[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [string]$CompilationBinary,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output)|Out-Null
if(-not $CompilationBinary){$CompilationBinary=Join-Path $EngineBinaries 'BEngine.Core.Editor.dll'}
& dotnet run --project "$PSScriptRoot/Tests/ProjectPackageCacheAccess/Probe.csproj" -c Release --artifacts-path "$output/Build" `
    "-p:EngineBinaries=$([IO.Path]::GetFullPath($EngineBinaries))" "-p:CompilationBinary=$([IO.Path]::GetFullPath($CompilationBinary))" -p:NuGetAudit=false -- $output *> "$output/Tests.log"
if($LASTEXITCODE -ne 0){Get-Content "$output/Tests.log" -Tail 35;throw "Package cache access verification failed: $output/Tests.log"}
Get-Content "$output/Tests.log" -Tail 1
