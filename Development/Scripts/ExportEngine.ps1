[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$IncrementalPackages,
    [switch]$SkipIDEPlugins,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$previousToolEnvironment = [Environment]::GetEnvironmentVariables('Process')
try {
. (Join-Path $PSScriptRoot 'SetToolEnvironment.ps1')
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src'))
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) { Join-Path $repositoryRoot 'Engine\Engine' } else { [IO.Path]::GetFullPath($OutputRoot) }
$engineVersion = & (Join-Path $PSScriptRoot 'GetEngineVersion.ps1')
$engineVersionsRoot = Join-Path $outputRoot 'BEgine'
$engineOutput = Join-Path $engineVersionsRoot "BEgine-$engineVersion"
$stageOutput = Join-Path $engineVersionsRoot ".BEgine-$engineVersion.publish-staging"
$backupOutput = Join-Path $engineVersionsRoot ".BEgine-$engineVersion.publish-backup"
$tempRoot = Join-Path $repositoryRoot 'Temp'
# Publication restores RID assets. Keep its obj files separate from ordinary
# portable builds, which can otherwise overwrite the shared project.assets.json
# between publish's restore and build phases.
$publishArtifactsPath = Join-Path $tempRoot 'Engine\Build\ExportPublish'
$hubOutput = Join-Path $outputRoot 'BEngine.Hub.exe'
$legacyHubOutput = Join-Path $outputRoot 'Hub'
$hubStageOutput = Join-Path $outputRoot '.Hub.publish-staging'
$hubBackupOutput = Join-Path $outputRoot '.Hub.publish-backup'
$hubBackupFile = Join-Path $outputRoot '.BEngine.Hub.publish-backup'
$launcherProject = Join-Path $sourceRoot 'Hub\BEngine.Launcher\BEngine.Launcher.csproj'
$editorProject = Join-Path $sourceRoot 'Platform\BEngine.Editor.Host\BEngine.Editor.Host.csproj'
$coreResources = Join-Path $sourceRoot 'Core\Resources'
$coreEditor = Join-Path $sourceRoot 'Core\Editor'
$platformPlayer = Join-Path $sourceRoot 'Platform\Player'
$engineDefines = Join-Path $PSScriptRoot '../Build/BEngine.Defines.targets'
$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
# Hub and Editor share the installed .NET runtime. Hub's managed bundle loads
# directly from the executable. Editor keeps its compiler references and native
# libraries beside its apphost. Neither extracts files into the user's .NET
# bundle cache before our application code runs.
$compactHostPublishArguments = @(
    "-p:ArtifactsPath=$publishArtifactsPath",
    # SDK project-reference evaluation removes SelfContained. Keep its publish
    # counterpart explicit so referenced executable projects cannot infer true.
    '-p:PublishSelfContained=false',
    '-p:PublishRuntimeIdentifier=win-x64',
    '-p:PublishSingleFile=true',
    '-p:IncludeAllContentForSelfExtract=false',
    '-p:IncludeNativeLibrariesForSelfExtract=false',
    '-p:EnableCompressionInSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)
# Editor compiles project/package sources against its own installed assemblies.
# A regular apphost preserves Assembly.Location for every compiler reference
# without bundling and extracting DLLs on startup.
$editorHostPublishArguments = @($compactHostPublishArguments | Where-Object {
    $_ -notlike '-p:PublishSingleFile=*'
}) + @('-p:PublishSingleFile=false')
# Packaged applications use a separate policy from the authoring tools.
# Preserve its established single-file layout for existing launch/build flows.
$applicationPublishArguments = @($compactHostPublishArguments | Where-Object {
    $_ -notlike '-p:IncludeAllContentForSelfExtract=*' -and
    $_ -notlike '-p:IncludeNativeLibrariesForSelfExtract=*'
}) + @('-p:IncludeAllContentForSelfExtract=true', '-p:IncludeNativeLibrariesForSelfExtract=true')

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
$toolingRoots = @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Packages') -Directory) +
    @((Get-Item -LiteralPath (Join-Path $sourceRoot 'Core')))
$tooling = @($toolingRoots | ForEach-Object {
    $descriptor = Join-Path $_.FullName 'Editor/tooling.json'
    if (Test-Path -LiteralPath $descriptor) {
        $data = Get-Content -Raw -LiteralPath $descriptor | ConvertFrom-Json
        if ($data.version -ne 1) { throw "Unsupported tooling descriptor: $descriptor" }
        [pscustomobject]@{ Root = Split-Path -Parent $descriptor; Data = $data }
    }
})
function Resolve-ToolingPath($Tool, [string]$Relative, [switch]$PackageScope) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative)) {
        throw 'Tooling paths must be relative to their descriptor.'
    }
    $boundary = if ($PackageScope) { Split-Path -Parent $Tool.Root } else { $Tool.Root }
    $prefix = [IO.Path]::GetFullPath($boundary).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath((Join-Path $Tool.Root $Relative))
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Invalid tooling path: $Relative" }
    return $resolved
}
# IDE plugins are independent authoring tools, outside the engine compilation
# graph. Build them before publishing either host so missing toolchains do not
# leave a partially updated engine installation.
$idePluginRoot = Join-Path (Split-Path -Parent $sourceRoot) 'Plugin'
$idePluginPackages = Join-Path $tempRoot 'Plugin/Packages'
if (-not $SkipIDEPlugins) {
    $idePluginBuilder = Join-Path $idePluginRoot 'Build.ps1'
    if (-not (Test-Path -LiteralPath $idePluginBuilder -PathType Leaf)) {
        throw "IDE plugin sources are missing: $idePluginBuilder. Use -SkipIDEPlugins for an engine-only export."
    }
    & $idePluginBuilder -Configuration $Configuration -NoExport `
        -OutputDirectory $idePluginPackages -ArtifactsDirectory (Join-Path $tempRoot 'Plugin/Build')
}
# Project references determine build order across Core, Platform, and packages.
& dotnet build (Join-Path $sourceRoot 'BEngine.sln') -c $Configuration -m:1 /nodeReuse:false `
    -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false -v:q
if ($LASTEXITCODE -ne 0) { throw 'Engine source compilation failed.' }
$packageExportProjects = Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Packages') -Filter '*.csproj' -File -Recurse |
    Where-Object { Select-String -LiteralPath $_.FullName -Pattern '<BEnginePackageExportOwner>\s*true\s*</BEnginePackageExportOwner>' -Quiet } |
    Sort-Object FullName
function Test-PackageExportCurrent([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Destination)) { return $false }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        $relative = $file.FullName.Substring($Source.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1)
        if ($relative -notmatch '^(BEngine[^\\]*\\.*\.cs|BEngine[^\\]*\\[^\\]+\.asmdef\.yaml|Resources\\.*|Editor\\.*|package\.yaml)$' -or $relative -match '\\(bin|obj)\\') { continue }
        [void]$expected.Add($relative)
        $target = Join-Path $Destination $relative
        if (-not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { return $false }
    }
    $descriptor = Join-Path $Source 'Editor/tooling.json'
    if (Test-Path -LiteralPath $descriptor) {
        $sourcePrefix = [IO.Path]::GetFullPath($Source).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        $data = Get-Content -Raw -LiteralPath $descriptor | ConvertFrom-Json
        foreach ($generated in $data.generatedContent) {
            $project = [IO.Path]::GetFullPath((Join-Path $Source $generated.project))
            $targetRoot = [IO.Path]::GetFullPath((Join-Path $Destination $generated.directory))
            $destinationPrefix = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
            if (-not $project.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase) -or
                -not $targetRoot.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase) -or
                $generated.sourcePathProperty -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or
                [IO.Path]::IsPathRooted($generated.sourceSubdirectory)) { throw "Invalid generated-content metadata: $descriptor" }
            $value = @(& dotnet msbuild $project -nologo "-getProperty:$($generated.sourcePathProperty)" `
                -p:BEnginePackageExportOwner=false -p:BEngineEngineExportOwner=false)
            if ($LASTEXITCODE -ne 0) { throw "Cannot resolve generated content for $project" }
            $dependencyRoot = [IO.Path]::GetFullPath(($value -join "`n").Trim()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
            $generatedRoot = [IO.Path]::GetFullPath((Join-Path $dependencyRoot $generated.sourceSubdirectory))
            if (-not $generatedRoot.StartsWith($dependencyRoot, [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $generatedRoot -PathType Container)) { throw "Generated content source is unavailable: $descriptor" }
            foreach ($file in Get-ChildItem -LiteralPath $generatedRoot -File -Recurse) {
                $relative = $file.FullName.Substring($generatedRoot.TrimEnd('\', '/').Length + 1)
                $target = Join-Path $targetRoot $relative
                $exportRelative = $target.Substring($destinationPrefix.Length)
                if (-not $expected.Add($exportRelative)) { throw "Duplicate generated export path: $exportRelative" }
                if (-not (Test-Path -LiteralPath $target) -or
                    (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne
                    (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { return $false }
            }
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $Destination -Recurse -File) {
        $relative = $file.FullName.Substring($Destination.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1)
        # IDE design-time evaluation may create these after publication. They
        # are excluded on the source side too and are never exported payloads.
        if ($relative -match '(^|[\\/])(bin|obj)[\\/]') { continue }
        if (-not $expected.Contains($relative)) { return $false }
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
    $packageStage = [IO.Path]::GetFullPath((Join-Path $packageOutput ".$packageName.staging"))
    Remove-DirectoryWithRetry -Path $packageStage
    # Publish core resources once from staging; package references must not replace the live Editor tree.
    & dotnet build $project.FullName -c $Configuration -m:1 /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEngineStagePackageOnly=true -p:BuildProjectReferences=false "-p:BEnginePackageOutputRoot=$packageOutput" -v:q
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
foreach ($packageSource in Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Packages') -Directory) {
    $dependencyStage = [IO.Path]::GetFullPath((Join-Path $packageOutput ".$($packageSource.Name).staging"))
    if ((Split-Path -Parent $dependencyStage) -ne $packageOutput) { throw 'Invalid dependency staging path.' }
    Remove-DirectoryWithRetry -Path $dependencyStage
}

# Project packages have no export-owner assembly: publish their source content directly.
foreach ($projectPackage in Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Packages') -Directory) {
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
$replacedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($sourcePackage in Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Packages') -Directory) {
    $definition = Join-Path $sourcePackage.FullName 'package.yaml'
    if (-not (Test-Path -LiteralPath $definition)) { continue }
    $text = [IO.File]::ReadAllText($definition)
    $replacementList = [regex]::Match($text, '(?ms)^replaces:\s*\r?\n(?<ids>(?:- [^\r\n]+\r?\n)*)').Groups['ids'].Value
    foreach ($match in [regex]::Matches($replacementList, '(?m)^- (\S+)\s*$')) { [void]$replacedIds.Add($match.Groups[1].Value) }
}
foreach ($group in Get-ChildItem -LiteralPath $packageOutput -Directory) {
    foreach ($version in Get-ChildItem -LiteralPath $group.FullName -Directory) {
        $definition = Join-Path $version.FullName 'package.yaml'
        if (-not (Test-Path -LiteralPath $definition)) { continue }
        $id = [regex]::Match([IO.File]::ReadAllText($definition), '(?m)^id:\s*(\S+)\s*$').Groups[1].Value
        if ($replacedIds.Contains($id)) { Remove-DirectoryWithRetry -Path $version.FullName }
    }
    if (@(Get-ChildItem -LiteralPath $group.FullName -Force).Count -eq 0) { Remove-DirectoryWithRetry -Path $group.FullName }
}

& dotnet publish $launcherProject -c $Configuration -r win-x64 --self-contained false -o $hubStageOutput -m:1 `
    /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
    @compactHostPublishArguments
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed with exit code $LASTEXITCODE." }
# The website distributes Hub as one executable. A future native/content
# dependency must not silently disappear when the staging directory is removed.
$unexpectedHubFiles = @(Get-ChildItem -LiteralPath $hubStageOutput -File -Recurse | Where-Object {
    $_.FullName -ne (Join-Path $hubStageOutput 'BEngine.Launcher.exe')
})
if ($unexpectedHubFiles.Count -gt 0) {
    throw "Hub must publish without extraction or external dependencies: $($unexpectedHubFiles.FullName -join ', ')."
}

# Keep the RID publish-scoped: a global RuntimeIdentifier makes transitive
# Referenced application projects can contribute both RID and portable apphost metadata.
& dotnet publish $editorProject -c $Configuration --self-contained false -o $stageOutput -m:1 `
    /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
    @editorHostPublishArguments
if ($LASTEXITCODE -ne 0) { throw "Editor publish failed with exit code $LASTEXITCODE." }
# Keep the established executable name for the composed editor host.
Move-Item -LiteralPath (Join-Path $stageOutput "BEngine.Editor.Host.exe") -Destination (Join-Path $stageOutput "BEngine.Editor.exe") -Force

$editorApplications = @($tooling | Where-Object { $null -ne $_.Data.editorApplication })
if ($editorApplications.Count -ne 1) { throw 'Exactly one editor application entry must be declared by installed tooling.' }
$editorBootstrapAssemblies = [Collections.Generic.List[string]]::new()
$editorBootstrapProjects = [Collections.Generic.List[object]]::new()
$editorBootstrapIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[void]$editorBootstrapIdentities.Add([string]$editorApplications[0].Data.editorApplication.assembly)
foreach ($tool in $tooling) {
    # Project policies can initialize before project compilation without making
    # the default workbench reference a development framework's implementation.
    if ($null -eq $tool.Data.editorBootstrap -and -not $tool.Data.editorBootstrapProject) { continue }
    $assembly = [string]$tool.Data.editorBootstrap.assembly
    if (-not $tool.Data.editorBootstrapProject -or [string]::IsNullOrWhiteSpace($assembly) -or
        $assembly -ne [IO.Path]::GetFileName($assembly) -or $assembly.IndexOfAny([char[]]'\/:') -ge 0 -or
        -not $assembly.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -or
        -not $editorBootstrapIdentities.Add($assembly)) {
        throw 'Editor bootstrap publication has an invalid or duplicate assembly declaration.'
    }
    $project = Resolve-ToolingPath $tool $tool.Data.editorBootstrapProject -PackageScope
    $editorBootstrapAssemblies.Add($assembly)
    $editorBootstrapProjects.Add([pscustomobject]@{ Project = $project; Assembly = $assembly })
}
foreach ($tool in $tooling) {
    if ($tool.Data.editorApplicationProject) {
        $project = Resolve-ToolingPath $tool $tool.Data.editorApplicationProject
        & dotnet publish $project -c $Configuration --self-contained false -o $stageOutput -m:1 `
            /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
            @editorHostPublishArguments
        if ($LASTEXITCODE -ne 0) { throw "Editor application publication failed: $project" }
        $entryAssembly = [string]$tool.Data.editorApplication.assembly
        $entryType = [string]$tool.Data.editorApplication.type
        if ([string]::IsNullOrWhiteSpace($entryType) -or [string]::IsNullOrWhiteSpace($entryAssembly) -or
            $entryAssembly -ne [IO.Path]::GetFileName($entryAssembly) -or $entryAssembly.IndexOfAny([char[]]'\/:') -ge 0 -or
            -not $entryAssembly.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath (Join-Path $stageOutput $entryAssembly)) -or
            -not (Test-Path -LiteralPath (Join-Path $stageOutput ([IO.Path]::ChangeExtension($entryAssembly, '.deps.json'))))) {
            throw 'Editor application publication has an invalid entry or missing dependency metadata.'
        }
        $editorApplicationDescriptor = [ordered]@{ version = 1; assembly = $tool.Data.editorApplication.assembly;
            type = $tool.Data.editorApplication.type; bootstrapAssemblies = @($editorBootstrapAssemblies) }
    }
    if ($tool.Data.engineExportScript) {
        $hook = Resolve-ToolingPath $tool $tool.Data.engineExportScript
        & $hook -StageOutput $stageOutput -Configuration $Configuration -RepositoryRoot $repositoryRoot `
            -HostPublishArguments $applicationPublishArguments
    }
}
# Player distribution is part of the engine independently of optional development frameworks.
foreach ($compilerReference in @('BEngine.Core.dll', 'BEngine.Core.Editor.dll',
        'Microsoft.Extensions.DependencyInjection.Abstractions.dll', 'BEngine.Player.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageOutput $compilerReference))) {
        throw "Player build services are missing the external compiler reference $compilerReference."
    }
}
& (Join-Path $platformPlayer 'ExportPlayer.ps1') -StageOutput $stageOutput -Configuration $Configuration -RepositoryRoot $repositoryRoot -HostPublishArguments $applicationPublishArguments
if ($LASTEXITCODE -ne 0) { throw 'Engine Player distribution export failed.' }

# Publish extensions after all executable hosts. Incremental single-file host
# publication can remove previously copied dependencies from its output folder.
# The extensions' independent dependency closure must be the final composition.
foreach ($bootstrap in $editorBootstrapProjects) {
    & dotnet publish $bootstrap.Project -c $Configuration --self-contained false -o $stageOutput -m:1 `
        /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
        @editorHostPublishArguments
    if ($LASTEXITCODE -ne 0) { throw "Editor bootstrap publication failed: $($bootstrap.Project)" }
    if (-not (Test-Path -LiteralPath (Join-Path $stageOutput $bootstrap.Assembly)) -or
        -not (Test-Path -LiteralPath (Join-Path $stageOutput ([IO.Path]::ChangeExtension($bootstrap.Assembly, '.deps.json'))))) {
        throw "Editor bootstrap publication is missing $($bootstrap.Assembly) or its dependency metadata."
    }
}
# Library publishes can prune transitive files from a shared output directory.
# Restore the three Core assemblies after the final host composition.
foreach ($project in @('Core/BEngine.Core/BEngine.Core.csproj',
        'Core/BEngine.Core.Editor/BEngine.Core.Editor.csproj',
        'Core/BEngine.Player/BEngine.Player.csproj') | ForEach-Object { Get-Item -LiteralPath (Join-Path $sourceRoot $_) }) {
    $targetPaths = @(& dotnet msbuild $project.FullName -nologo -getProperty:TargetPath `
        "-p:Configuration=$Configuration" -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($LASTEXITCODE -ne 0 -or $targetPaths.Count -ne 1 -or
        -not (Test-Path -LiteralPath $targetPaths[0] -PathType Leaf)) {
        throw "Built-in Core module assembly is unavailable: $($project.FullName)."
    }
    Copy-Item -LiteralPath $targetPaths[0] -Destination (Join-Path $stageOutput ([IO.Path]::GetFileName($targetPaths[0]))) -Force
}
$editorApplicationDescriptor | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $stageOutput 'editor.application.json') -Encoding utf8

& (Join-Path $sourceRoot 'Platform/ExportPlatformSources.ps1') -StageOutput $stageOutput

# Symbols are useful while building the engine but are not required by the
# published hosts. Keep managed BuildRuntime symbols for player diagnostics,
# but exclude third-party native runtime PDBs (SkiaSharp/HarfBuzzSharp, etc.).
foreach ($symbol in @(Get-ChildItem -LiteralPath $stageOutput -File -Recurse -Filter '*.pdb' |
        Where-Object { $_.FullName -notmatch '[\\/]BuildRuntime[\\/]' -or
            $_.FullName -match '[\\/]runtimes[\\/][^\\/]+[\\/]native[\\/]' })) {
    Assert-ExportPath $symbol.FullName
    Remove-Item -LiteralPath $symbol.FullName -Force
}
foreach ($symbol in @(Get-ChildItem -LiteralPath $hubStageOutput -File -Recurse -Filter '*.pdb')) {
    Remove-Item -LiteralPath $symbol.FullName -Force
}

if (Test-Path -LiteralPath $coreResources) { Copy-Item -LiteralPath $coreResources -Destination (Join-Path $stageOutput 'Resources') -Recurse }
Copy-Item -LiteralPath $coreEditor -Destination (Join-Path $stageOutput 'Editor') -Recurse
Copy-Item -LiteralPath $engineDefines -Destination (Join-Path $stageOutput 'BEngine.Defines.targets')

foreach ($required in @('BEngine.Editor.exe', 'BEngine.Editor.Host.dll',
        'BEngine.Editor.Host.runtimeconfig.json', 'BEngine.Editor.Host.deps.json',
        'BEngine.Core.dll', 'BEngine.Core.Editor.dll', 'BEngine.Player.dll',
        'Microsoft.Extensions.DependencyInjection.Abstractions.dll',
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

foreach ($hostName in @('BEngine.Launcher', 'BEngine.Editor.Host')) {
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

& (Join-Path $PSScriptRoot '../Tools/Test-PackageBoundaries.ps1') -AssemblyDirectories @($stageOutput) `
    -OutputPath (Join-Path $tempRoot 'Engine/Verification/PackageReorganization20261101/ExportBoundary.json')

& (Join-Path $PSScriptRoot 'ExportBundledPackages.ps1') -SourceRoot $sourceRoot `
    -PackagesRoot $packageOutput -EngineVersion $engineVersion `
    -OutputPath (Join-Path $stageOutput 'bundled-packages.json')

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
        try { Move-DirectoryWithRetry -Source $stageOutput -Destination $engineOutput }
        catch {
            $reason = $_.Exception
            while ($null -ne $reason.InnerException) { $reason = $reason.InnerException }
            if (-not $runningOnWindows -or ($reason -isnot [IO.IOException] -and $reason -isnot [UnauthorizedAccessException])) { throw }
            Write-Warning 'A process holds the staging directory; publishing verified files with a mirror.'
            Sync-DirectoryWithRobocopy -Source $stageOutput -Destination $engineOutput
            try { Remove-DirectoryWithRetry -Path $stageOutput }
            catch { Write-Warning "Published successfully; staging cleanup will be retried: $_" }
        }
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
foreach ($module in @('Attributes', 'Math', 'Async', 'BuildCore', 'Collections', 'Serialization',
        'Instrumentation', 'Foundation', 'Compilation', 'EditorServices', 'AssetPipeline',
        'Player', 'Pooling')) {
    Remove-DirectoryWithRetry -Path (Join-Path $packageOutput $module)
}
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

# Replacing an EXE through a staging rename can leave Explorer displaying its
# previous icon. Notify the Shell about these exact items after publication.
# https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotify
if ($runningOnWindows) {
    try {
        if (-not ('BEngine.Export.ShellNotification' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace BEngine.Export {
    public static class ShellNotification {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);
        public static void ItemUpdated(string path) {
            const int updateItem = 0x00002000;
            const uint pathW = 0x0005;
            const uint flushNoWait = 0x2000;
            SHChangeNotify(updateItem, pathW | flushNoWait, path, IntPtr.Zero);
        }
    }
}
'@
        }
        [BEngine.Export.ShellNotification]::ItemUpdated($hubOutput)
        [BEngine.Export.ShellNotification]::ItemUpdated((Join-Path $engineOutput 'BEngine.Editor.exe'))
        Write-Output 'BENGINE_EXPORT_SHELL_ICONS_NOTIFIED'
    }
    catch {
        Write-Warning "The engine was exported, but the Shell icon refresh notification failed: $_"
    }
}

if (-not $SkipIDEPlugins) {
    & (Join-Path $idePluginRoot 'Export.ps1') -PackageDirectory $idePluginPackages -InstallationRoot $outputRoot
}
# These assemblies now ship with the host. Retire only the platform package
# groups previously emitted by this source tree, after publication succeeds.
$retiredPackages = @('Coroutines', 'PlatformAbstractions', 'PlatformAndroid', 'PlatformAndroidBuild',
    'PlatformBuild', 'PlatformContracts', 'PlatformDesktop', 'PlatformDotNet', 'PlatformiOS',
    'PlatformiOSBuild', 'PlatformLinux', 'PlatformLinuxBuild', 'PlatformmacOS',
    'PlatformmacOSBuild', 'PlatformVeldrid', 'PlatformWeb', 'PlatformWebBuild',
    'PlatformWindows', 'PlatformWindowsBuild')
# CommonPackages owns these source packages and exports them independently.
# Remove legacy engine releases as well as their old bundled-package status.
$retiredPackages += @('Jobs', 'Reactive', 'StandardAsset', 'ArtImport2D', 'Codex',
    'Cinemachine', 'Localization', 'PixelPerfect2D', 'PostProcessing', 'PropertyAttributes',
    'ShaderGraph', 'SpriteShape2D', 'SpriteSkinning2D', 'Testing', 'TiledMap',
    'Timeline', 'VisualScripting')
foreach ($name in $retiredPackages) {
    $retired = Join-Path $packageOutput $name
    Assert-ExportPath -Path $retired
    if (Test-Path -LiteralPath $retired) { Remove-DirectoryWithRetry -Path $retired }
}
Write-Output "BENGINE_EXPORT_OK|$Configuration|$engineOutput|$hubOutput"
}
finally {
    # Invoking this script from an existing PowerShell session must not change
    # the user-data paths of games or other applications launched afterward.
    foreach ($name in $toolEnvironmentNames) {
        $previousValue = if ($previousToolEnvironment.Contains($name)) { $previousToolEnvironment[$name] } else { [NullString]::Value }
        [Environment]::SetEnvironmentVariable($name, $previousValue, 'Process')
    }
}
