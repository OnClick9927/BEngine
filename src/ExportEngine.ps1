[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$IncrementalPackages
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outputRoot = Join-Path $repositoryRoot 'Engine\Engine'
$engineVersion = & (Join-Path $PSScriptRoot 'GetEngineVersion.ps1')
$engineVersionsRoot = Join-Path $outputRoot 'BEgine'
$engineOutput = Join-Path $engineVersionsRoot "BEgine-$engineVersion"
$stageOutput = Join-Path $engineVersionsRoot ".BEgine-$engineVersion.publish-staging"
$backupOutput = Join-Path $engineVersionsRoot ".BEgine-$engineVersion.publish-backup"
$tempRoot = Join-Path $repositoryRoot 'Temp'
$hubOutput = Join-Path $outputRoot 'BEngine.Hub.exe'
$legacyHubOutput = Join-Path $outputRoot 'Hub'
$hubStageOutput = Join-Path $outputRoot '.Hub.publish-staging'
$hubBackupOutput = Join-Path $outputRoot '.Hub.publish-backup'
$hubBackupFile = Join-Path $outputRoot '.BEngine.Hub.publish-backup'
$launcherProject = Join-Path $PSScriptRoot 'Hub\BEngine.Launcher\BEngine.Launcher.csproj'
$editorProject = Join-Path $PSScriptRoot 'Hosts\BEngine.Editor.Host\BEngine.Editor.Host.csproj'
$playerProject = Join-Path $PSScriptRoot 'Packages\Player\BEngine.Player\BEngine.Player.csproj'
$coreResources = Join-Path $PSScriptRoot 'Core\Resources'
$coreEditor = Join-Path $PSScriptRoot 'Core\Editor'
$playerBuildHosts = Join-Path $PSScriptRoot 'Platform\Hosts'
$engineDefines = Join-Path $PSScriptRoot 'BEngine.Defines.targets'
$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
# Host applications are published as compressed .NET single-file bundles. The
# bundle self-extracts assemblies/native assets at runtime.
$compactHostPublishArguments = @(
    '-p:PublishSingleFile=true',
    '-p:IncludeAllContentForSelfExtract=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

function Assert-ExportPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $prefix = [IO.Path]::GetFullPath($outputRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($Path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Export path escaped Engine: $Path"
    }
}

function Assert-RootExportPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $prefix = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($Path).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Export path escaped repository root: $Path"
    }
}

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [int]$Attempts = 8,
        [int]$DelayMilliseconds = 250
    )

    Assert-ExportPath $Path
    if (-not (Test-Path -LiteralPath $Path)) { return }
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq $Attempts) { throw }
            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}

function Move-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [int]$Attempts = 20,
        [int]$DelayMilliseconds = 500
    )

    Assert-ExportPath $Source
    Assert-ExportPath $Destination
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            [System.IO.Directory]::Move($Source, $Destination)
            return
        }
        catch {
            if ($attempt -eq $Attempts) { throw }
            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}

function Sync-DirectoryWithRobocopy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not $runningOnWindows) {
        throw "In-place export fallback is only available on Windows: '$Destination'."
    }
    Assert-ExportPath $Source
    Assert-ExportPath $Destination
    # A loaded/mapped DLL cannot be overwritten even when only its timestamp
    # changed. Compare bytes before mirroring and align staging timestamps for
    # identical files; genuinely changed locked files still fail publication.
    foreach ($sourceFile in Get-ChildItem -LiteralPath $Source -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($Source, $sourceFile.FullName)
        $destinationFile = Join-Path $Destination $relative
        if (-not [IO.File]::Exists($destinationFile)) { continue }
        $existing = Get-Item -LiteralPath $destinationFile
        if ($existing.Length -ne $sourceFile.Length -or $existing.LastWriteTimeUtc -eq $sourceFile.LastWriteTimeUtc) { continue }
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destinationFile -Algorithm SHA256).Hash
        if ($sourceHash -eq $destinationHash) {
            [IO.File]::SetLastWriteTimeUtc($sourceFile.FullName, $existing.LastWriteTimeUtc)
        }
    }
    $robocopy = Join-Path $env:SystemRoot 'System32\robocopy.exe'
    & $robocopy $Source $Destination /MIR /COPY:DAT /DCOPY:DAT /R:8 /W:1 /XJ `
        /NFL /NDL /NJH /NJS /NP
    $exitCode = $LASTEXITCODE
    if ($exitCode -gt 7) {
        throw "In-place engine export failed with robocopy exit code $exitCode."
    }
    # Robocopy uses nonzero success codes; do not leak them as script failures.
    $global:LASTEXITCODE = 0
}

foreach ($path in @($engineOutput, $stageOutput, $backupOutput, $legacyHubOutput, $hubStageOutput,
        $hubBackupOutput, $hubBackupFile)) {
    Assert-ExportPath $path
}
Assert-ExportPath $hubOutput

$hubExecutablePath = [IO.Path]::GetFullPath($hubOutput)
$legacyRootHubPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'BEngine.Launcher.exe'))
$legacyHubPrefix = [IO.Path]::GetFullPath($legacyHubOutput) + [IO.Path]::DirectorySeparatorChar
$runningHub = @(Get-Process -Name 'BEngine.Hub', 'BEngine.Launcher' -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and ([IO.Path]::GetFullPath($_.Path).Equals($hubExecutablePath,
            [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFullPath($_.Path).Equals($legacyRootHubPath,
            [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFullPath($_.Path).StartsWith($legacyHubPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $_.Path.StartsWith(([IO.Path]::GetFullPath($engineVersionsRoot) + [IO.Path]::DirectorySeparatorChar),
            [StringComparison]::OrdinalIgnoreCase))
})
if ($runningHub.Count -gt 0) {
    throw 'BEngine Hub is running from the export directory. Close it before exporting.'
}

$enginePrefix = [System.IO.Path]::GetFullPath($engineVersionsRoot).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$runningEditor = @(Get-Process -Name 'BEngine.Editor', 'BEngine.Editor.Host' -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Path -and [System.IO.Path]::GetFullPath($_.Path).StartsWith(
            $enginePrefix, [System.StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $false
    }
})
if ($runningEditor.Count -gt 0) {
    $processIds = ($runningEditor | ForEach-Object Id) -join ', '
    throw "BEngine Editor is running from Engine/Engine/BEgine (PID: $processIds). Save your work and close it before exporting."
}

if (Test-Path -LiteralPath $stageOutput) {
    Remove-DirectoryWithRetry -Path $stageOutput
}
if (Test-Path -LiteralPath $backupOutput) {
    if (-not (Test-Path -LiteralPath $engineOutput)) {
        Move-DirectoryWithRetry -Source $backupOutput -Destination $engineOutput
    }
    else {
        Remove-DirectoryWithRetry -Path $backupOutput
    }
}

if (Test-Path -LiteralPath $hubStageOutput) { Remove-DirectoryWithRetry -Path $hubStageOutput }
if (Test-Path -LiteralPath $hubBackupOutput) {
    Remove-DirectoryWithRetry -Path $hubBackupOutput
}
if (Test-Path -LiteralPath $hubBackupFile) { Remove-Item -LiteralPath $hubBackupFile -Force }
New-Item -ItemType Directory -Path $stageOutput, $hubStageOutput | Out-Null

$packageOutput = [System.IO.Path]::GetFullPath((Join-Path $outputRoot 'Packages'))
& (Join-Path $PSScriptRoot 'MigratePackageLayout.ps1') -PackagesRoot $packageOutput
$packageExportProjects = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Packages') -Filter '*.csproj' -File -Recurse |
    Where-Object { Select-String -LiteralPath $_.FullName -Pattern '<BEnginePackageExportOwner>\s*true\s*</BEnginePackageExportOwner>' -Quiet } |
    Sort-Object FullName
function Test-PackageExportCurrent([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Destination)) { return $false }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        $relative = $file.FullName.Substring($Source.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1)
        if ($relative -notmatch '^(BEngine\.[^\\]+\\.*\.cs|BEngine\.[^\\]+\\[^\\]+\.asmdef\.yaml|Resources\\.*|Editor\\.*|package\.yaml)$' -or $relative -match '\\(bin|obj)\\') { continue }
        [void]$expected.Add($relative)
        $target = Join-Path $Destination $relative
        if (-not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { return $false }
    }
    foreach ($file in Get-ChildItem -LiteralPath $Destination -Recurse -File) {
        if (-not $expected.Contains($file.FullName.Substring($Destination.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1))) { return $false }
    }
    return $expected.Count -gt 0
}
foreach ($project in $packageExportProjects) {
    $packageName = $project.Directory.Parent.Name
    $release = & (Join-Path $PSScriptRoot 'GetPackageRelease.ps1') -DefinitionPath (Join-Path $project.Directory.Parent.FullName 'package.yaml')
    $packageGroup = [IO.Path]::GetFullPath((Join-Path $packageOutput $packageName))
    $packageDestination = [IO.Path]::GetFullPath((Join-Path $packageGroup "$packageName-$($release.Version)"))
    if ($IncrementalPackages -and (Test-PackageExportCurrent $project.Directory.Parent.FullName $packageDestination)) {
        Write-Output "BENGINE_PACKAGE_EXPORT_CURRENT|$packageName|$($release.Version)"
        continue
    }
    # Publish core resources once from staging; package references must not replace the live Editor tree.
    & dotnet build $project.FullName -c $Configuration -m:1 /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEngineStagePackageOnly=true -v:q
    if ($LASTEXITCODE -ne 0) { throw "Package export failed: $($project.Name)." }
    $packageName = $project.Directory.Parent.Name
    $release = & (Join-Path $PSScriptRoot 'GetPackageRelease.ps1') -DefinitionPath (Join-Path $project.Directory.Parent.FullName 'package.yaml')
    $packageStage = [IO.Path]::GetFullPath((Join-Path $packageOutput ".$packageName.staging"))
    $packageGroup = [IO.Path]::GetFullPath((Join-Path $packageOutput $packageName))
    $packageDestination = [IO.Path]::GetFullPath((Join-Path $packageGroup "$packageName-$($release.Version)"))
    if ((Split-Path -Parent $packageStage) -ne $packageOutput -or
        (Split-Path -Parent $packageGroup) -ne $packageOutput -or
        (Split-Path -Parent $packageDestination) -ne $packageGroup) { throw 'Invalid package export path.' }
    [IO.Directory]::CreateDirectory($packageGroup) | Out-Null
    if ($runningOnWindows) {
        # Preserve unchanged files that source compilation/indexing may currently have open.
        Sync-DirectoryWithRobocopy -Source $packageStage -Destination $packageDestination
    }
    else {
        Remove-DirectoryWithRetry -Path $packageDestination
        Copy-Item -LiteralPath $packageStage -Destination $packageDestination -Recurse
    }
    Remove-DirectoryWithRetry -Path $packageStage
}
# A package dependency can stage its sources again after its own export completed.
foreach ($packageSource in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Packages') -Directory) {
    $dependencyStage = [IO.Path]::GetFullPath((Join-Path $packageOutput ".$($packageSource.Name).staging"))
    if ((Split-Path -Parent $dependencyStage) -ne $packageOutput) { throw 'Invalid dependency staging path.' }
    Remove-DirectoryWithRetry -Path $dependencyStage
}
# Project packages have no export-owner assembly: publish their source content directly.
foreach ($projectPackage in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Packages') -Directory) {
    $definitionPath = Join-Path $projectPackage.FullName 'package.yaml'
    if (-not (Test-Path -LiteralPath $definitionPath)) { continue }
    if (-not (Select-String -LiteralPath $definitionPath -Pattern '^type:\s*project\s*$' -Quiet)) { continue }
    $release = & (Join-Path $PSScriptRoot 'GetPackageRelease.ps1') -DefinitionPath $definitionPath
    $packageGroup = [IO.Path]::GetFullPath((Join-Path $packageOutput $projectPackage.Name))
    $destination = [IO.Path]::GetFullPath((Join-Path $packageGroup "$($projectPackage.Name)-$($release.Version)"))
    if ((Split-Path -Parent $packageGroup) -ne $packageOutput -or
        (Split-Path -Parent $destination) -ne $packageGroup) { throw 'Invalid project package output path.' }
    Remove-DirectoryWithRetry -Path $destination
    foreach ($file in Get-ChildItem -LiteralPath $projectPackage.FullName -File -Recurse) {
        # GetRelativePath is unavailable in the Windows PowerShell 5.1 fallback used by the batch entry point.
        $relativePath = $file.FullName.Substring($projectPackage.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1)
        $target = Join-Path $destination $relativePath
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
foreach ($retired in @('Media')) {
    $retiredPath = [System.IO.Path]::GetFullPath((Join-Path $packageOutput $retired))
    if ((Split-Path -Parent $retiredPath) -ne $packageOutput) { throw 'Invalid retired package path.' }
    Remove-DirectoryWithRetry -Path $retiredPath
}

& dotnet publish $launcherProject -c $Configuration -r win-x64 --self-contained true -o $hubStageOutput -m:1 `
    /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
    @compactHostPublishArguments
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed with exit code $LASTEXITCODE." }

& dotnet publish $editorProject -c $Configuration -o $stageOutput -m:1 `
    /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
    @compactHostPublishArguments
if ($LASTEXITCODE -ne 0) { throw "Editor publish failed with exit code $LASTEXITCODE." }
# Keep the established executable name for the composed editor host.
Move-Item -LiteralPath (Join-Path $stageOutput "BEngine.Editor.Host.exe") -Destination (Join-Path $stageOutput "BEngine.Editor.exe") -Force

& dotnet publish $playerProject -c $Configuration -o $stageOutput -m:1 `
    /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
    @compactHostPublishArguments
if ($LASTEXITCODE -ne 0) { throw "Player publish failed with exit code $LASTEXITCODE." }

# Symbols are useful while building the engine but are not required by the
# compact self-extracting hosts. Keep BuildRuntime symbols for platform build
# diagnostics and remove only symbols beside the Editor/Player hosts and Hub.
foreach ($symbol in @(Get-ChildItem -LiteralPath $stageOutput -File -Recurse -Filter '*.pdb' |
        Where-Object { $_.FullName -notmatch '[\\/]BuildRuntime[\\/]' })) {
    Remove-Item -LiteralPath $symbol.FullName -Force
}
foreach ($symbol in @(Get-ChildItem -LiteralPath $hubStageOutput -File -Recurse -Filter '*.pdb')) {
    Remove-Item -LiteralPath $symbol.FullName -Force
}

# Build Hosts must not reference the Editor export directory. In particular, mobile AOT
# must never see the Windows-only Editor closure, so publish a clean Player runtime dependency set.
$buildRuntimeOutput = Join-Path $stageOutput 'BuildRuntime'
New-Item -ItemType Directory -Path $buildRuntimeOutput | Out-Null
& dotnet publish $playerProject -c $Configuration -o $buildRuntimeOutput -m:1 `
    /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false
if ($LASTEXITCODE -ne 0) { throw "BuildRuntime publish failed with exit code $LASTEXITCODE." }

# Each variant is compiled independently: removing DLL files from the combined SDK
# would leave unresolved type references inside Desktop and keep compiler native assets.
foreach ($variant in @('OpenGL', 'Vulkan', 'VulkanOffline', 'BothOffline')) {
    $variantOutput = Join-Path $buildRuntimeOutput "Desktop/$variant"
    $variantArtifacts = Join-Path $repositoryRoot "Temp/Engine/GraphicsVariants/$variant"
    $graphics = $variant.Replace('Offline', '')
    $compiler = if ($variant.EndsWith('Offline') -or $graphics -eq 'OpenGL') { 'false' } else { 'true' }
    & dotnet publish $playerProject -c $Configuration -o $variantOutput -m:1 `
        --artifacts-path $variantArtifacts /nodeReuse:false -p:NuGetAudit=false `
        -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
        "-p:BEngineDesktopGraphics=$graphics" "-p:BEngineRuntimeShaderCompiler=$compiler"
    if ($LASTEXITCODE -ne 0) { throw "$variant runtime export failed with exit code $LASTEXITCODE." }
    Set-Content -LiteralPath (Join-Path $variantOutput 'graphics-variant.txt') -Value $variant -Encoding utf8
    if ($graphics -eq 'OpenGL' -and (Test-Path -LiteralPath (Join-Path $variantOutput 'Veldrid.dll'))) {
        throw 'OpenGL runtime unexpectedly contains Veldrid.'
    }
    if ($compiler -eq 'false' -and (Test-Path -LiteralPath (Join-Path $variantOutput 'Veldrid.SPIRV.dll'))) {
        throw "$variant runtime unexpectedly contains the shader compiler."
    }
}

foreach ($platform in @('Android', 'iOS', 'Web')) {
    $platformRuntime = Join-Path $buildRuntimeOutput $platform
    & dotnet publish $playerProject -c $Configuration -o $platformRuntime -m:1 `
        /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
        -p:OutputType=Library "-p:BEnginePlayerPlatform=$platform"
    if ($LASTEXITCODE -ne 0) { throw "$platform runtime export failed with exit code $LASTEXITCODE." }
    foreach ($forbidden in @('BEngine.Editor.dll', 'BEngine.Platform.Desktop.dll', 'glfw3.dll', 'Veldrid.dll')) {
        if (Test-Path -LiteralPath (Join-Path $platformRuntime $forbidden)) {
            throw "$platform runtime contains desktop dependency $forbidden."
        }
    }
}

Copy-Item -LiteralPath $coreResources -Destination (Join-Path $stageOutput 'Resources') -Recurse
Copy-Item -LiteralPath $coreEditor -Destination (Join-Path $stageOutput 'Editor') -Recurse
$buildHostsOutput = Join-Path $stageOutput 'BuildHosts'
New-Item -ItemType Directory -Path $buildHostsOutput -Force | Out-Null
$buildHostRoot = [System.IO.Path]::GetFullPath($playerBuildHosts).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($hostFile in Get-ChildItem -LiteralPath $playerBuildHosts -Recurse -File | Where-Object {
        $_.Name -notlike '*.tmp' -and
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }) {
    $relativeHostPath = [System.IO.Path]::GetFullPath($hostFile.FullName).Substring($buildHostRoot.Length)
    $hostDestination = Join-Path $buildHostsOutput $relativeHostPath
    $hostDestinationParent = Split-Path -Parent $hostDestination
    if (-not (Test-Path -LiteralPath $hostDestinationParent)) {
        New-Item -ItemType Directory -Path $hostDestinationParent | Out-Null
    }
    Copy-Item -LiteralPath $hostFile.FullName -Destination $hostDestination
}
$buildHostsPrefix = [System.IO.Path]::GetFullPath($buildHostsOutput).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($artifactDirectory in @(Get-ChildItem -LiteralPath $buildHostsOutput -Directory -Recurse |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        Sort-Object { $_.FullName.Length } -Descending)) {
    $artifactPath = [System.IO.Path]::GetFullPath($artifactDirectory.FullName)
    if (-not $artifactPath.StartsWith($buildHostsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "BuildHost artifact path escaped the export staging directory: $artifactPath"
    }
    Remove-DirectoryWithRetry -Path $artifactPath
}
foreach ($artifactFile in @(Get-ChildItem -LiteralPath $buildHostsOutput -File -Recurse |
        Where-Object { $_.Name -like '*.tmp' })) {
    $artifactPath = [System.IO.Path]::GetFullPath($artifactFile.FullName)
    if (-not $artifactPath.StartsWith($buildHostsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "BuildHost temporary file escaped the export staging directory: $artifactPath"
    }
    Remove-Item -LiteralPath $artifactPath -Force
}
Copy-Item -LiteralPath $engineDefines -Destination (Join-Path $stageOutput 'BEngine.Defines.targets')

foreach ($required in @('BEngine.Editor.exe', 'BEngine.Player.exe',
        'BEngine.Defines.targets')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageOutput $required))) {
        throw "Engine export is missing $required."
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $hubStageOutput 'BEngine.Launcher.exe'))) {
    throw 'Hub export is missing the staged launcher executable.'
}
if (Test-Path -LiteralPath (Join-Path $stageOutput 'BEngine.Launcher.dll')) {
    throw 'Launcher files must be exported to Hub, not BEgine.'
}

foreach ($requiredRuntime in @('BEngine.dll', 'BEngine.Player.dll', 'BEngine.Platform.DotNet.dll',
        'BEngine.Player.runtimeconfig.json', 'BEngine.Player.deps.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $buildRuntimeOutput $requiredRuntime))) {
        throw "Engine BuildRuntime is missing $requiredRuntime."
    }
}
if (Test-Path -LiteralPath (Join-Path $buildRuntimeOutput 'BEngine.Editor.dll')) {
    throw 'Engine BuildRuntime must not contain BEngine.Editor.dll.'
}
if (Test-Path -LiteralPath (Join-Path $buildRuntimeOutput 'BEngine.Launcher.dll')) {
    throw 'Engine BuildRuntime must not contain BEngine.Launcher.dll.'
}

foreach ($buildHost in @(
        'BEngine.Player.Hosts.props',
        'Desktop\BEngine.Player.DesktopHost.csproj',
        'Android\BEngine.Player.AndroidHost.csproj',
        'iOS\BEngine.Player.iOSHost.csproj',
        'Web\BEngine.Player.WebHost.csproj')) {
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $stageOutput 'BuildHosts') $buildHost))) {
        throw "Engine export is missing Player BuildHost template $buildHost."
    }
}

foreach ($hostName in @('BEngine.Launcher', 'BEngine.Editor.Host', 'BEngine.Player')) {
    $hostOutput = if ($hostName -eq 'BEngine.Launcher') { $hubStageOutput } else { $stageOutput }
    $runtimeConfigPath = Join-Path $hostOutput "$hostName.runtimeconfig.json"
    $depsPath = Join-Path $hostOutput "$hostName.deps.json"
    if (-not (Test-Path -LiteralPath $runtimeConfigPath) -or -not (Test-Path -LiteralPath $depsPath)) {
        $bundleName = if ($hostName -eq 'BEngine.Editor.Host') { 'BEngine.Editor.exe' } else { "$hostName.exe" }
        if (-not (Test-Path -LiteralPath (Join-Path $hostOutput $bundleName))) {
            throw "Engine export is missing .NET host metadata and single-file bundle for $hostName."
        }
        continue
    }

    $runtimeConfig = Get-Content -Raw -LiteralPath $runtimeConfigPath | ConvertFrom-Json
    if ($runtimeConfig.runtimeOptions.tfm -ne 'net10.0') {
        throw "$hostName runtimeconfig targets '$($runtimeConfig.runtimeOptions.tfm)' instead of net10.0."
    }
    $frameworks = if ($null -ne $runtimeConfig.runtimeOptions.frameworks) {
        @($runtimeConfig.runtimeOptions.frameworks)
    }
    else {
        @($runtimeConfig.runtimeOptions.framework)
    }
    if ($frameworks.Count -eq 0 -or @($frameworks | Where-Object {
            -not $_.version.StartsWith('10.', [System.StringComparison]::Ordinal)
        }).Count -gt 0) {
        throw "$hostName runtimeconfig does not exclusively reference .NET 10 frameworks."
    }

    $deps = Get-Content -Raw -LiteralPath $depsPath | ConvertFrom-Json
    if (-not $deps.runtimeTarget.name.StartsWith(
            '.NETCoreApp,Version=v10.0', [System.StringComparison]::Ordinal)) {
        throw "$hostName deps target '$($deps.runtimeTarget.name)' instead of .NET 10."
    }
}

$publishedInPlace = $false
if (Test-Path -LiteralPath $engineOutput) {
    try {
        Move-DirectoryWithRetry -Source $engineOutput -Destination $backupOutput
    }
    catch {
        $reason = $_.Exception
        while ($null -ne $reason.InnerException) { $reason = $reason.InnerException }
        if (-not $runningOnWindows -or
            ($reason -isnot [System.UnauthorizedAccessException] -and
             $reason -isnot [System.IO.IOException])) {
            throw
        }
        Write-Warning 'The existing engine directory is in use; publishing it in place.'
        Sync-DirectoryWithRobocopy -Source $stageOutput -Destination $engineOutput
        $publishedInPlace = $true
    }
}
if ($publishedInPlace) {
    try {
        Remove-DirectoryWithRetry -Path $stageOutput
    }
    catch [System.IO.IOException] {
        Write-Warning "Engine files were published successfully, but a process still holds the staging directory '$stageOutput'. Cleanup will be retried on the next export."
    }
}
else {
    try {
        Move-DirectoryWithRetry -Source $stageOutput -Destination $engineOutput
    }
    catch {
        if (Test-Path -LiteralPath $engineOutput) {
            Remove-DirectoryWithRetry -Path $engineOutput
        }
        if (Test-Path -LiteralPath $backupOutput) {
            Move-DirectoryWithRetry -Source $backupOutput -Destination $engineOutput
        }
        throw
    }
    if (Test-Path -LiteralPath $backupOutput) {
        Remove-DirectoryWithRetry -Path $backupOutput
    }
}

$hadHubOutput = Test-Path -LiteralPath $hubOutput
if ($hadHubOutput) { Move-Item -LiteralPath $hubOutput -Destination $hubBackupFile -Force }
try {
    Move-Item -LiteralPath (Join-Path $hubStageOutput 'BEngine.Launcher.exe') -Destination $hubOutput -Force
}
catch {
    if ($hadHubOutput -and (Test-Path -LiteralPath $hubBackupFile)) {
        Move-Item -LiteralPath $hubBackupFile -Destination $hubOutput -Force
    }
    throw
}
if (Test-Path -LiteralPath $hubStageOutput) { Remove-DirectoryWithRetry -Path $hubStageOutput }
if ($hadHubOutput -and (Test-Path -LiteralPath $hubBackupFile)) { Remove-Item -LiteralPath $hubBackupFile -Force }
if (Test-Path -LiteralPath $legacyHubOutput) { Remove-DirectoryWithRetry -Path $legacyHubOutput }
if (Test-Path -LiteralPath $legacyRootHubPath) { Remove-Item -LiteralPath $legacyRootHubPath -Force }
New-Item -ItemType Directory -Path $tempRoot, `
    (Join-Path $outputRoot 'Preferences\Hub'), `
    (Join-Path $outputRoot "Preferences\BEgine-$engineVersion") -Force | Out-Null

# Retire the flat layout only after both new hosts have published successfully.
# Preserve an older installed release, or archive the previous copy of this release.
$legacyCore = Join-Path $engineVersionsRoot 'BEngine.dll'
if (Test-Path -LiteralPath $legacyCore) {
    $legacyVersion = ([string](Get-Item -LiteralPath $legacyCore).VersionInfo.ProductVersion).Split('+')[0]
    $legacyDestination = if ($legacyVersion -match '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$' -and
        -not (Test-Path -LiteralPath (Join-Path $engineVersionsRoot "BEgine-$legacyVersion"))) {
        Join-Path $engineVersionsRoot "BEgine-$legacyVersion"
    }
    else {
        Join-Path $tempRoot ('LegacyEngine/flat-' + [Guid]::NewGuid().ToString('N'))
    }
    Assert-RootExportPath $legacyDestination
    $legacyEntries = @(Get-ChildItem -LiteralPath $engineVersionsRoot -Force | Where-Object {
        -not $_.PSIsContainer -or ($_.Name -notlike 'BEgine-*' -and $_.Name -notlike '.BEgine-*')
    })
    New-Item -ItemType Directory -Path $legacyDestination -Force | Out-Null
    foreach ($entry in $legacyEntries) {
        Assert-ExportPath $entry.FullName
        $destination = Join-Path $legacyDestination $entry.Name
        Assert-RootExportPath $destination
        Move-Item -LiteralPath $entry.FullName -Destination $destination
    }
    Write-Output "BENGINE_LEGACY_LAYOUT_MOVED|$legacyDestination"
}

# The Hub is launched directly from Engine/Engine/BEngine.Hub.exe. Retire both legacy
# batch entry points so a compact export has one authoritative executable.
$legacyLauncherShortcuts = @(
    (Join-Path $repositoryRoot 'BEngine.bat'),
    (Join-Path $outputRoot 'BEngine.bat')
)
foreach ($legacyLauncherShortcut in $legacyLauncherShortcuts) {
    if (Test-Path -LiteralPath $legacyLauncherShortcut) {
        Remove-Item -LiteralPath $legacyLauncherShortcut -Force
    }
}

Write-Output "BENGINE_EXPORT_OK|$Configuration|$engineOutput|$hubOutput"
