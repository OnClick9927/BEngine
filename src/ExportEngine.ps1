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
$playerBuildHosts = Join-Path $PSScriptRoot 'Core\BEngine.Player.Hosts'
$engineDefines = Join-Path $PSScriptRoot 'BEngine.Defines.targets'

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

# Build Hosts must not reference the mixed Launcher/Editor export directory. In particular, mobile AOT
# must never see the Windows-only Editor closure, so publish a clean Player runtime dependency set.
$buildRuntimeOutput = Join-Path $stageOutput 'BuildRuntime'
New-Item -ItemType Directory -Path $buildRuntimeOutput | Out-Null
& dotnet publish $playerProject -c $Configuration --no-restore -o $buildRuntimeOutput -m:1 `
    -p:BEngineEngineExportOwner=false
if ($LASTEXITCODE -ne 0) { throw "BuildRuntime publish failed with exit code $LASTEXITCODE." }

Copy-Item -LiteralPath $coreResources -Destination (Join-Path $stageOutput 'Resources') -Recurse
Copy-Item -LiteralPath $coreEditor -Destination (Join-Path $stageOutput 'Editor') -Recurse
$buildHostsOutput = Join-Path $stageOutput 'BuildHosts'
New-Item -ItemType Directory -Path $buildHostsOutput | Out-Null
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
Copy-Item -LiteralPath $engineDefines -Destination (Join-Path $stageOutput 'BEngine.Defines.targets')

foreach ($required in @('BEngine.Launcher.exe', 'BEngine.Editor.exe', 'BEngine.Player.exe',
        'BEngine.Defines.targets')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageOutput $required))) {
        throw "Engine export is missing $required."
    }
}

foreach ($requiredRuntime in @('BEngine.dll', 'BEngine.Player.dll',
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
