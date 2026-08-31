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
$launcherProject = Join-Path $PSScriptRoot 'Hub\BEngine.Launcher\BEngine.Launcher.csproj'
$playerProject = Join-Path $PSScriptRoot 'Core\BEngine.Player\BEngine.Player.csproj'
$coreResources = Join-Path $PSScriptRoot 'Core\Resources'
$coreEditor = Join-Path $PSScriptRoot 'Core\Editor'

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [int]$Attempts = 8,
        [int]$DelayMilliseconds = 250
    )

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

New-Item -ItemType Directory -Path $stageOutput | Out-Null

& dotnet publish $launcherProject -c $Configuration --no-restore -o $stageOutput -m:1 `
    -p:BEngineEngineExportOwner=false
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed with exit code $LASTEXITCODE." }

& dotnet publish $playerProject -c $Configuration --no-restore -o $stageOutput -m:1 `
    -p:BEngineEngineExportOwner=false
if ($LASTEXITCODE -ne 0) { throw "Player publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath $coreResources -Destination (Join-Path $stageOutput 'Resources') -Recurse
Copy-Item -LiteralPath $coreEditor -Destination (Join-Path $stageOutput 'Editor') -Recurse

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
    Move-DirectoryWithRetry -Source $engineOutput -Destination $backupOutput
}
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

Write-Output "BENGINE_EXPORT_OK|$Configuration|$engineOutput"
