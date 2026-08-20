[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repositoryRoot 'Output'
$engineOutput = Join-Path $outputRoot 'BEgine'
$stageOutput = Join-Path $outputRoot '.BEgine.publish-staging'
$backupOutput = Join-Path $outputRoot '.BEgine.publish-backup'
$launcherProject = Join-Path $PSScriptRoot 'Core\BEngine.Launcher\BEngine.Launcher.csproj'
$playerProject = Join-Path $PSScriptRoot 'Core\BEngine.Player\BEngine.Player.csproj'
$coreResources = Join-Path $PSScriptRoot 'Core\Resources'
$coreEditorResources = Join-Path $PSScriptRoot 'Core\EditorResources'

foreach ($path in @($engineOutput, $stageOutput, $backupOutput)) {
    $parent = Split-Path -Parent $path
    if ([System.IO.Path]::GetFullPath($parent) -ne [System.IO.Path]::GetFullPath($outputRoot)) {
        throw "Engine export path escaped Output: $path"
    }
}

if (Test-Path -LiteralPath $stageOutput) {
    Remove-Item -LiteralPath $stageOutput -Recurse -Force
}
if (Test-Path -LiteralPath $backupOutput) {
    throw "A previous engine export backup still exists: $backupOutput"
}

New-Item -ItemType Directory -Path $stageOutput | Out-Null

& dotnet publish $launcherProject -c $Configuration --no-restore -o $stageOutput -m:1 `
    -p:BEngineEngineExportOwner=false
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed with exit code $LASTEXITCODE." }

& dotnet publish $playerProject -c $Configuration --no-restore -o $stageOutput -m:1 `
    -p:BEngineEngineExportOwner=false
if ($LASTEXITCODE -ne 0) { throw "Player publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath $coreResources -Destination (Join-Path $stageOutput 'Resources') -Recurse
Copy-Item -LiteralPath $coreEditorResources -Destination (Join-Path $stageOutput 'EditorResources') -Recurse

foreach ($required in @('BEngine.Launcher.exe', 'BEngine.Editor.exe', 'BEngine.Player.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageOutput $required))) {
        throw "Engine export is missing $required."
    }
}

if (Test-Path -LiteralPath $engineOutput) {
    Move-Item -LiteralPath $engineOutput -Destination $backupOutput
}
try {
    Move-Item -LiteralPath $stageOutput -Destination $engineOutput
    if (Test-Path -LiteralPath $backupOutput) {
        Remove-Item -LiteralPath $backupOutput -Recurse -Force
    }
}
catch {
    if (-not (Test-Path -LiteralPath $engineOutput) -and (Test-Path -LiteralPath $backupOutput)) {
        Move-Item -LiteralPath $backupOutput -Destination $engineOutput
    }
    throw
}

Write-Output "BENGINE_EXPORT_OK|$Configuration|$engineOutput"
