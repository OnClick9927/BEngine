[CmdletBinding()]
param([Parameter(Mandatory)][string]$StageOutput)

$ErrorActionPreference = 'Stop'
$destinationRoot = Join-Path ([IO.Path]::GetFullPath($StageOutput)) 'BuildPlatform'
[void][IO.Directory]::CreateDirectory($destinationRoot)

# Target SDK backends compile on the target's toolchain, independently of the
# desktop authoring tools. Preserve the same paths in the installed SDK.
$directories = @('Resources', 'Native')
$files = @((Get-Item -LiteralPath (Join-Path $PSScriptRoot 'PlatformNative.props')))
foreach ($directory in $directories) {
    $path = Join-Path $PSScriptRoot $directory
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { continue }
    $files += @(Get-ChildItem -LiteralPath $path -File -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -notlike '*.tmp'
    })
}
$sourcePrefix = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
foreach ($file in $files) {
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Platform sources must be ordinary files: $($file.FullName)"
    }
    $relative = $file.FullName.Substring($sourcePrefix.Length)
    $destination = Join-Path $destinationRoot $relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
Write-Output "BENGINE_PLATFORM_SOURCES_EXPORTED|files=$($files.Count)"

# Mobile backend projects are part of the platform source tree and are also
# shipped as target-toolchain templates under BuildPlatform.
foreach ($name in @('Android', 'iOS')) {
    $source = Join-Path $PSScriptRoot "$name/Editor/Native/BEngine.$name.Native"
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Missing native platform source: $source"
    }
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -notlike '*.tmp' }) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Native platform source cannot be a link: $($file.FullName)" }
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        $destination = Join-Path (Join-Path $destinationRoot "BEngine.$name.Native") $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
