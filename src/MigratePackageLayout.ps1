param([string]$PackagesRoot = (Join-Path $PSScriptRoot '..\Engine\Packages'))
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath($PackagesRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not (Test-Path -LiteralPath $repositoryRoot)) { return }
function Assert-PackagePath([string]$Path) {
    if (-not [IO.Path]::GetFullPath($Path).StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package migration path escaped repository: $Path"
    }
}
foreach ($group in Get-ChildItem -LiteralPath $repositoryRoot -Directory) {
    if ($group.Name.StartsWith('.')) { continue }
    $definition = Join-Path $group.FullName 'package.yaml'
    if (-not (Test-Path -LiteralPath $definition)) { continue }
    $content = [IO.File]::ReadAllText($definition)
    # Pre-versioned packages were shipped with BEngine 0.1.0.
    if ($content -notmatch '(?m)^minimumEngineVersion:') {
        $content += "`nminimumEngineVersion: 0.1.0`n"
        [IO.File]::WriteAllText($definition, $content, [Text.UTF8Encoding]::new($false))
    }
    $release = & (Join-Path $PSScriptRoot 'GetPackageRelease.ps1') -DefinitionPath $definition
    $destination = Join-Path $group.FullName ($group.Name + '-' + $release.Version)
    $staging = Join-Path $repositoryRoot ('.migration-' + [Guid]::NewGuid().ToString('N'))
    Assert-PackagePath $destination
    Assert-PackagePath $staging
    if (Test-Path -LiteralPath $destination) { throw "Legacy package and version directory both exist; preserve and resolve them before migration: $destination" }
    $entries = @(Get-ChildItem -LiteralPath $group.FullName -Force | Where-Object {
        -not ($_.PSIsContainer -and (Test-Path -LiteralPath (Join-Path $_.FullName 'package.yaml')))
    })
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    try {
        foreach ($entry in $entries) {
            Assert-PackagePath $entry.FullName
            Move-Item -LiteralPath $entry.FullName -Destination (Join-Path $staging $entry.Name)
        }
        Move-Item -LiteralPath $staging -Destination $destination
        Write-Output "Migrated $($group.Name) -> $($group.Name)-$($release.Version)"
    }
    catch {
        foreach ($entry in Get-ChildItem -LiteralPath $staging -Force) {
            Move-Item -LiteralPath $entry.FullName -Destination (Join-Path $group.FullName $entry.Name)
        }
        [IO.Directory]::Delete($staging, $false)
        throw
    }
}
