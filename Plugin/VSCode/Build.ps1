[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ArtifactsDirectory
)

$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$artifacts = [IO.Path]::GetFullPath($ArtifactsDirectory)
$source = [IO.Path]::GetFullPath($PSScriptRoot)
if ($artifacts.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
    $artifacts.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ArtifactsDirectory must be outside the plugin source directory. Use the repository Temp directory.'
}
[void][IO.Directory]::CreateDirectory($output)
[void][IO.Directory]::CreateDirectory($artifacts)
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$toolTemp = Join-Path $artifacts 'SystemTemp'
[void][IO.Directory]::CreateDirectory($toolTemp)
try {
$env:TEMP = $toolTemp
$env:TMP = $toolTemp
$tools = Join-Path $artifacts 'Tools'
$stage = Join-Path $artifacts 'Package'
$vsce = Join-Path $tools 'node_modules/@vscode/vsce/vsce'
$node = (Get-Command node -ErrorAction Stop).Source
if ([version]((& $node --version).TrimStart('v')) -lt [version]'22.0.0') {
    $node = Join-Path $tools 'node_modules/node/bin/node.exe'
    if (-not $IsWindows -and [System.Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        $node = Join-Path $tools 'node_modules/node/bin/node'
    }
}
if (-not (Test-Path -LiteralPath $vsce) -or -not (Test-Path -LiteralPath $node)) {
    Write-Output 'BENGINE_VSCODE_BUILD|Preparing pinned VSIX packaging tools in Temp'
    & npm install --prefix $tools --cache (Join-Path $artifacts 'NpmCache') --no-audit --no-fund node@22.16.0 @vscode/vsce@4.1.0 *> (Join-Path $artifacts 'ToolsInstall.log')
    if ($LASTEXITCODE -ne 0) { throw "VSIX packaging tool restore failed. See $(Join-Path $artifacts 'ToolsInstall.log')" }
}
if (Test-Path -LiteralPath $stage) {
    $stageFullPath = [IO.Path]::GetFullPath($stage)
    $artifactsPrefix = $artifacts.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $stageFullPath.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        ((Get-Item -LiteralPath $stage).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Refusing to clear a package staging path outside the artifacts directory or through a link.'
    }
    Remove-Item -LiteralPath $stageFullPath -Recurse -Force
}
[void][IO.Directory]::CreateDirectory($stage)
foreach ($name in @('src','snippets','media','package.json','package.nls.json','package.nls.zh-cn.json','README.md','CHANGELOG.md','LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $stage -Recurse -Force
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $stage 'src') -Filter '*.js' -File) {
    & $node --check $file.FullName
    if ($LASTEXITCODE -ne 0) { throw "Invalid JavaScript: $($file.Name)" }
}
$version = (Get-Content -LiteralPath (Join-Path $stage 'package.json') -Raw | ConvertFrom-Json).version
$vsix = Join-Path $output "BEngine.VSCode-$version.vsix"
Push-Location $stage
try {
    & $node $vsce package --no-dependencies --out $vsix *> (Join-Path $artifacts 'Package.log')
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $artifacts 'Package.log') -Tail 30; throw 'VSIX packaging failed.' }
} finally { Pop-Location }
$metadata = [pscustomobject]@{
    id = 'bengine.bengine-tools'
    version = $version
    file = [IO.Path]::GetFileName($vsix)
    sha256 = (Get-FileHash -LiteralPath $vsix -Algorithm SHA256).Hash
    length = (Get-Item -LiteralPath $vsix).Length
    dependency = 'ms-dotnettools.csharp'
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'metadata.json') -Encoding utf8
# Keep upgrades repeatable when this output directory already contains an older
# release. Only remove archives whose embedded identity belongs to this extension;
# similarly named third-party, malformed or linked files are preserved.
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($previous in Get-ChildItem -LiteralPath $output -Filter 'BEngine.VSCode-*.vsix' -File) {
    if ($previous.FullName.Equals($vsix, [StringComparison]::OrdinalIgnoreCase) -or
        $previous.Name -notmatch '^BEngine\.VSCode-\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\.vsix$' -or
        ($previous.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
    $owned = $false
    $archive = $null
    $manifestReader = $null
    $manifestStream = $null
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($previous.FullName)
        $entry = $archive.GetEntry('extension.vsixmanifest')
        if ($null -ne $entry -and $entry.Length -le 65536) {
            $manifestStream = $entry.Open()
            $xmlSettings = [Xml.XmlReaderSettings]::new()
            $xmlSettings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
            $xmlSettings.XmlResolver = $null
            $manifestReader = [Xml.XmlReader]::Create($manifestStream, $xmlSettings)
            $manifestDocument = [Xml.XmlDocument]::new()
            $manifestDocument.XmlResolver = $null
            $manifestDocument.Load($manifestReader)
            $identity = $manifestDocument.SelectSingleNode('/*[local-name()="PackageManifest"]/*[local-name()="Metadata"]/*[local-name()="Identity"]')
            $owned = $null -ne $identity -and $identity.GetAttribute('Id') -eq 'bengine-tools' -and
                $identity.GetAttribute('Publisher') -eq 'bengine'
        }
    } catch {
        Write-Output "BENGINE_VSCODE_BUILD|Preserving unrecognized archive $($previous.Name)"
    } finally {
        if ($null -ne $manifestReader) { $manifestReader.Dispose() }
        if ($null -ne $manifestStream) { $manifestStream.Dispose() }
        if ($null -ne $archive) { $archive.Dispose() }
    }
    if ($owned) {
        Remove-Item -LiteralPath $previous.FullName -Force
        Write-Output "BENGINE_VSCODE_BUILD|Removed previous release $($previous.Name)"
    }
}
Write-Output "BENGINE_VSCODE_BUILD_OK|$vsix|$($metadata.length)|$($metadata.sha256)"
} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
