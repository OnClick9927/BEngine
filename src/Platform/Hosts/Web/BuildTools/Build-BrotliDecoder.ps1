#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$IntermediateDirectory,
    [string]$RuntimePackVersion='10.0.12'
)
$ErrorActionPreference='Stop'
$snapshot=@{}
Get-ChildItem Env: | ForEach-Object {$snapshot[$_.Name]=$_.Value}
try {
    $dotnetRoot=Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source
    $packs=Join-Path $dotnetRoot 'packs'
    $sdk=Join-Path $packs "Microsoft.NET.Runtime.Emscripten.3.1.56.Sdk.win-x64/$RuntimePackVersion/tools"
    $native=Join-Path $packs "Microsoft.NETCore.App.Runtime.Mono.browser-wasm/$RuntimePackVersion/runtimes/browser-wasm/native"
    $python=Join-Path $packs "Microsoft.NET.Runtime.Emscripten.3.1.56.Python.win-x64/$RuntimePackVersion/tools/python.exe"
    $env:DOTNET_EMSCRIPTEN_LLVM_ROOT=Join-Path $sdk 'bin'
    $env:DOTNET_EMSCRIPTEN_BINARYEN_ROOT=$sdk
    $env:DOTNET_EMSCRIPTEN_NODE_JS=Join-Path $packs "Microsoft.NET.Runtime.Emscripten.3.1.56.Node.win-x64/$RuntimePackVersion/tools/bin/node.exe"
    $env:EM_CONFIG=Join-Path $sdk 'emscripten/.emscripten'
    $env:EM_CACHE=Join-Path $packs "Microsoft.NET.Runtime.Emscripten.3.1.56.Cache.win-x64/$RuntimePackVersion/tools/emscripten/cache"
    foreach($path in @($python,$env:DOTNET_EMSCRIPTEN_NODE_JS,$env:EM_CONFIG,(Join-Path $native 'libbrotlidec.a'),(Join-Path $native 'libbrotlicommon.a'))){
        if(!(Test-Path -LiteralPath $path)){throw "Install wasm-tools $RuntimePackVersion before regenerating the decoder. Missing: $path"}
    }
    $work=[IO.Path]::GetFullPath($IntermediateDirectory)
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $env:TEMP=$work
    $env:TMP=$work
    $output=Join-Path $work 'brotli-decoder.wasm'
    $arguments=@((Join-Path $sdk 'emscripten/emcc.py'),(Join-Path $PSScriptRoot 'BrotliDecoder.c'),
        (Join-Path $native 'libbrotlidec.a'),(Join-Path $native 'libbrotlicommon.a'),
        '-Oz','--no-entry','-sSTANDALONE_WASM=1','-sALLOW_MEMORY_GROWTH=1','-sMAXIMUM_MEMORY=1073741824',
        '-sINITIAL_MEMORY=16777216','-sSTACK_SIZE=1048576',
        '-sEXPORTED_FUNCTIONS=["_bengine_brotli_decode","_malloc","_free"]','-o',$output)
    & $python @arguments
    if($LASTEXITCODE -ne 0){throw 'Brotli decoder compilation failed.'}
    Copy-Item -LiteralPath $output -Destination (Join-Path $PSScriptRoot '../../WebResources/brotli-decoder.wasm')
    Get-FileHash -LiteralPath $output -Algorithm SHA256
} finally {
    foreach($item in @(Get-ChildItem Env:)){if(!$snapshot.ContainsKey($item.Name)){Remove-Item -LiteralPath ('Env:'+$item.Name)}}
    foreach($name in $snapshot.Keys){Set-Item -LiteralPath ('Env:'+$name) -Value $snapshot[$name]}
}
