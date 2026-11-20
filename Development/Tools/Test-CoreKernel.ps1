[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/CoreKernel'),
    [string]$SourceRoot = (Join-Path $PSScriptRoot '../../src')
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
& dotnet run --project "$PSScriptRoot/Tests/CoreKernel/Probe.csproj" -c Release --artifacts-path "$output/Build" `
    -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false -- ([IO.Path]::GetFullPath($SourceRoot)) *> "$output/Tests.log"
if ($LASTEXITCODE -ne 0) { Get-Content "$output/Tests.log" -Tail 25; throw "Core kernel verification failed: $output/Tests.log" }
Copy-Item -LiteralPath "$output/Build/bin/Probe/release/KernelResult.json" -Destination "$output/Result.json"
Get-Content "$output/Tests.log" -Tail 1
