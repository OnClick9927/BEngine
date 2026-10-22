$ErrorActionPreference = 'Stop'
$buildProperties = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)
$releaseVersion = ([string]$buildProperties.Project.PropertyGroup.Version).Trim()
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw "Invalid engine release version in Directory.Build.props: '$releaseVersion'."
}
$releaseVersion
