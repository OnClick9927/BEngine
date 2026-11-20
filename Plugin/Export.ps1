[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [string]$InstallationRoot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repositoryRoot
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $workspaceRoot 'Temp/Plugin/Packages' }
if (-not $InstallationRoot) { $InstallationRoot = Join-Path $repositoryRoot 'Engine' }
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$InstallationRoot = [IO.Path]::GetFullPath($InstallationRoot)
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $InstallationRoot 'Plugin'))
$pluginPrefix = $pluginRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar

function Assert-UnlinkedPath([string]$Path) {
    # Checking every existing ancestor also rejects a linked installation root.
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        $entry = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Plugin export cannot use a linked path: $current"
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Assert-UnlinkedTree([string]$Path) {
    Assert-UnlinkedPath $Path
    $entry = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $entry -or -not $entry.PSIsContainer) { return }
    # Do not recurse through an unknown tree before verifying its links.
    $directories = [Collections.Generic.Stack[string]]::new()
    $directories.Push($entry.FullName)
    while ($directories.Count -ne 0) {
        foreach ($child in Get-ChildItem -LiteralPath $directories.Pop() -Force) {
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Plugin export cannot replace a tree containing a linked path: $($child.FullName)"
            }
            if ($child.PSIsContainer) { $directories.Push($child.FullName) }
        }
    }
}

function Get-OwnedPath([string]$Name) {
    $path = [IO.Path]::GetFullPath((Join-Path $pluginRoot $Name))
    if (-not $path.StartsWith($pluginPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetDirectoryName($path) -ne $pluginRoot) {
        throw 'Plugin path escaped its owned installation directory.'
    }
    return $path
}

function Assert-Destination($Item) {
    Assert-UnlinkedTree $Item.Destination
    $entry = Get-Item -LiteralPath $Item.Destination -Force -ErrorAction SilentlyContinue
    if ($null -ne $entry -and $entry.PSIsContainer -ne $Item.IsDirectory) {
        throw "Plugin output has the wrong file type: $($Item.Destination)"
    }
}

function Move-ExportItem([string]$Source, [string]$Destination, [bool]$IsDirectory) {
    if ($IsDirectory) { [IO.Directory]::Move($Source, $Destination) }
    else { [IO.File]::Move($Source, $Destination) }
}

$inventory = @()
$items = @()
Assert-UnlinkedPath $PackageDirectory
Assert-UnlinkedPath $pluginRoot
if ($PackageDirectory.Equals($pluginRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $PackageDirectory.StartsWith($pluginPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Plugin package inputs must be outside the installed Plugin directory.'
}

# Validate every input and every owned output before creating staging or moving
# installed content. Unrelated third-party plugin directories are not traversed.
foreach ($hostName in @('VSCode', 'VisualStudio')) {
    $inputDirectory = Join-Path $PackageDirectory $hostName
    Assert-UnlinkedPath $inputDirectory
    $files = @(Get-ChildItem -LiteralPath $inputDirectory -File -Force | Where-Object { $_.Extension -in @('.vsix', '.md', '.json') })
    foreach ($file in $files) { Assert-UnlinkedPath $file.FullName }
    $packages = @($files | Where-Object { $_.Extension -eq '.vsix' })
    if ($packages.Count -ne 1) { throw "Expected one $hostName VSIX in $inputDirectory." }
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        if ($null -eq $archive.GetEntry('extension.vsixmanifest') -or
            $null -eq $archive.GetEntry('[Content_Types].xml')) { throw "Invalid VSIX: $($packages[0].FullName)" }
        $reader = [IO.StreamReader]::new($archive.GetEntry('extension.vsixmanifest').Open())
        try { [xml]$packageManifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $packageVersion = [string]$packageManifest.PackageManifest.Metadata.Identity.Version
        if ([string]::IsNullOrWhiteSpace($packageVersion)) { throw 'VSIX version is missing.' }
    }
    finally { $archive.Dispose() }
    $inventory += [pscustomobject]@{
        host = $hostName
        file = "$hostName/$($packages[0].Name)"
        version = $packageVersion
        size = $packages[0].Length
        sha256 = (Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $items += [pscustomobject]@{
        Name = $hostName; Destination = Get-OwnedPath $hostName; IsDirectory = $true
        SourceFiles = $files; Stage = $null; Backup = $null; PreviousMoved = $false; Published = $false
    }
}
foreach ($name in @('plugins.json', 'README.md', 'Protocol.md')) {
    $source = $null
    if ($name -ne 'plugins.json') {
        $source = Join-Path $PSScriptRoot $name
        Assert-UnlinkedPath $source
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Plugin documentation is missing: $source" }
    }
    $items += [pscustomobject]@{
        Name = $name; Destination = Get-OwnedPath $name; IsDirectory = $false
        SourceFiles = @($source); Stage = $null; Backup = $null; PreviousMoved = $false; Published = $false
    }
}
foreach ($item in $items) { Assert-Destination $item }

[void][IO.Directory]::CreateDirectory($pluginRoot)
Assert-UnlinkedPath $pluginRoot
$lockPath = Get-OwnedPath '.export.lock'
Assert-UnlinkedPath $lockPath
# DeleteOnClose removes only this lock file and serializes concurrent exporters.
$exportLock = [IO.FileStream]::new($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite,
    [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
$transaction = $null
$canClean = $true
try {
    foreach ($item in $items) { Assert-Destination $item }
    $transaction = Get-OwnedPath ('.export.' + [Guid]::NewGuid().ToString('N') + '.staging')
    $stageRoot = Join-Path $transaction 'Stage'
    $backupRoot = Join-Path $transaction 'Backup'
    [void][IO.Directory]::CreateDirectory($stageRoot)
    [void][IO.Directory]::CreateDirectory($backupRoot)
    $manifest = [ordered]@{
        schemaVersion = 1
        enginePackage = 'com.bengine.ide-integration'
        protocolVersion = 1
        plugins = $inventory
    }
    foreach ($item in $items) {
        $item.Stage = Join-Path $stageRoot $item.Name
        $item.Backup = Join-Path $backupRoot $item.Name
        if ($item.IsDirectory) {
            [void][IO.Directory]::CreateDirectory($item.Stage)
            foreach ($file in $item.SourceFiles) {
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $item.Stage $file.Name)
            }
            $package = $inventory | Where-Object { $_.host -eq $item.Name }
            $stagedPackage = Join-Path $stageRoot $package.file
            if ((Get-FileHash -LiteralPath $stagedPackage -Algorithm SHA256).Hash.ToLowerInvariant() -ne $package.sha256) {
                throw "Plugin package changed while staging: $($item.Name)"
            }
        }
        elseif ($item.Name -eq 'plugins.json') {
            [IO.File]::WriteAllText($item.Stage, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
        }
        else { Copy-Item -LiteralPath $item.SourceFiles[0] -Destination $item.Stage }
    }

    # Keep all five backups until the complete publication has succeeded.
    try {
        foreach ($item in $items) {
            Assert-Destination $item
            if (Test-Path -LiteralPath $item.Destination) {
                Move-ExportItem $item.Destination $item.Backup $item.IsDirectory
                $item.PreviousMoved = $true
            }
            Move-ExportItem $item.Stage $item.Destination $item.IsDirectory
            $item.Published = $true
        }
    }
    catch {
        $publicationError = $_
        $rollbackErrors = [Collections.Generic.List[string]]::new()
        for ($index = $items.Count - 1; $index -ge 0; $index--) {
            $item = $items[$index]
            try {
                if ($item.Published) {
                    Assert-Destination $item
                    Move-ExportItem $item.Destination $item.Stage $item.IsDirectory
                    $item.Published = $false
                }
                if ($item.PreviousMoved) {
                    Move-ExportItem $item.Backup $item.Destination $item.IsDirectory
                    $item.PreviousMoved = $false
                }
            }
            catch { $rollbackErrors.Add("$($item.Name): $($_.Exception.Message)") }
        }
        if ($rollbackErrors.Count -ne 0) {
            $canClean = $false
            throw "Plugin publication failed: $($publicationError.Exception.Message) Rollback needs recovery from '$transaction': $($rollbackErrors -join '; ')"
        }
        throw $publicationError
    }
}
finally {
    try {
        if ($canClean -and $transaction -and (Test-Path -LiteralPath $transaction)) {
            # Recheck the exact, direct child and its tree before recursive cleanup.
            if ((Get-OwnedPath ([IO.Path]::GetFileName($transaction))) -ne $transaction) { throw 'Plugin cleanup escaped installation.' }
            Assert-UnlinkedTree $transaction
            Remove-Item -LiteralPath $transaction -Recurse -Force
        }
    }
    catch { Write-Warning "Plugin transaction cleanup was retained at '$transaction': $($_.Exception.Message)" }
    finally { $exportLock.Dispose() }
}
Write-Output "BENGINE_PLUGIN_EXPORTED|$pluginRoot"
