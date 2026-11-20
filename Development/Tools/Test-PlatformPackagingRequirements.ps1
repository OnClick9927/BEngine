[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineBinaries,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/PlatformPackagingRequirements')
)
$ErrorActionPreference = 'Stop'
$binaries = [IO.Path]::GetFullPath($EngineBinaries)
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$project = Join-Path $PSScriptRoot 'Tests/PlatformPackagingRequirements/PlatformPackagingRequirements.csproj'
$artifacts = Join-Path $output Build
& dotnet build $project -c Release --artifacts-path $artifacts "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if ($LASTEXITCODE -ne 0) { throw "Platform packaging test build failed; see $output/Build.log" }
& dotnet (Join-Path $artifacts 'bin/PlatformPackagingRequirements/release/PlatformPackagingRequirements.dll') $output $binaries *> (Join-Path $output Tests.log)
if ($LASTEXITCODE -ne 0) { throw "Platform packaging tests failed; see $output/Tests.log" }
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../src'))
$template = Join-Path $source 'Platform/Player/Hosts/Android/BEngine.Player.AndroidHost.csproj'
$exportedTemplate = Join-Path $binaries 'BuildHosts/Android/BEngine.Player.AndroidHost.csproj'
$property = [IO.File]::ReadAllText((Join-Path $output AndroidProperty.txt))
foreach ($item in @(@('Source', $template), @('Exported', $exportedTemplate))) {
    $evaluation = Join-Path $output ("Android$($item[0])Property.txt")
    & dotnet msbuild $item[1] -nologo -getProperty:AndroidStoreUncompressedFileExtensions $property "-p:BEngineContentRoot=$output/Payload" -p:BEngineDataDirectoryName=game_data *> $evaluation
    if ($LASTEXITCODE -ne 0) { throw "Android $($item[0]) template evaluation failed; see $evaluation" }
    $extensions = [IO.File]::ReadAllText($evaluation).Trim().Split(';')
    foreach ($expected in @('.archive.bin', '.data', '.model-2_0', '.z')) {
        if ($extensions -cnotcontains $expected) { throw "Android $($item[0]) template lost $expected" }
    }
}
foreach ($scope in @('Core', 'Platform')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $source $scope) -Recurse -File | Where-Object Extension -in '.cs', '.csproj', '.props', '.targets') {
        if ([IO.File]::ReadAllText($file.FullName).Contains('.bassetbundle')) { throw "Foundation depends on a content format: $($file.FullName)" }
    }
}
foreach ($removed in @('Packages/Compilation/Editor/Hosts', 'Packages/Compilation/Editor/RuntimeDistribution', 'Packages/Compilation/Editor/ExportPlayer.ps1')) {
    if (Test-Path -LiteralPath (Join-Path $source $removed)) { throw "Platform implementation remains under Core: $removed" }
}
$nativeResults = Join-Path $output NativeReferences
New-Item -ItemType Directory -Force -Path $nativeResults | Out-Null
$nativeChecks = @()
$nativeRuntimeAssemblies = @('BEngine.Core', 'BEngine', 'BEngine.Async', 'BEngine.Attributes',
    'BEngine.Build.Core', 'BEngine.Collections', 'BEngine.Instrumentation', 'BEngine.Math',
    'BEngine.Platform.Abstractions', 'BEngine.Serialization')
foreach ($context in @('Source', 'Exported')) {
    foreach ($target in @('Android', 'iOS')) {
        $native = if ($context -eq 'Source') {
            Join-Path $source "Platform/$target/Editor/Native/BEngine.$target.Native/BEngine.$target.Native.csproj"
        } else { Join-Path $binaries "BuildPlatform/BEngine.$target.Native/BEngine.$target.Native.csproj" }
        $evaluation = Join-Path $nativeResults "$context-$target.json"
        & dotnet msbuild $native -nologo -getItem:ProjectReference,Reference *> $evaluation
        if ($LASTEXITCODE -ne 0) { throw "Native project evaluation failed: $context $target" }
        $items = (Get-Content -LiteralPath $evaluation -Raw | ConvertFrom-Json).Items
        if ($context -eq 'Source') {
            $contracts = @($items.ProjectReference | Where-Object { $_.Identity -like '*BEngine.Platform.Contracts.csproj' })
            if ($contracts.Count -ne 1 -or -not (Test-Path -LiteralPath $contracts[0].FullPath)) {
                throw "Source native backend must reference the existing platform contract project: $target"
            }
        } else {
            $contracts = @($items.Reference | Where-Object Identity -EQ 'BEngine.Platform.Contracts')
            if ($contracts.Count -ne 1 -or -not (Test-Path -LiteralPath $contracts[0].HintPath)) {
                throw "Exported native backend must reference the shipped platform contract DLL: $target"
            }
            foreach ($assembly in $nativeRuntimeAssemblies) {
                $references = @($items.Reference | Where-Object Identity -EQ $assembly)
                if ($references.Count -ne 1 -or -not (Test-Path -LiteralPath $references[0].HintPath)) {
                    throw "Exported native backend must reference the shipped $assembly DLL: $target"
                }
            }
            $nativeChecks += "Exported $target foundation dependency references"
            foreach ($reference in $items.ProjectReference) {
                if (-not (Test-Path -LiteralPath $reference.FullPath)) { throw "Exported native backend references a missing project: $($reference.FullPath)" }
            }
        }
        $nativeChecks += "$context $target native contract reference"
    }
}
@{Passed=$true;Checks=$nativeChecks.Count;Tests=$nativeChecks} | ConvertTo-Json | Set-Content (Join-Path $nativeResults Result.json)
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
Write-Output 'PLATFORM_PACKAGING_OK|sourceAndExportedAndroidTemplates=2|foundationFormatLeaks=0'
