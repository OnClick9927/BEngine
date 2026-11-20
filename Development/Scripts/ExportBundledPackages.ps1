[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$PackagesRoot,
    [Parameter(Mandatory)][string]$EngineVersion,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$packages = [Collections.Generic.List[object]]::new()
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'Packages') -Directory | Sort-Object Name) {
    $definition = Join-Path $directory.FullName 'package.yaml'
    if (-not (Test-Path -LiteralPath $definition -PathType Leaf)) { continue }
    $text = [IO.File]::ReadAllText($definition)
    $idMatch = [regex]::Matches($text, '(?m)^id:\s*([^\r\n#]+)')
    if ($idMatch.Count -ne 1) { throw "Missing or duplicate package id: $definition" }
    $id = $idMatch[0].Groups[1].Value.Trim().Trim([char]34, [char]39)
    if (-not $ids.Add($id)) { throw "Duplicate bundled package id: $id" }
    $release = & (Join-Path $PSScriptRoot 'GetPackageRelease.ps1') -DefinitionPath $definition
    $relativePath = "$($directory.Name)/$($directory.Name)-$($release.Version)/package.yaml"
    $published = Join-Path $PackagesRoot $relativePath
    if (-not (Test-Path -LiteralPath $published -PathType Leaf) -or
        (Get-FileHash -LiteralPath $definition).Hash -ne (Get-FileHash -LiteralPath $published).Hash) {
        throw "Bundled package was not exported from the current source: $published"
    }
    $packages.Add([ordered]@{ id = $id; version = $release.Version; path = $relativePath })
}
$manifest = [ordered]@{
    format = 'BEngine.BundledPackages'
    version = 1
    engineVersion = $EngineVersion
    packages = @($packages.ToArray())
}
$destination = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
[IO.File]::WriteAllText($destination, ($manifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))
Write-Output "BENGINE_BUNDLED_PACKAGES_EXPORTED|engine=$EngineVersion|packages=$($packages.Count)"
