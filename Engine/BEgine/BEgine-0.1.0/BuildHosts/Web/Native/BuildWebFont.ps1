param(
    [Parameter(Mandatory=$true)][string]$FreeTypeSource,
    [Parameter(Mandatory=$true)][string]$EmscriptenTools,
    [Parameter(Mandatory=$true)][string]$SkiaArchive,
    [Parameter(Mandatory=$true)][string]$IntermediateDirectory,
    [string]$Output = (Join-Path $PSScriptRoot 'libBEngineWebFont.a')
)
$ErrorActionPreference = 'Stop'
$nativeIntermediate = [IO.Path]::GetFullPath($IntermediateDirectory)
New-Item -ItemType Directory -Path $nativeIntermediate -Force | Out-Null
$nativeAr = Join-Path $EmscriptenTools 'bin/llvm-ar.exe'
$nativeHeaders = Join-Path $FreeTypeSource 'include'
if (!(Test-Path -LiteralPath (Join-Path $nativeHeaders 'ft2build.h'))) { throw 'FreeType public headers are missing.' }
if (!(Test-Path -LiteralPath $SkiaArchive)) { throw 'The pinned SkiaSharp FreeType archive is missing.' }
$nativeMembers = @(& $nativeAr t $SkiaArchive | Where-Object { $_ -match '^(libfreetype2|libpng|libzlib|libbrotli)\..*\.o$' })
if ($LASTEXITCODE -ne 0 -or !($nativeMembers -contains 'libfreetype2.ftinit.o')) { throw 'FreeType objects are missing from the native archive.' }
Push-Location $nativeIntermediate
try {
    & $nativeAr x $SkiaArchive @nativeMembers
    if ($LASTEXITCODE -ne 0) { throw 'Extracting FreeType objects failed.' }
    $nativeObject = Join-Path $nativeIntermediate 'WebFont.o'
    & $env:EMSDK_PYTHON (Join-Path $EmscriptenTools 'emscripten/emcc.py') '-Oz' '-msimd128' '-fwasm-exceptions' '-flto' '-Wall' '-Wextra' '-Werror' "-I$nativeHeaders" '-c' (Join-Path $PSScriptRoot 'WebFont.c') '-o' $nativeObject
    if ($LASTEXITCODE -ne 0) { throw 'Compiling WebFont.c failed.' }
    $nativeArchive = Join-Path $nativeIntermediate 'libBEngineWebFont.a'
    # A fresh archive avoids retaining stale members when the pinned library changes.
    if (Test-Path -LiteralPath $nativeArchive) { throw 'Use an empty intermediate directory for a reproducible archive.' }
    & $nativeAr rcs $nativeArchive $nativeObject @nativeMembers
    if ($LASTEXITCODE -ne 0) { throw 'Creating the Web font archive failed.' }
    $nativeDestination = [IO.Path]::GetFullPath($Output)
    New-Item -ItemType Directory -Path (Split-Path -Parent $nativeDestination) -Force | Out-Null
    Copy-Item -LiteralPath $nativeArchive -Destination $nativeDestination -Force
    Get-FileHash -LiteralPath $nativeDestination -Algorithm SHA256
} finally { Pop-Location }
