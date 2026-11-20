[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$StageOutput,
    [Parameter(Mandatory)][string]$Configuration,
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string[]]$HostPublishArguments
)
$ErrorActionPreference = 'Stop'
$playerBuildHosts = Join-Path $PSScriptRoot 'Hosts'
$playerProject = Join-Path $playerBuildHosts 'Desktop/BEngine.Player.DesktopHost.csproj'
$runtimeDistributionProject = Join-Path $PSScriptRoot 'RuntimeDistribution/BEngine.RuntimeDistribution.csproj'
$playerHostPublishArguments = $HostPublishArguments
& dotnet publish $playerProject -c $Configuration --self-contained false -o $stageOutput -m:1 `
    /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
    @playerHostPublishArguments
if ($LASTEXITCODE -ne 0) { throw "Player publish failed with exit code $LASTEXITCODE." }

# Build Hosts must not reference the Editor export directory. In particular, mobile AOT
# must never see the Windows-only Editor closure, so publish a clean Player runtime dependency set.
$buildRuntimeOutput = Join-Path $stageOutput 'BuildRuntime'
New-Item -ItemType Directory -Path $buildRuntimeOutput | Out-Null
& dotnet publish $runtimeDistributionProject -c $Configuration -o $buildRuntimeOutput -m:1 `
    /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false
if ($LASTEXITCODE -ne 0) { throw "BuildRuntime publish failed with exit code $LASTEXITCODE." }

# Each variant is compiled independently: removing DLL files from the combined SDK
# would leave unresolved type references inside Desktop and keep compiler native assets.
foreach ($variant in @('OpenGL', 'Vulkan', 'VulkanOffline', 'BothOffline')) {
    $variantOutput = Join-Path $buildRuntimeOutput "Desktop/$variant"
    $variantArtifacts = Join-Path $repositoryRoot "Temp/Engine/GraphicsVariants/$variant"
    $graphics = $variant.Replace('Offline', '')
    $compiler = if ($variant.EndsWith('Offline') -or $graphics -eq 'OpenGL') { 'false' } else { 'true' }
    & dotnet publish $runtimeDistributionProject -c $Configuration -o $variantOutput -m:1 `
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
    $platformArtifacts = Join-Path $repositoryRoot "Temp/Engine/PlatformRuntime/$platform"
    & dotnet publish $runtimeDistributionProject -c $Configuration -o $platformRuntime -m:1 `
        --artifacts-path $platformArtifacts `
        /nodeReuse:false -p:NuGetAudit=false -p:BEngineEngineExportOwner=false -p:BEnginePackageExportOwner=false `
        -p:BEngineRuntimeShaderCompiler=false "-p:BEnginePlayerPlatform=$platform"
    if ($LASTEXITCODE -ne 0) { throw "$platform runtime export failed with exit code $LASTEXITCODE." }
    foreach ($required in @('BEngine.Player.dll', "BEngine.$platform.dll",
            'BEngine.RuntimeDistribution.runtimeconfig.json', 'BEngine.RuntimeDistribution.deps.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $platformRuntime $required))) {
            throw "$platform runtime is missing $required."
        }
    }
    # iOS uses the platform's Metal renderer; shader compilation and desktop backends stay out.
    if ($platform -eq 'iOS') {
        foreach ($metalRuntime in @('BEngine.Platform.Veldrid.dll', 'Veldrid.dll', 'Veldrid.MetalBindings.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $platformRuntime $metalRuntime))) {
                throw "iOS runtime is missing Metal dependency $metalRuntime."
            }
        }
    }
    $forbiddenRuntime = @('BEngine.Core.Editor.dll', 'BEngine.Editor.dll', 'BEngine.Platform.Desktop.dll', 'glfw3.dll',
        'Veldrid.SPIRV.dll', 'shaderc_shared.dll', 'libshaderc_shared.dll', 'libshaderc_shared.so', 'libshaderc_shared.dylib')
    if ($platform -ne 'iOS') { $forbiddenRuntime += 'Veldrid.dll' }
    foreach ($forbidden in $forbiddenRuntime) {
        if (Test-Path -LiteralPath (Join-Path $platformRuntime $forbidden)) {
            throw "$platform runtime contains desktop dependency $forbidden."
        }
    }
}

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
foreach ($requiredRuntime in @('BEngine.Core.dll', 'BEngine.Player.dll', 'BEngine.Platform.DotNet.dll',
        'BEngine.Platform.Desktop.dll', 'BEngine.Windows.dll', 'BEngine.Linux.dll', 'BEngine.macOS.dll',
        'BEngine.RuntimeDistribution.runtimeconfig.json', 'BEngine.RuntimeDistribution.deps.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $buildRuntimeOutput $requiredRuntime))) {
        throw "Engine BuildRuntime is missing $requiredRuntime."
    }
}
if (Test-Path -LiteralPath (Join-Path $buildRuntimeOutput 'BEngine.Editor.dll')) {
    throw 'Engine BuildRuntime must not contain BEngine.Editor.dll.'
}
if (Test-Path -LiteralPath (Join-Path $buildRuntimeOutput 'BEngine.Core.Editor.dll')) {
    throw 'Engine BuildRuntime must not contain the editor package kernel.'
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

foreach ($path in @('BEngine.Game.runtimeconfig.json', 'BEngine.Game.deps.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $StageOutput $path)) -and
        -not (Test-Path -LiteralPath (Join-Path $StageOutput 'BEngine.Game.exe'))) {
        throw "Player publication is missing $path and its executable bundle."
    }
}
