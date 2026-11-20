[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$ArtifactsDirectory,
    [string]$InstallationRoot,
    [switch]$NoExport
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repositoryRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspaceRoot 'Temp/Plugin/Packages' }
if (-not $ArtifactsDirectory) { $ArtifactsDirectory = Join-Path $workspaceRoot 'Temp/Plugin/Build' }
if (-not $InstallationRoot) { $InstallationRoot = Join-Path $repositoryRoot 'Engine' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ArtifactsDirectory = [IO.Path]::GetFullPath($ArtifactsDirectory)

foreach ($hostName in @('VSCode', 'VisualStudio')) {
    $builder = Join-Path $PSScriptRoot "$hostName/Build.ps1"
    if (-not (Test-Path -LiteralPath $builder -PathType Leaf)) { throw "Plugin builder is missing: $builder" }
    $arguments = @{
        OutputDirectory = Join-Path $OutputDirectory $hostName
        ArtifactsDirectory = Join-Path $ArtifactsDirectory $hostName
    }
    if ($hostName -eq 'VisualStudio') { $arguments.Configuration = $Configuration }
    Write-Output "BENGINE_PLUGIN_BUILD|$hostName"
    $global:LASTEXITCODE = 0
    & $builder @arguments
    if ($LASTEXITCODE -ne 0) { throw "$hostName plugin build failed ($LASTEXITCODE)." }
    $packages = @(Get-ChildItem -LiteralPath $arguments.OutputDirectory -File -Filter '*.vsix')
    if ($packages.Count -ne 1) { throw "Expected one $hostName VSIX in $($arguments.OutputDirectory)." }
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        if ($null -eq $archive.GetEntry('extension.vsixmanifest') -or
            $null -eq $archive.GetEntry('[Content_Types].xml')) { throw "Invalid VSIX archive: $($packages[0].FullName)" }
    }
    finally { $archive.Dispose() }
}
if (-not $NoExport) {
    & (Join-Path $PSScriptRoot 'Export.ps1') -PackageDirectory $OutputDirectory -InstallationRoot $InstallationRoot
}
Write-Output "BENGINE_PLUGIN_BUILD_OK|$OutputDirectory"
