[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/ShaderGraphLightingBoundary'))

$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $PSScriptRoot 'Tests/ShaderGraphLightingBoundary/ShaderGraphLightingBoundary.csproj'
$lightingResources = Join-Path $PSScriptRoot '../../src/Packages/Lighting2D'
$artifacts = Join-Path $output 'Build'
& dotnet build $project -c Release --artifacts-path $artifacts -p:NuGetAudit=false -v:q
if ($LASTEXITCODE -ne 0) { throw 'Shader Graph lighting boundary build failed.' }
$runner = Join-Path $artifacts 'bin/ShaderGraphLightingBoundary/release/ShaderGraphLightingBoundary.dll'
foreach ($mode in @('without', 'with')) {
    & dotnet $runner $mode $lightingResources
    if ($LASTEXITCODE -ne 0) { throw "Shader Graph lighting boundary failed: $mode" }
}
