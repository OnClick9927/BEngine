#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IntermediateDirectory,
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string[]]$RuntimeIdentifiers = @('win-x64', 'win-x86'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'runtimes'),
    [string]$CMakePath
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The GLFW Win32 backend must be built with the Windows C++ toolchain.' }
$work = [IO.Path]::GetFullPath($IntermediateDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($work)

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install the Visual Studio C++ build tools before rebuilding GLFW.' }
$installation = @(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json | ConvertFrom-Json)[0]
if ($null -eq $installation) { throw 'The Visual Studio C++ toolchain was not found.' }
$major = ([version]$installation.installationVersion).Major
$generator = switch ($major) {
    17 { 'Visual Studio 17 2022' }
    18 { 'Visual Studio 18 2026' }
    default { throw "Unsupported Visual Studio generator version: $major" }
}
if (-not $CMakePath) {
    $cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
    $CMakePath = if ($cmakeCommand) { $cmakeCommand.Source } else {
        Join-Path $installation.installationPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
    }
}
if (-not (Test-Path -LiteralPath $CMakePath)) { throw 'Install CMake for Windows before rebuilding GLFW.' }

# Pin the complete official source archive; no Git checkout or mutable branch is used.
$sourceVersion = '3.4'
$archiveHash = 'A133DDC3D3C66143EBA9035621DB8E0BCF34DBA1EE9514A9E23E96AFD39FD57A'
$archive = Join-Path $work "glfw-$sourceVersion.zip"
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri "https://codeload.github.com/glfw/glfw/zip/refs/tags/$sourceVersion" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveHash) {
    throw "The cached GLFW source archive has the wrong SHA256: $archive"
}
Expand-Archive -LiteralPath $archive -DestinationPath $work -Force
$sourceRoot = Join-Path $work "glfw-$sourceVersion"
$windowSource = Join-Path $sourceRoot 'src/win32_window.c'
$source = [IO.File]::ReadAllText($windowSource)
$anchor = '        window = GetPropW(handle, L"GLFW");'
if (($source.Split($anchor).Count - 1) -ne 1) { throw 'The GLFW ownership patch no longer has one exact source anchor.' }
$patch = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'win32-window-ownership.inc')).TrimEnd("`r", "`n")
[IO.File]::WriteAllText($windowSource, $source.Replace($anchor, $patch))

$previousTemp = $env:TEMP
$previousTmp = $env:TMP
try {
    $env:TEMP = $work
    $env:TMP = $work
    foreach ($rid in $RuntimeIdentifiers) {
        $architecture = switch ($rid) { 'win-x64' { 'x64' } 'win-x86' { 'Win32' } 'win-arm64' { 'ARM64' } }
        $build = Join-Path $work "cmake-$rid"
        & $CMakePath -S $sourceRoot -B $build -G $generator -A $architecture `
            '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' '-DBUILD_SHARED_LIBS=ON' `
            '-DGLFW_BUILD_EXAMPLES=OFF' '-DGLFW_BUILD_TESTS=OFF' '-DGLFW_BUILD_DOCS=OFF' `
            '-DGLFW_INSTALL=OFF' '-DUSE_MSVC_RUNTIME_LIBRARY_DLL=OFF' `
            '-DCMAKE_SHARED_LINKER_FLAGS_RELEASE=/INCREMENTAL:NO /Brepro'
        if ($LASTEXITCODE -ne 0) { throw "GLFW configure failed for $rid." }
        & $CMakePath --build $build --config Release --parallel 8
        if ($LASTEXITCODE -ne 0) { throw "GLFW compilation failed for $rid. Install that architecture's Visual Studio C++ tools." }
        $destination = Join-Path $output "$rid/native"
        [void][IO.Directory]::CreateDirectory($destination)
        Copy-Item -LiteralPath (Join-Path $build 'src/Release/glfw3.dll') -Destination $destination -Force
        $hash = (Get-FileHash -LiteralPath (Join-Path $destination 'glfw3.dll') -Algorithm SHA256).Hash
        Write-Output "BENGINE_GLFW_BUILT|$rid|source=$sourceVersion|sha256=$hash"
    }
} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
