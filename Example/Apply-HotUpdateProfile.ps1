[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('V1', 'V2')]
    [string]$Profile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$exampleRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$projectFile = Join-Path $exampleRoot 'Project.yaml'
if (-not [System.IO.File]::Exists($projectFile)) {
    throw "Apply-HotUpdateProfile.ps1 must remain in the Example project root."
}

$assetsRoot = [System.IO.Path]::GetFullPath((Join-Path $exampleRoot 'Assets'))
$profileRoot = [System.IO.Path]::GetFullPath((Join-Path $assetsRoot "Editor/HotUpdateDemo/$Profile"))
$scriptsRoot = [System.IO.Path]::GetFullPath((Join-Path $assetsRoot 'Scripts'))
$resourcesRoot = [System.IO.Path]::GetFullPath((Join-Path $assetsRoot 'Resources/HotUpdate'))

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    $volumeRoot = [System.IO.Path]::GetPathRoot($fullRoot)
    while ($fullRoot.Length -gt $volumeRoot.Length -and
           ($fullRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar) -or
            $fullRoot.EndsWith([System.IO.Path]::AltDirectorySeparatorChar))) {
        $fullRoot = $fullRoot.Substring(0, $fullRoot.Length - 1)
    }
    if (-not $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Profile output escapes the expected root: '$fullPath'."
    }
    return $fullPath
}

function Copy-ProfileTemplate {
    param(
        [Parameter(Mandatory = $true)][string]$SourceName,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$DestinationRoot
    )

    $source = Assert-ChildPath -Path (Join-Path $profileRoot $SourceName) -Root $profileRoot
    if (-not [System.IO.File]::Exists($source)) {
        throw "HotUpdate profile template is missing: '$source'."
    }
    $target = Assert-ChildPath -Path $Destination -Root $DestinationRoot
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}

function Convert-HexColor {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value -notmatch '^[0-9A-Fa-f]{6}$') {
        throw "Badge color '$Value' must contain exactly six hexadecimal digits."
    }
    return [System.Drawing.ColorTranslator]::FromHtml("#$Value")
}

function Write-BadgePng {
    param(
        [Parameter(Mandatory = $true)][string]$DefinitionPath,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    try {
        Add-Type -AssemblyName System.Drawing.Common -ErrorAction Stop
    }
    catch {
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
    }
    $definition = Get-Content -Raw -LiteralPath $DefinitionPath | ConvertFrom-StringData
    foreach ($key in @('Title', 'Subtitle', 'Background', 'Panel', 'Accent', 'Foreground')) {
        if (-not $definition.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($definition[$key])) {
            throw "Badge profile '$DefinitionPath' is missing '$key'."
        }
    }

    $bitmap = [System.Drawing.Bitmap]::new(
        512, 192, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $panelBrush = $null
    $accentBrush = $null
    $foregroundBrush = $null
    $titleFont = $null
    $subtitleFont = $null
    $format = $null
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear((Convert-HexColor $definition.Background))
        $panelBrush = [System.Drawing.SolidBrush]::new((Convert-HexColor $definition.Panel))
        $accentBrush = [System.Drawing.SolidBrush]::new((Convert-HexColor $definition.Accent))
        $foregroundBrush = [System.Drawing.SolidBrush]::new((Convert-HexColor $definition.Foreground))
        $graphics.FillRectangle($panelBrush, 12, 12, 488, 168)
        $graphics.FillRectangle($accentBrush, 12, 12, 14, 168)
        $graphics.FillRectangle($accentBrush, 42, 145, 428, 4)

        foreach ($x in @(70, 194, 318, 442)) {
            $graphics.FillEllipse($accentBrush, $x - 8, 141, 16, 16)
        }

        $titleFont = [System.Drawing.Font]::new(
            [System.Drawing.FontFamily]::GenericSansSerif, 38,
            [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $subtitleFont = [System.Drawing.Font]::new(
            [System.Drawing.FontFamily]::GenericSansSerif, 17,
            [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        $format = [System.Drawing.StringFormat]::new()
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString($definition.Title, $titleFont, $foregroundBrush,
            [System.Drawing.RectangleF]::new(36, 30, 448, 58), $format)
        $graphics.DrawString($definition.Subtitle, $subtitleFont, $foregroundBrush,
            [System.Drawing.RectangleF]::new(36, 91, 448, 32), $format)

        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Destination)) | Out-Null
        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        if ($null -ne $format) { $format.Dispose() }
        if ($null -ne $subtitleFont) { $subtitleFont.Dispose() }
        if ($null -ne $titleFont) { $titleFont.Dispose() }
        if ($null -ne $foregroundBrush) { $foregroundBrush.Dispose() }
        if ($null -ne $accentBrush) { $accentBrush.Dispose() }
        if ($null -ne $panelBrush) { $panelBrush.Dispose() }
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

[System.IO.Directory]::CreateDirectory($scriptsRoot) | Out-Null
[System.IO.Directory]::CreateDirectory($resourcesRoot) | Out-Null
Copy-ProfileTemplate -SourceName 'HotUpdateShowcase.cs.template' `
    -Destination (Join-Path $scriptsRoot 'HotUpdateShowcase.cs') -DestinationRoot $scriptsRoot
Copy-ProfileTemplate -SourceName 'release.txt.template' `
    -Destination (Join-Path $resourcesRoot 'ReleaseInfo.txt') -DestinationRoot $resourcesRoot
Copy-ProfileTemplate -SourceName 'Release.shader.template' `
    -Destination (Join-Path $resourcesRoot 'Release.shader') -DestinationRoot $resourcesRoot

$legacyReleasePath = Assert-ChildPath -Path (Join-Path $resourcesRoot 'release.txt') -Root $resourcesRoot
if ([System.IO.File]::Exists($legacyReleasePath)) {
    [System.IO.File]::Delete($legacyReleasePath)
}

$badgeDefinition = Assert-ChildPath -Path (Join-Path $profileRoot 'badge.profile') -Root $profileRoot
$badgePath = Assert-ChildPath -Path (Join-Path $resourcesRoot 'ReleaseBadge.png') -Root $resourcesRoot
Write-BadgePng -DefinitionPath $badgeDefinition -Destination $badgePath

$scriptHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $scriptsRoot 'HotUpdateShowcase.cs')).Hash.ToLowerInvariant()
$textHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $resourcesRoot 'ReleaseInfo.txt')).Hash.ToLowerInvariant()
$shaderHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $resourcesRoot 'Release.shader')).Hash.ToLowerInvariant()
$imageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $badgePath).Hash.ToLowerInvariant()
Write-Output "BENGINE_HOTUPDATE_PROFILE_APPLIED|profile=$($Profile.ToLowerInvariant())|script=$scriptHash|text=$textHash|shader=$shaderHash|image=$imageHash"
