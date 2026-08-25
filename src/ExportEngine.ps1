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

$enginePrefix = [System.IO.Path]::GetFullPath($engineOutput).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$runningEditor = @(Get-Process -Name 'BEngine.Editor' -ErrorAction SilentlyContinue | Where-Object {
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
    throw "BEngine Editor is running from Output/BEgine (PID: $processIds). Save your work and close it before exporting."
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

foreach ($hostName in @('BEngine.Launcher', 'BEngine.Editor', 'BEngine.Player')) {
    $runtimeConfigPath = Join-Path $stageOutput "$hostName.runtimeconfig.json"
    $depsPath = Join-Path $stageOutput "$hostName.deps.json"
    if (-not (Test-Path -LiteralPath $runtimeConfigPath) -or -not (Test-Path -LiteralPath $depsPath)) {
        throw "Engine export is missing .NET host metadata for $hostName."
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

if (Test-Path -LiteralPath $engineOutput) {
    [System.IO.Directory]::Move($engineOutput, $backupOutput)
}
try {
    Copy-Item -LiteralPath $stageOutput -Destination $engineOutput -Recurse
    if (Test-Path -LiteralPath $backupOutput) {
        Remove-Item -LiteralPath $backupOutput -Recurse -Force
    }
    Remove-Item -LiteralPath $stageOutput -Recurse -Force
}
catch {
    if (Test-Path -LiteralPath $engineOutput) {
        Remove-Item -LiteralPath $engineOutput -Recurse -Force
    }
    if (Test-Path -LiteralPath $backupOutput) {
        [System.IO.Directory]::Move($backupOutput, $engineOutput)
    }
    throw
}

Write-Output "BENGINE_EXPORT_OK|$Configuration|$engineOutput"
