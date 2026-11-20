[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ArtifactsDirectory,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
$source = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
foreach ($directory in @($output,$artifacts)) {
    if (($directory + [IO.Path]::DirectorySeparatorChar).StartsWith($source,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Build output must be outside the VisualStudio source directory. Use the repository Temp directory.'
    }
    [void][IO.Directory]::CreateDirectory($directory)
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not [IO.File]::Exists($vswhere)) { throw 'Visual Studio with the .NET desktop development tools is required.' }
$msbuild = @(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
if ($msbuild.Count -ne 1) { throw 'MSBuild was not found in the Visual Studio installation.' }
$buildOutput = Join-Path $artifacts 'bin'
$intermediate = (Join-Path $artifacts 'obj') + [IO.Path]::DirectorySeparatorChar
$arguments = @(
    (Join-Path $PSScriptRoot 'BEngine.VisualStudio.csproj'), '-restore', '-m:1', '-nodeReuse:false', '-verbosity:minimal',
    "-p:Configuration=$Configuration", "-p:OutputPath=$buildOutput", "-p:BaseIntermediateOutputPath=$intermediate",
    '-p:DeployExtension=false', '-p:NuGetAudit=false', '-p:ContinuousIntegrationBuild=true'
)
$previousTemp = [Environment]::GetEnvironmentVariable('TEMP','Process')
$previousTmp = [Environment]::GetEnvironmentVariable('TMP','Process')
$toolTemp = Join-Path $artifacts 'Temp'
[void][IO.Directory]::CreateDirectory($toolTemp)
try {
    $env:TEMP = $toolTemp
    $env:TMP = $toolTemp
    & $msbuild[0] @arguments
    if ($LASTEXITCODE -ne 0) { throw "Visual Studio plugin build failed: $LASTEXITCODE" }
}
finally {
    [Environment]::SetEnvironmentVariable('TEMP',$previousTemp,'Process')
    [Environment]::SetEnvironmentVariable('TMP',$previousTmp,'Process')
}
$vsix = Join-Path $buildOutput 'BEngine.VisualStudio.vsix'
if (-not [IO.File]::Exists($vsix)) { throw "The SDK did not produce a VSIX: $vsix" }
Copy-Item -LiteralPath $vsix -Destination (Join-Path $output 'BEngine.VisualStudio.vsix') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $output 'README.md') -Force
[pscustomobject]@{VSIX=(Join-Path $output 'BEngine.VisualStudio.vsix');SHA256=(Get-FileHash -LiteralPath (Join-Path $output 'BEngine.VisualStudio.vsix')).Hash} | ConvertTo-Json -Compress
