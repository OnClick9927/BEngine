[CmdletBinding()]
param(
    [string]$EditorPath,
    [string]$RemoteBaseUrl = 'http://127.0.0.1:8080/bengine-2d-showcase',
    [string]$TargetId = 'windows'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http

$exampleRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $exampleRoot '..'))
$expectedBuildRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Build'))
$validationRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Temp/WindowsHotUpdateValidation'))
$playerOutput = [System.IO.Path]::GetFullPath((Join-Path $expectedBuildRoot 'bengine 2d showcase'))
$remoteOutput = [System.IO.Path]::GetFullPath((Join-Path $expectedBuildRoot 'hotres'))
$runtimePlayerOutput = [System.IO.Path]::GetFullPath((Join-Path $validationRoot 'player-runtime'))
$logsRoot = [System.IO.Path]::GetFullPath((Join-Path $validationRoot 'logs'))
$packageName = 'bengine-2d-showcase'
$persistentDataRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $runtimePlayerOutput 'sandbox'))
$cacheRoot = $persistentDataRoot
$v1ProofPath = [System.IO.Path]::GetFullPath((Join-Path $validationRoot 'game-v1.proof'))
$v1LogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'player-install-v1.log'))
$declinedV2ProofPath = [System.IO.Path]::GetFullPath((Join-Path $validationRoot 'game-v1-after-decline-v2.proof'))
$declinedV2LogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'player-decline-v2.log'))
$v2ProofPath = [System.IO.Path]::GetFullPath((Join-Path $validationRoot 'game-v2.proof'))
$v2LogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'player-install-v2.log'))
$declinedV1ProofPath = [System.IO.Path]::GetFullPath(
    (Join-Path $validationRoot 'game-v2-after-decline-v1.proof'))
$declinedV1LogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'player-decline-v1.log'))
$rollbackV1ProofPath = [System.IO.Path]::GetFullPath(
    (Join-Path $validationRoot 'game-v1-after-latest-rollback.proof'))
$rollbackV1LogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'player-rollback-v1.log'))
$emptySandboxLogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'player-empty-sandbox.log'))
$hfsV1PreflightLogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'hfs-v1-preflight.log'))
$hfsV2PreflightLogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'hfs-v2-preflight.log'))
$hfsRollbackV1PreflightLogPath = [System.IO.Path]::GetFullPath(
    (Join-Path $logsRoot 'hfs-rollback-v1-preflight.log'))
$manualLatestV1LogPath = [System.IO.Path]::GetFullPath((Join-Path $logsRoot 'manual-latest-v1.log'))
$applyProfile = Join-Path $exampleRoot 'Apply-HotUpdateProfile.ps1'
$hfsServerHeader = $null

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw 'Validate-WindowsHotUpdate.ps1 must run on Windows.'
}
if (-not [System.IO.File]::Exists((Join-Path $exampleRoot 'Project.yaml'))) {
    throw "The Example project root is invalid: '$exampleRoot'."
}
if ([string]::IsNullOrWhiteSpace($EditorPath)) {
    $EditorPath = Join-Path $repositoryRoot 'Output/BEgine/BEngine.Editor.exe'
}
$EditorPath = [System.IO.Path]::GetFullPath($EditorPath)
if (-not [System.IO.File]::Exists($EditorPath)) {
    throw "The exported BEngine Editor was not found: '$EditorPath'."
}
$remoteBaseUri = $null
if (-not [System.Uri]::TryCreate($RemoteBaseUrl.TrimEnd('/'),
        [System.UriKind]::Absolute, [ref]$remoteBaseUri) -or
    $remoteBaseUri.Scheme -notin @('http', 'https')) {
    throw "RemoteBaseUrl must be an absolute HTTP or HTTPS URL: '$RemoteBaseUrl'."
}
$RemoteBaseUrl = $remoteBaseUri.AbsoluteUri.TrimEnd('/')

function Clear-ExactDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedPath
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullExpected = [System.IO.Path]::GetFullPath($ExpectedPath)
    if (-not $fullPath.Equals($fullExpected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear unexpected directory '$fullPath'. Expected '$fullExpected'."
    }
    if ([System.IO.Directory]::Exists($fullPath)) {
        $resolved = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $fullPath).Path)
        if (-not $resolved.Equals($fullExpected, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Resolved directory '$resolved' differs from the approved target '$fullExpected'."
        }
        Get-ChildItem -LiteralPath $resolved -Force | Remove-Item -Recurse -Force
    }
    else {
        [System.IO.Directory]::CreateDirectory($fullPath) | Out-Null
    }
}

function ConvertTo-ProcessArgument {
    param([AllowEmptyString()][string]$Value)

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append([char]34)
    [int]$backslashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq [char]92) {
            $backslashes++
            continue
        }
        if ($character -eq [char]34) {
            [void]$builder.Append(('\' * (($backslashes * 2) + 1)))
            [void]$builder.Append([char]34)
            $backslashes = 0
            continue
        }
        if ($backslashes -gt 0) {
            [void]$builder.Append(('\' * $backslashes))
            $backslashes = 0
        }
        [void]$builder.Append($character)
    }
    if ($backslashes -gt 0) { [void]$builder.Append(('\' * ($backslashes * 2))) }
    [void]$builder.Append([char]34)
    return $builder.ToString()
}

function Stop-ProcessIncludingChildren {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process)

    $supportsProcessTree = @($Process.PSObject.Methods['Kill'].OverloadDefinitions | Where-Object {
        $_.IndexOf('Boolean', [System.StringComparison]::Ordinal) -ge 0
    }).Count -gt 0
    if ($supportsProcessTree) { $Process.Kill($true) }
    else { $Process.Kill() }
}

function Invoke-LoggedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 300,
        [switch]$ExpectFailure
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FileName
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment.Remove('BENGINE_PLAYER_PERSISTENT_DATA_PATH') | Out-Null
    if ($null -ne $start.PSObject.Properties['ArgumentList']) {
        foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    }
    else {
        $start.Arguments = (($Arguments | ForEach-Object {
            ConvertTo-ProcessArgument -Value $_
        }) -join ' ')
    }
    foreach ($entry in $Environment.GetEnumerator()) { $start.Environment[$entry.Key] = [string]$entry.Value }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw "Could not start '$FileName'." }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-ProcessIncludingChildren -Process $process
            throw "'$FileName' exceeded the $TimeoutSeconds second timeout."
        }
        $output = $standardOutput.GetAwaiter().GetResult()
        $errorOutput = $standardError.GetAwaiter().GetResult()
        $combined = $output + $errorOutput
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($LogPath)) | Out-Null
        [System.IO.File]::WriteAllText($LogPath, $combined, [System.Text.UTF8Encoding]::new($false))
        if ($ExpectFailure -and $process.ExitCode -eq 0) {
            throw "'$FileName' unexpectedly succeeded. See '$LogPath'."
        }
        if (-not $ExpectFailure -and $process.ExitCode -ne 0) {
            throw "'$FileName' exited with code $($process.ExitCode). See '$LogPath'."
        }
        return $combined
    }
    finally {
        $process.Dispose()
    }
}

function Read-SharedText {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.FileStream]::new(
        $Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite)
    try {
        $reader = [System.IO.StreamReader]::new(
            $stream, [System.Text.UTF8Encoding]::new($false), $true, 4096, $true)
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Copy-PlayerForValidation {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not [System.IO.Directory]::Exists($Source)) {
        throw "The Player payload to copy does not exist: '$Source'."
    }
    if ([System.IO.Directory]::Exists($Destination)) {
        throw "The validation Player destination must be empty: '$Destination'."
    }
    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($directory in [System.IO.Directory]::EnumerateDirectories(
            $Source, '*', [System.IO.SearchOption]::AllDirectories)) {
        $relative = [System.IO.Path]::GetRelativePath($Source, $directory)
        [System.IO.Directory]::CreateDirectory((Join-Path $Destination $relative)) | Out-Null
    }
    foreach ($file in [System.IO.Directory]::EnumerateFiles(
            $Source, '*', [System.IO.SearchOption]::AllDirectories)) {
        $relative = [System.IO.Path]::GetRelativePath($Source, $file)
        $target = Join-Path $Destination $relative
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
        [System.IO.File]::Copy($file, $target, $false)
    }
}

function Invoke-WindowPlayerUntilProof {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ProofPath,
        [Parameter(Mandatory = $true)][string]$PlayerLogPath,
        [Parameter(Mandatory = $true)][hashtable]$Environment,
        [int]$TimeoutSeconds = 60
    )

    foreach ($path in @($ProofPath, $PlayerLogPath)) {
        if ([System.IO.File]::Exists($path)) { [System.IO.File]::Delete($path) }
    }
    $start = [System.Diagnostics.ProcessStartInfo]::new($FileName)
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $false
    foreach ($name in @(
            'BENGINE_PLAYER_PERSISTENT_DATA_PATH',
            'BENGINE_ASSET_BUNDLE_REMOTE_URL',
            'BENGINE_HOTUPDATE_PROOF_PATH',
            'BENGINE_PLAYER_LOG_PATH',
            'BENGINE_AOT_AUTO_CHECK',
            'BENGINE_AOT_AUTO_CONFIRM_UPDATE',
            'BENGINE_AOT_AUTO_DECLINE_UPDATE',
            'BENGINE_AOT_AUTO_CONTINUE')) {
        $start.Environment.Remove($name) | Out-Null
    }
    foreach ($entry in $Environment.GetEnumerator()) {
        $start.Environment[$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw "Could not start packaged Player '$FileName'." }
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $playerLog = ''
        while ([DateTime]::UtcNow -lt $deadline) {
            if ([System.IO.File]::Exists($PlayerLogPath)) {
                $playerLog = Read-SharedText -Path $PlayerLogPath
            }
            if ([System.IO.File]::Exists($ProofPath) -and
                $playerLog.IndexOf('07_GAME_STARTED', [System.StringComparison]::Ordinal) -ge 0) {
                Start-Sleep -Milliseconds 500
                if ($process.HasExited) {
                    throw "The packaged Player exited immediately after entering the game: code " +
                        "$($process.ExitCode). See '$PlayerLogPath'."
                }
                return $playerLog
            }
            if ($process.HasExited) {
                throw "The packaged Player exited before entering the game: code $($process.ExitCode). " +
                    "See '$PlayerLogPath'."
            }
            Start-Sleep -Milliseconds 100
        }
        throw "The packaged Player did not enter the game within $TimeoutSeconds seconds. " +
            "See '$PlayerLogPath'."
    }
    finally {
        if (-not $process.HasExited) {
            Stop-ProcessIncludingChildren -Process $process
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

function Get-HttpBytes {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [int]$TimeoutSeconds = 10
    )

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    try {
        $response = $client.GetAsync($Uri).GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) {
                throw "HTTP $([int]$response.StatusCode) $($response.ReasonPhrase)"
            }
            $serverHeader = $response.Headers.Server.ToString()
            if ([string]::IsNullOrWhiteSpace($serverHeader) -or
                $serverHeader.IndexOf('HFS', [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                throw "The endpoint identified itself as '$serverHeader', not Rejetto HFS."
            }
            $script:hfsServerHeader = $serverHeader
            return ,$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        }
        finally { $response.Dispose() }
    }
    catch {
        throw "HFS could not serve '$Uri'. Start Rejetto HFS on port 8080 and publish " +
            "'$remoteOutput/$packageName' as the real folder '$packageName'. $($_.Exception.Message)"
    }
    finally { $client.Dispose() }
}

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashText = [System.BitConverter]::ToString($algorithm.ComputeHash($Bytes))
        return $hashText.Replace('-', '').ToLowerInvariant()
    }
    finally { $algorithm.Dispose() }
}

function Set-RemoteLatestVersionNumber {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$FromVersion,
        [Parameter(Mandatory = $true)][string]$ToVersion,
        [Parameter(Mandatory = $true)][string]$EvidencePath
    )

    [byte[]]$beforeBytes = [System.IO.File]::ReadAllBytes($Path)
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $beforeText = $utf8.GetString($beforeBytes)
    $fromToken = '"version":"' + $FromVersion + '"'
    $toToken = '"version":"' + $ToVersion + '"'
    $first = $beforeText.IndexOf($fromToken, [System.StringComparison]::Ordinal)
    if ($first -lt 0 -or
        $first -ne $beforeText.LastIndexOf($fromToken, [System.StringComparison]::Ordinal)) {
        throw "latest.json must contain exactly one canonical '$fromToken' field."
    }
    $afterText = $beforeText.Substring(0, $first) + $toToken +
        $beforeText.Substring($first + $fromToken.Length)
    $after = $afterText | ConvertFrom-Json
    if (-not ([string]$after.version).Equals($ToVersion, [System.StringComparison]::Ordinal)) {
        throw "Editing the latest version field did not produce '$ToVersion'."
    }
    [byte[]]$afterBytes = $utf8.GetBytes($afterText)
    $temporary = $Path + '.manual-' + [System.Guid]::NewGuid().ToString('N') + '.tmp'
    $backup = $Path + '.manual-' + [System.Guid]::NewGuid().ToString('N') + '.backup'
    try {
        $stream = [System.IO.FileStream]::new(
            $temporary, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None, 4096, [System.IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($afterBytes, 0, $afterBytes.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }
        [System.IO.File]::Replace($temporary, $Path, $backup, $true)
    }
    finally {
        if ([System.IO.File]::Exists($temporary)) {
            [System.IO.File]::Delete($temporary)
        }
        if ([System.IO.File]::Exists($backup)) {
            [System.IO.File]::Delete($backup)
        }
    }
    [byte[]]$verifiedBytes = [System.IO.File]::ReadAllBytes($Path)
    if ($verifiedBytes.Length -ne $afterBytes.Length -or
        (Get-BytesSha256 -Bytes $verifiedBytes) -ne (Get-BytesSha256 -Bytes $afterBytes)) {
        throw 'The manually edited latest.json could not be verified after its atomic replacement.'
    }
    [System.IO.File]::WriteAllLines($EvidencePath, @(
        'changedField=version',
        "from=$FromVersion",
        "to=$ToVersion",
        "beforeSha256=$(Get-BytesSha256 -Bytes $beforeBytes)",
        "afterSha256=$(Get-BytesSha256 -Bytes $afterBytes)",
        "beforeBytes=$($beforeBytes.Length)",
        "afterBytes=$($afterBytes.Length)"
    ), [System.Text.UTF8Encoding]::new($false))
}

function Assert-HfsPublication {
    param(
        [Parameter(Mandatory = $true)][string]$LocalPackage,
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$EvidencePath
    )

    $localLatestPath = Join-Path $LocalPackage 'latest.json'
    $localLatestBytes = [System.IO.File]::ReadAllBytes($localLatestPath)
    [byte[]]$servedLatestBytes = Get-HttpBytes -Uri "$BaseUrl/latest.json"
    $localLatestHash = Get-BytesSha256 -Bytes $localLatestBytes
    $servedLatestHash = Get-BytesSha256 -Bytes $servedLatestBytes
    if ($localLatestBytes.Length -ne $servedLatestBytes.Length -or
        $localLatestHash -ne $servedLatestHash) {
        throw "HFS '$BaseUrl/latest.json' does not match '$localLatestPath'. " +
            "local=$localLatestHash/$($localLatestBytes.Length), " +
            "served=$servedLatestHash/$($servedLatestBytes.Length)."
    }

    $latest = [System.Text.Encoding]::UTF8.GetString($localLatestBytes) | ConvertFrom-Json
    $localVersionPath = Join-Path $LocalPackage "$($latest.version)/version.json"
    if (-not [System.IO.File]::Exists($localVersionPath)) {
        throw "The immutable version metadata referenced by latest.json is missing: '$localVersionPath'."
    }
    [byte[]]$localVersionBytes = [System.IO.File]::ReadAllBytes($localVersionPath)
    [byte[]]$servedVersionBytes = Get-HttpBytes -Uri "$BaseUrl/$($latest.version)/version.json"
    $localVersionHash = Get-BytesSha256 -Bytes $localVersionBytes
    $servedVersionHash = Get-BytesSha256 -Bytes $servedVersionBytes
    if ($localVersionBytes.Length -ne $servedVersionBytes.Length -or
        $localVersionHash -ne $servedVersionHash) {
        throw "HFS served version metadata that differs from '$localVersionPath'. " +
            "local=$localVersionHash/$($localVersionBytes.Length), " +
            "served=$servedVersionHash/$($servedVersionBytes.Length)."
    }
    $version = [System.Text.Encoding]::UTF8.GetString($localVersionBytes) | ConvertFrom-Json
    if (-not ([string]$version.packageName).Equals(
            $packageName, [System.StringComparison]::Ordinal) -or
        -not ([string]$version.version).Equals(
            [string]$latest.version, [System.StringComparison]::Ordinal)) {
        throw 'latest.json and its immutable version.json disagree.'
    }
    $localCatalogPath = Join-Path $LocalPackage "$($latest.version)/$($version.catalogFile)"
    if (-not [System.IO.File]::Exists($localCatalogPath)) {
        throw "The catalog referenced by version.json is missing: '$localCatalogPath'."
    }
    $localCatalogBytes = [System.IO.File]::ReadAllBytes($localCatalogPath)
    [byte[]]$servedCatalogBytes = Get-HttpBytes `
        -Uri "$BaseUrl/$($latest.version)/$($version.catalogFile)"
    $localCatalogHash = Get-BytesSha256 -Bytes $localCatalogBytes
    $servedCatalogHash = Get-BytesSha256 -Bytes $servedCatalogBytes
    if ($localCatalogBytes.Length -ne $servedCatalogBytes.Length -or
        $localCatalogHash -ne $servedCatalogHash) {
        throw "HFS served a catalog that differs from '$localCatalogPath'. " +
            "local=$localCatalogHash/$($localCatalogBytes.Length), " +
            "served=$servedCatalogHash/$($servedCatalogBytes.Length)."
    }

    [System.IO.File]::WriteAllLines($EvidencePath, @(
        "server=$hfsServerHeader",
        "baseUrl=$BaseUrl",
        "localPackage=$LocalPackage",
        "latestSha256=$localLatestHash",
        "latestBytes=$($localLatestBytes.Length)",
        "versionSha256=$localVersionHash",
        "versionBytes=$($localVersionBytes.Length)",
        "catalogSha256=$localCatalogHash",
        "catalogBytes=$($localCatalogBytes.Length)"
    ), [System.Text.UTF8Encoding]::new($false))
}

function Assert-PlayerHttpTransfers {
    param(
        [Parameter(Mandatory = $true)][string]$Contents,
        [Parameter(Mandatory = $true)][int]$ExpectedDownloadedBundles,
        [Parameter(Mandatory = $true)][long]$ExpectedDownloadedBytes,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $eventMarker = 'BENGINE_FLOW|04_HTTP_TRANSFER|'
    $eventLines = @($Contents -split '\r?\n' | Where-Object {
        $_.IndexOf($eventMarker, [System.StringComparison]::Ordinal) -ge 0
    })
    if ($eventLines.Count -eq 0) {
        throw "Player startup log '$LogPath' contains no HTTP transfer diagnostics."
    }

    $eventPattern = '\|BENGINE_FLOW\|04_HTTP_TRANSFER\|' +
        'resource=(?<resource>VersionPointer|VersionMetadata|Catalog|Bundle);' +
        'result=(?<result>[^;\r\n]+);status=(?<status>[^;\r\n]+);' +
        'bytes=(?<bytes>\d+);attempt=(?<attempt>\d+);endpoint=(?<endpoint>\S+)\s*$'
    $transfers = @()
    foreach ($line in $eventLines) {
        $match = [System.Text.RegularExpressions.Regex]::Match(
            $line, $eventPattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $match.Success) {
            throw "Player startup log '$LogPath' contains a malformed HTTP transfer event: $line"
        }

        [long]$receivedBytes = 0
        [int]$attempt = 0
        if (-not [long]::TryParse($match.Groups['bytes'].Value,
                [System.Globalization.NumberStyles]::None,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [ref]$receivedBytes) -or
            -not [int]::TryParse($match.Groups['attempt'].Value,
                [System.Globalization.NumberStyles]::None,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [ref]$attempt)) {
            throw "Player startup log '$LogPath' contains an out-of-range HTTP transfer count: $line"
        }

        $resource = $match.Groups['resource'].Value
        $result = $match.Groups['result'].Value
        $status = $match.Groups['status'].Value
        $endpoint = $match.Groups['endpoint'].Value
        if ($endpoint.IndexOf('?', [System.StringComparison]::Ordinal) -ge 0 -or
            $endpoint.IndexOf('#', [System.StringComparison]::Ordinal) -ge 0) {
            throw "HTTP transfer endpoint exposed query or fragment data: '$endpoint'."
        }
        $endpointUri = $null
        if (-not [System.Uri]::TryCreate($endpoint, [System.UriKind]::Absolute,
                [ref]$endpointUri) -or
            $endpointUri.Scheme -notin @('http', 'https') -or
            -not [string]::IsNullOrEmpty($endpointUri.UserInfo) -or
            -not [string]::IsNullOrEmpty($endpointUri.Query) -or
            -not [string]::IsNullOrEmpty($endpointUri.Fragment)) {
            throw "HTTP transfer endpoint is not a redacted absolute HTTP URI: '$endpoint'."
        }

        $hasExpectedSuffix = switch ($resource) {
            'VersionPointer' {
                $endpoint.EndsWith('/latest.json', [System.StringComparison]::OrdinalIgnoreCase)
            }
            'VersionMetadata' {
                $endpoint.EndsWith('/version.json', [System.StringComparison]::OrdinalIgnoreCase)
            }
            'Catalog' {
                $endpoint.EndsWith('/catalog.json', [System.StringComparison]::OrdinalIgnoreCase)
            }
            'Bundle' {
                $endpoint.EndsWith('.bassetbundle', [System.StringComparison]::OrdinalIgnoreCase)
            }
            default { $false }
        }
        if (-not $hasExpectedSuffix) {
            throw "HTTP transfer endpoint '$endpoint' does not match resource '$resource'."
        }

        $successful = $result.Equals('Success', [System.StringComparison]::Ordinal)
        if ($successful -and
            ($status -ne '200' -or $receivedBytes -le 0 -or $attempt -lt 1)) {
            throw "Successful HTTP transfer must report status=200, bytes>0 and attempt>=1: $line"
        }
        $transfers += [pscustomobject]@{
            Resource = $resource
            Successful = $successful
            Bytes = $receivedBytes
            Attempt = $attempt
            Endpoint = $endpoint
        }
    }

    foreach ($resource in @('VersionPointer', 'VersionMetadata', 'Catalog')) {
        $successes = @($transfers | Where-Object {
            $_.Resource -eq $resource -and $_.Successful
        })
        if ($successes.Count -eq 0) {
            throw "Player startup log '$LogPath' has no successful $resource HTTP transfer."
        }
    }

    $bundleSuccesses = @($transfers | Where-Object {
        $_.Resource -eq 'Bundle' -and $_.Successful
    })
    $bundleTransfers = @($transfers | Where-Object { $_.Resource -eq 'Bundle' })
    if ($ExpectedDownloadedBundles -eq 0 -and $bundleTransfers.Count -ne 0) {
        throw "Player HTTP diagnostics reported Bundle transfer attempts during a zero-download " +
            "update: $($eventLines -join '; ')."
    }
    [long]$bundleBytes = 0
    foreach ($transfer in $bundleSuccesses) { $bundleBytes += [long]$transfer.Bytes }
    if ($bundleSuccesses.Count -ne $ExpectedDownloadedBundles -or
        $bundleBytes -ne $ExpectedDownloadedBytes) {
        throw "Player HTTP diagnostics reported $($bundleSuccesses.Count) successful bundle(s)/" +
            "$bundleBytes byte(s), expected $ExpectedDownloadedBundles bundle(s)/" +
            "$ExpectedDownloadedBytes byte(s)."
    }
}

function Assert-OrderedStartupLog {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedTargetVersion,
        [AllowEmptyString()][string]$ExpectedPreviousVersion = '',
        [Parameter(Mandatory = $true)][int]$ExpectedDownloadedBundles,
        [Parameter(Mandatory = $true)][long]$ExpectedDownloadedBytes,
        [switch]$RequireSplash
    )

    if (-not [System.IO.File]::Exists($Path)) { throw "Player startup log is missing: '$Path'." }
    $contents = Read-SharedText -Path $Path
    $stages = @()
    if ($RequireSplash) {
        $stages += @('01_SPLASH_SHOWN', '01_SPLASH_PLAYBACK_COMPLETED')
    }
    $stages += @('02_CORE_AOT_BOOTSTRAP_COMPLETED', '03_AOT_ASSEMBLY_ACTIVATED',
        '03_AOT_SCENE_STARTED')
    if ($RequireSplash) { $stages += '01_SPLASH_CLOSED' }
    $stages += @('04_UPDATE_CHECK_STARTED', '04_UPDATE_PLAN_READY',
        '04_UPDATE_CONFIRMATION_REQUIRED', '04_UPDATE_CONFIRMED', '04_CONTENT_ACTIVATED',
        '04_GAME_CONTENT_READY', '05_HOTUPDATE_INJECTED', '06_SCENE_LOADED', '07_GAME_STARTED')
    $cursor = -1
    foreach ($stage in $stages) {
        $index = $contents.IndexOf($stage, $cursor + 1, [System.StringComparison]::Ordinal)
        if ($index -lt 0) {
            throw "Player startup log '$Path' is missing ordered stage '$stage'."
        }
        $cursor = $index
    }
    if ($contents.IndexOf('hasUpdates=True', [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Player startup log '$Path' did not report an available content update."
    }
    foreach ($required in @(
            ("04_UPDATE_PLAN_READY|target=$ExpectedTargetVersion;hasUpdates=True;" +
                "bundles=$ExpectedDownloadedBundles;bytes=$ExpectedDownloadedBytes"),
            ("04_UPDATE_CONFIRMATION_REQUIRED|target=$ExpectedTargetVersion;" +
                "bundles=$ExpectedDownloadedBundles;bytes=$ExpectedDownloadedBytes;"),
            ("04_UPDATE_CONFIRMED|target=$ExpectedTargetVersion;" +
                "bundles=$ExpectedDownloadedBundles;bytes=$ExpectedDownloadedBytes"),
            "04_CONTENT_ACTIVATED|previous=$ExpectedPreviousVersion;active=$ExpectedTargetVersion;")) {
        if ($contents.IndexOf($required, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "Player startup log '$Path' does not prove '$required'."
        }
    }
    if ($contents.IndexOf('BENGINE_AOT_UI_READY|', [System.StringComparison]::Ordinal) -lt 0) {
        throw "Player startup log '$Path' did not prove that the AOT UI document and controls loaded."
    }
    if ($RequireSplash -and $contents.IndexOf('01_SPLASH_SHOWN|visible=True',
            [System.StringComparison]::Ordinal) -lt 0) {
        throw "Player startup log '$Path' did not prove that the configured splash was visible."
    }
    if ($contents.IndexOf('BENGINE_LOG|level=Error|', [System.StringComparison]::Ordinal) -ge 0) {
        throw "Player startup log '$Path' contains an engine error."
    }
    $activation = [System.Text.RegularExpressions.Regex]::Match(
        $contents,
        '04_CONTENT_ACTIVATED[^\r\n]*downloadedBundles=(\d+);downloadedBytes=(\d+)',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $activation.Success) {
        throw "Player startup log '$Path' does not contain download counts for content activation."
    }
    $actualBundles = [int]::Parse($activation.Groups[1].Value,
        [System.Globalization.CultureInfo]::InvariantCulture)
    $actualBytes = [long]::Parse($activation.Groups[2].Value,
        [System.Globalization.CultureInfo]::InvariantCulture)
    if ($actualBundles -ne $ExpectedDownloadedBundles -or
        $actualBytes -ne $ExpectedDownloadedBytes) {
        throw "Player downloaded $actualBundles bundle(s)/$actualBytes byte(s), expected " +
            "$ExpectedDownloadedBundles bundle(s)/$ExpectedDownloadedBytes byte(s)."
    }
    Assert-PlayerHttpTransfers -Contents $contents `
        -ExpectedDownloadedBundles $ExpectedDownloadedBundles `
        -ExpectedDownloadedBytes $ExpectedDownloadedBytes -LogPath $Path
}

function Assert-DownloadedCache {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RemotePackage,
        [Parameter(Mandatory = $true)]$Version,
        [Parameter(Mandatory = $true)][object[]]$ExpectedBundles,
        [Parameter(Mandatory = $true)][string[]]$ExpectedObjectHashes,
        [Parameter(Mandatory = $true)][string[]]$ExpectedCatalogHashes,
        [AllowEmptyString()][string]$ExpectedPreviousVersion = ''
    )

    $activePath = Join-Path $Root 'active.json'
    if (-not [System.IO.File]::Exists($activePath)) {
        throw "The default Player sandbox cache has no active pointer: '$activePath'."
    }
    $active = Get-Content -Raw -LiteralPath $activePath | ConvertFrom-Json
    if ([string]$active.packageName -ne [string]$Version.packageName -or
        [string]$active.version -ne [string]$Version.version -or
        [string]$active.catalogSha256 -ne [string]$Version.catalogSha256) {
        throw "The default Player sandbox cache did not activate remote '$($Version.version)'. See '$activePath'."
    }

    $catalogHash = ([string]$Version.catalogSha256).ToLowerInvariant()
    $cachedCatalogPath = Join-Path $Root "catalogs/$catalogHash.json"
    if (-not [System.IO.File]::Exists($cachedCatalogPath)) {
        throw "The downloaded '$($Version.version)' catalog is missing from the Player sandbox: '$cachedCatalogPath'."
    }
    $remoteCatalogPath = Join-Path $RemotePackage "$($Version.version)/$($Version.catalogFile)"
    $cachedCatalogHash = (Get-FileHash -LiteralPath $cachedCatalogPath -Algorithm SHA256).Hash
    $remoteCatalogHash = (Get-FileHash -LiteralPath $remoteCatalogPath -Algorithm SHA256).Hash
    if (-not $cachedCatalogHash.Equals($remoteCatalogHash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The cached V1 catalog differs from the catalog published through HFS."
    }
    $cachedCatalog = Get-Content -Raw -LiteralPath $cachedCatalogPath | ConvertFrom-Json
    $aotAssets = @($cachedCatalog.assets | Where-Object {
        $address = [string]$_.address
        $address.Equals('Assets/Aot', [System.StringComparison]::OrdinalIgnoreCase) -or
        $address.StartsWith('Assets/Aot/', [System.StringComparison]::OrdinalIgnoreCase) -or
        $address.EndsWith('/AOT.dll', [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($aotAssets.Count -ne 0) {
        throw "The sandbox catalog contains main-package AOT content: $($aotAssets.address -join ', ')."
    }

    foreach ($descriptor in $ExpectedBundles) {
        $expectedHash = ([string]$descriptor.sha256).ToLowerInvariant()
        $cachedBundlePath = Join-Path $Root "objects/$expectedHash.bassetbundle"
        if (-not [System.IO.File]::Exists($cachedBundlePath)) {
            throw "HFS bundle '$($descriptor.name)' was not downloaded into '$cachedBundlePath'."
        }
        $cachedFile = Get-Item -LiteralPath $cachedBundlePath
        $cachedHash = (Get-FileHash -LiteralPath $cachedBundlePath -Algorithm SHA256).Hash
        if ($cachedFile.Length -ne [long]$descriptor.size -or
            -not $cachedHash.Equals($expectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Downloaded cache object '$cachedBundlePath' failed its size/hash proof."
        }

        $remoteBundleRelativePath = "$($Version.version)/bundles/$($descriptor.fileName)"
        $remoteBundlePath = Join-Path $RemotePackage $remoteBundleRelativePath
        if (-not [System.IO.File]::Exists($remoteBundlePath)) {
            throw "The corresponding HFS source bundle is missing: '$remoteBundlePath'."
        }
        $remoteHash = (Get-FileHash -LiteralPath $remoteBundlePath -Algorithm SHA256).Hash
        if (-not $remoteHash.Equals($cachedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Cached bundle '$cachedBundlePath' differs from HFS source '$remoteBundlePath'."
        }
    }

    $expectedObjectSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($hash in $ExpectedObjectHashes) { [void]$expectedObjectSet.Add($hash.ToLowerInvariant()) }
    $cachedObjects = @(Get-ChildItem -LiteralPath (Join-Path $Root 'objects') -File -Force)
    $actualObjectHashes = @($cachedObjects | ForEach-Object {
        if (-not $_.Name.EndsWith('.bassetbundle', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "The sandbox object store contains an unexpected file '$($_.FullName)'."
        }
        $_.BaseName.ToLowerInvariant()
    })
    if ($actualObjectHashes.Count -ne $expectedObjectSet.Count -or
        @($actualObjectHashes | Where-Object { -not $expectedObjectSet.Contains($_) }).Count -ne 0) {
        throw "The sandbox object hashes differ from the expected immutable V1/V2 set. " +
            "actual=$($actualObjectHashes -join ','), expected=$($expectedObjectSet -join ',')."
    }

    $expectedCatalogSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($hash in $ExpectedCatalogHashes) { [void]$expectedCatalogSet.Add($hash.ToLowerInvariant()) }
    $cachedCatalogs = @(Get-ChildItem -LiteralPath (Join-Path $Root 'catalogs') -File -Force)
    $actualCatalogHashes = @($cachedCatalogs | ForEach-Object {
        if (-not $_.Name.EndsWith('.json', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "The sandbox catalog store contains an unexpected file '$($_.FullName)'."
        }
        $_.BaseName.ToLowerInvariant()
    })
    if ($actualCatalogHashes.Count -ne $expectedCatalogSet.Count -or
        @($actualCatalogHashes | Where-Object { -not $expectedCatalogSet.Contains($_) }).Count -ne 0) {
        throw "The sandbox catalog hashes differ from the expected V1/V2 set. " +
            "actual=$($actualCatalogHashes -join ','), expected=$($expectedCatalogSet -join ',')."
    }

    $previousPath = Join-Path $Root 'previous.json'
    if ([string]::IsNullOrWhiteSpace($ExpectedPreviousVersion)) {
        if ([System.IO.File]::Exists($previousPath)) {
            throw "The sandbox unexpectedly retained a previous release pointer: '$previousPath'."
        }
    }
    else {
        if (-not [System.IO.File]::Exists($previousPath)) {
            throw "The sandbox is missing its rollback pointer for '$ExpectedPreviousVersion'."
        }
        $previous = Get-Content -Raw -LiteralPath $previousPath | ConvertFrom-Json
        if (-not ([string]$previous.version).Equals(
                $ExpectedPreviousVersion, [System.StringComparison]::Ordinal)) {
            throw "The sandbox previous pointer targets '$($previous.version)', " +
                "expected '$ExpectedPreviousVersion'."
        }
    }

    foreach ($forbidden in @('AssetBundles', '_aot_builtin', 'AOT', 'BuiltIn', 'staging',
            'pending.json')) {
        if ([System.IO.Directory]::Exists((Join-Path $Root $forbidden)) -or
            [System.IO.File]::Exists((Join-Path $Root $forbidden))) {
            throw "The simplified sandbox contains forbidden AOT/BuiltIn or transient entry '$forbidden'."
        }
    }
    $allowedEntries = @('objects', 'catalogs', 'active.json', 'logs')
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPreviousVersion)) {
        $allowedEntries += 'previous.json'
    }
    $unexpectedEntries = @(Get-ChildItem -LiteralPath $Root -Force | Where-Object {
        $_.Name -notin $allowedEntries
    })
    if ($unexpectedEntries.Count -ne 0) {
        throw "The simplified sandbox contains unexpected entries: $($unexpectedEntries.Name -join ', ')."
    }
}

function Assert-NoLoosePackagedResources {
    param([Parameter(Mandatory = $true)][string]$Root)

    $forbiddenExtensions = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in @(
            '.png', '.jpg', '.jpeg', '.rgba', '.glsl', '.hlsl', '.wgsl', '.spv', '.metal', '.msl',
            '.shader', '.yaml', '.yml', '.uxml', '.uss', '.json', '.bmeta', '.cs', '.txt')) {
        [void]$forbiddenExtensions.Add($extension)
    }
    $looseFiles = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Force | Where-Object {
        $forbiddenExtensions.Contains($_.Extension)
    })
    if ($looseFiles.Count -ne 0) {
        throw "Packaged _data contains loose resource, metadata, or source files: " +
            ($looseFiles.FullName -join ', ')
    }
}

function Assert-LowercaseTree {
    param([Parameter(Mandatory = $true)][string]$Root)

    if (-not [System.IO.Directory]::Exists($Root)) {
        throw "Lowercase path audit root is missing: '$Root'."
    }
    $nonLowercase = @(Get-ChildItem -LiteralPath $Root -Recurse -Force | Where-Object {
        -not $_.Name.Equals($_.Name.ToLowerInvariant(), [System.StringComparison]::Ordinal)
    })
    if ($nonLowercase.Count -ne 0) {
        $relative = @($nonLowercase | ForEach-Object {
            [System.IO.Path]::GetRelativePath($Root, $_.FullName)
        })
        throw "Packaged file and directory names must all be lowercase: $($relative -join ', ')."
    }
}

function Test-FileContainsByteSequence {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][byte[]]$Sequence
    )

    if ($Sequence.Length -eq 0) { throw 'A binary search sequence cannot be empty.' }
    [int[]]$failure = [int[]]::new($Sequence.Length)
    [int]$prefixLength = 0
    for ([int]$index = 1; $index -lt $Sequence.Length; $index++) {
        while ($prefixLength -gt 0 -and $Sequence[$index] -ne $Sequence[$prefixLength]) {
            $prefixLength = $failure[$prefixLength - 1]
        }
        if ($Sequence[$index] -eq $Sequence[$prefixLength]) { $prefixLength++ }
        $failure[$index] = $prefixLength
    }

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        [byte[]]$buffer = [byte[]]::new(65536)
        [int]$matched = 0
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            for ([int]$index = 0; $index -lt $read; $index++) {
                while ($matched -gt 0 -and $buffer[$index] -ne $Sequence[$matched]) {
                    $matched = $failure[$matched - 1]
                }
                if ($buffer[$index] -eq $Sequence[$matched]) { $matched++ }
                if ($matched -eq $Sequence.Length) { return $true }
            }
        }
        return $false
    }
    finally { $stream.Dispose() }
}

function Assert-ArchiveDoesNotExposePlaintext {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][byte[]]$Sequence,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (Test-FileContainsByteSequence -Path $ArchivePath -Sequence $Sequence) {
        throw "Resource archive '$ArchivePath' exposes $Description as plaintext."
    }
}

function Assert-PlayerResourceArchives {
    param([Parameter(Mandatory = $true)][string]$PlayerRoot)

    $dataDirectory = Join-Path $PlayerRoot 'bengine 2d showcase_data'
    if (-not [System.IO.Directory]::Exists($dataDirectory)) {
        throw "The packaged Player data directory is missing: '$dataDirectory'."
    }
    $legacyResourceDirectory = Join-Path $dataDirectory 'res'
    if ([System.IO.Directory]::Exists($legacyResourceDirectory) -or
        [System.IO.File]::Exists($legacyResourceDirectory)) {
        throw "The packaged Player still contains the removed res layer: '$legacyResourceDirectory'."
    }

    $dataEntries = @(Get-ChildItem -LiteralPath $dataDirectory -Force)
    $unexpectedDataEntries = @($dataEntries | Where-Object {
        -not $_.PSIsContainer -or $_.Name -notin @('assembly', 'resources')
    })
    if ($dataEntries.Count -ne 2 -or $unexpectedDataEntries.Count -ne 0) {
        throw "Packaged _data must contain exactly the assembly and resources directories: " +
            ($dataEntries.Name -join ', ')
    }

    $resourcesDirectory = Join-Path $dataDirectory 'resources'
    $expectedArchives = @('player.bresources', 'aot.bresources')
    $resourceEntries = @(Get-ChildItem -LiteralPath $resourcesDirectory -Force)
    $unexpectedResourceEntries = @($resourceEntries | Where-Object {
        $_.PSIsContainer -or $_.Name -notin $expectedArchives
    })
    $missingArchives = @($expectedArchives | Where-Object {
        -not [System.IO.File]::Exists((Join-Path $resourcesDirectory $_))
    })
    if ($resourceEntries.Count -ne $expectedArchives.Count -or
        $unexpectedResourceEntries.Count -ne 0 -or $missingArchives.Count -ne 0) {
        throw "Packaged resources must contain exactly player.bresources and aot.bresources. " +
            "actual=$($resourceEntries.Name -join ','), missing=$($missingArchives -join ',')."
    }

    $archives = @($expectedArchives | ForEach-Object { Join-Path $resourcesDirectory $_ })
    foreach ($archivePath in $archives) {
        $archiveFile = Get-Item -LiteralPath $archivePath
        if ($archiveFile.Length -le 96) {
            throw "Packaged resource archive is empty or truncated: '$archivePath'."
        }
        $stream = [System.IO.File]::OpenRead($archivePath)
        try {
            [byte[]]$magic = [byte[]]::new(8)
            if ($stream.Read($magic, 0, $magic.Length) -ne $magic.Length -or
                [System.Text.Encoding]::ASCII.GetString($magic) -ne 'BENGBRES') {
                throw "Packaged resource archive has an invalid header: '$archivePath'."
            }
        }
        finally { $stream.Dispose() }
    }

    Assert-NoLoosePackagedResources -Root $dataDirectory
    $utf8 = [System.Text.Encoding]::UTF8
    $plaintextMarkers = @(
        [pscustomobject]@{
            Description = 'the runtime metadata address'
            Bytes = $utf8.GetBytes('Assets/__BEngine/Player/runtime.bmeta')
        },
        [pscustomobject]@{
            Description = 'the AOT scene address'
            Bytes = $utf8.GetBytes('Assets/Aot/AOT.scene.yaml')
        },
        [pscustomobject]@{
            Description = 'the runtime metadata magic'
            Bytes = $utf8.GetBytes('BENGBMET')
        },
        [pscustomobject]@{
            Description = 'AOT scene YAML'
            Bytes = $utf8.GetBytes('format: BEngine.Scene')
        },
        [pscustomobject]@{
            Description = 'PortableScene Vulkan Shader source'
            Bytes = $utf8.GetBytes('layout(location = 0) in vec2 aPosition;')
        },
        [pscustomobject]@{
            Description = 'PortableScene Direct3D Shader source'
            Bytes = $utf8.GetBytes('cbuffer SceneViewport : register(b0)')
        },
        [pscustomobject]@{
            Description = 'a PNG signature'
            Bytes = [byte[]]@(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)
        }
    )
    foreach ($archivePath in $archives) {
        foreach ($marker in $plaintextMarkers) {
            Assert-ArchiveDoesNotExposePlaintext -ArchivePath $archivePath `
                -Sequence $marker.Bytes -Description $marker.Description
        }
    }
}

function Assert-ProfileProof {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('v1', 'v2')][string]$Profile,
        [Parameter(Mandatory = $true)][string]$ExpectedImageHash
    )

    if (-not [System.IO.File]::Exists($Path)) { throw "HotUpdate proof is missing: '$Path'." }
    $proof = [System.IO.File]::ReadAllText($Path)
    $upperProfile = $Profile.ToUpperInvariant()
    foreach ($marker in @('HOTUPDATE_DEMO_OK', "profile=$Profile", "code=REMOTE-CODE-$upperProfile",
            "resource=RESOURCE-$upperProfile", "shader=SHADER-$upperProfile",
            "imageSha256=$ExpectedImageHash")) {
        if ($proof.IndexOf($marker, [System.StringComparison]::Ordinal) -lt 0) {
            throw "HotUpdate proof '$Path' is missing '$marker': $proof"
        }
    }
    $otherProfile = if ($Profile -eq 'v1') { 'V2' } else { 'V1' }
    if ($proof.IndexOf("REMOTE-CODE-$otherProfile", [System.StringComparison]::Ordinal) -ge 0 -or
        $proof.IndexOf("RESOURCE-$otherProfile", [System.StringComparison]::Ordinal) -ge 0 -or
        $proof.IndexOf("SHADER-$otherProfile", [System.StringComparison]::Ordinal) -ge 0) {
        throw "HotUpdate proof '$Path' mixed V1 and V2 content: $proof"
    }
    return $proof
}

function Assert-CatalogHasNoManagedSymbols {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $catalogFiles = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter 'catalog.json')
    if ($catalogFiles.Count -eq 0) {
        throw "$Label does not contain an AssetBundle catalog under '$Root'."
    }
    foreach ($catalogFile in $catalogFiles) {
        $catalog = Get-Content -Raw -LiteralPath $catalogFile.FullName | ConvertFrom-Json
        $symbols = @($catalog.assets | Where-Object {
            $_.assetType -eq 'ManagedSymbols' -or
            ([string]$_.address).EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase)
        })
        if ($symbols.Count -ne 0) {
            throw "$Label catalog '$($catalogFile.FullName)' contains disabled managed symbols: " +
                ($symbols.address -join ', ')
        }
    }
}

function Assert-PlayerHasNoAssetBundles {
    param([Parameter(Mandatory = $true)][string]$PlayerRoot)

    $bundles = @(Get-ChildItem -LiteralPath $PlayerRoot -Recurse -File -Force |
        Where-Object { $_.Name.EndsWith('.bassetbundle', [System.StringComparison]::OrdinalIgnoreCase) })
    if ($bundles.Count -ne 0) {
        throw "The Player main package must contain zero AssetBundles: $($bundles.FullName -join ', ')."
    }
    $assetBundleDirectories = @(Get-ChildItem -LiteralPath $PlayerRoot -Recurse -Directory -Force |
        Where-Object { $_.Name.Equals('AssetBundles', [System.StringComparison]::OrdinalIgnoreCase) })
    if ($assetBundleDirectories.Count -ne 0) {
        throw "The Player main package contains an AssetBundles directory: " +
            ($assetBundleDirectories.FullName -join ', ')
    }
    $remotePointers = @(Get-ChildItem -LiteralPath $PlayerRoot -Recurse -File -Force |
        Where-Object { $_.Name -in @('latest.json', 'catalog.json', 'active.json', 'previous.json') })
    if ($remotePointers.Count -ne 0) {
        throw "The Player payload contains remote AssetBundle metadata: " +
            ($remotePointers.FullName -join ', ')
    }

    $forbiddenGameFiles = @(Get-ChildItem -LiteralPath $PlayerRoot -Recurse -File -Force |
        Where-Object { $_.Name -in @('Main.scene.yaml', 'ReleaseInfo.txt', 'Release.shader',
            'ReleaseBadge.png', 'Game.dll') })
    if ($forbiddenGameFiles.Count -ne 0) {
        throw "The Player payload contains loose V1/V2 game content: " +
            ($forbiddenGameFiles.FullName -join ', ')
    }
}

function Assert-ColdSandboxHasNoGameContent {
    param([Parameter(Mandatory = $true)][string]$Root)

    if (-not [System.IO.Directory]::Exists($Root)) { return }
    foreach ($forbiddenPath in @('active.json', 'previous.json', 'pending.json',
            'AssetBundles', '_aot_builtin', 'AOT', 'BuiltIn')) {
        if ([System.IO.File]::Exists((Join-Path $Root $forbiddenPath)) -or
            [System.IO.Directory]::Exists((Join-Path $Root $forbiddenPath))) {
            throw "The cold Player sandbox already contains game content '$forbiddenPath'."
        }
    }
    foreach ($cacheDirectory in @('objects', 'catalogs', 'staging')) {
        $path = Join-Path $Root $cacheDirectory
        [string[]]$cachedFiles = @()
        if ([System.IO.Directory]::Exists($path)) {
            $cachedFiles = @([System.IO.Directory]::EnumerateFiles(
                $path, '*', [System.IO.SearchOption]::AllDirectories))
        }
        if ($cachedFiles.Count -ne 0) {
            throw "The cold Player sandbox already contains files under '$cacheDirectory'."
        }
    }
}

function Assert-RemoteHasNoAot {
    param([Parameter(Mandatory = $true)]$Catalog)

    $aotAssets = @($Catalog.assets | Where-Object {
        $address = [string]$_.address
        $address.Equals('Assets/Aot', [System.StringComparison]::OrdinalIgnoreCase) -or
        $address.StartsWith('Assets/Aot/', [System.StringComparison]::OrdinalIgnoreCase) -or
        $address.EndsWith('/AOT.dll', [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($aotAssets.Count -ne 0) {
        throw "HotRes contains AOT assets that belong in the Player: $($aotAssets.address -join ', ')."
    }
}

function Assert-RemoteProfileContent {
    param(
        [Parameter(Mandatory = $true)]$Catalog,
        [Parameter(Mandatory = $true)][ValidateSet('v1', 'v2')][string]$Version
    )

    if (-not ([string]$Catalog.version).Equals(
            $Version, [System.StringComparison]::Ordinal)) {
        throw "Remote catalog version '$($Catalog.version)' does not match '$Version'."
    }

    $requiredAssets = @{
        'Assets/Scenes/Main.scene.yaml' = 'Scene'
        'Assets/Resources/HotUpdate/ReleaseInfo.txt' = 'Text'
        'Assets/Resources/HotUpdate/Release.shader' = 'Shader'
        'Assets/Resources/HotUpdate/ReleaseBadge.png' = 'Texture'
        'Assets/__BEngine/HotUpdate/release.json' = 'ManagedCodeReleaseManifest'
        'Assets/__BEngine/HotUpdate/Game.dll' = 'ManagedAssembly'
    }
    foreach ($entry in $requiredAssets.GetEnumerator()) {
        $matches = @($Catalog.assets | Where-Object {
            ([string]$_.address).Equals($entry.Key, [System.StringComparison]::OrdinalIgnoreCase)
        })
        if ($matches.Count -ne 1) {
            throw "Remote $Version catalog must contain exactly one '$($entry.Key)' asset."
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$entry.Value) -and
            -not ([string]$matches[0].assetType).Equals(
                [string]$entry.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Remote $Version asset '$($entry.Key)' has type '$($matches[0].assetType)', " +
                "expected '$($entry.Value)'."
        }
    }
}

function Read-PublishedProfileRelease {
    param(
        [Parameter(Mandatory = $true)][string]$RemotePackage,
        [Parameter(Mandatory = $true)][ValidateSet('v1', 'v2')][string]$Version
    )

    $latestPath = Join-Path $RemotePackage 'latest.json'
    if (-not [System.IO.File]::Exists($latestPath)) {
        throw "The $Version content build did not publish '$latestPath'."
    }
    $latest = Get-Content -Raw -LiteralPath $latestPath | ConvertFrom-Json
    if (-not ([string]$latest.version).Equals($Version, [System.StringComparison]::Ordinal)) {
        throw "The latest pointer targets '$($latest.version)', expected '$Version'."
    }
    $latestProperties = @($latest.PSObject.Properties | Select-Object -ExpandProperty Name)
    $expectedLatestProperties = @('version')
    $unexpectedLatestProperties = @($latestProperties | Where-Object {
        $_ -notin $expectedLatestProperties
    })
    if ($latestProperties.Count -ne $expectedLatestProperties.Count -or
        $unexpectedLatestProperties.Count -ne 0 -or
        @($expectedLatestProperties | Where-Object { $_ -notin $latestProperties }).Count -ne 0) {
        throw 'latest.json is not the canonical version-only mutable pointer.'
    }
    $versionRoot = Join-Path $RemotePackage $Version
    $versionPath = Join-Path $versionRoot 'version.json'
    if (-not [System.IO.File]::Exists($versionPath)) {
        throw "The immutable remote release '$versionRoot' is incomplete."
    }
    $versionDocument = Get-Content -Raw -LiteralPath $versionPath | ConvertFrom-Json
    $catalogPath = Join-Path $versionRoot ([string]$versionDocument.catalogFile)
    if (-not [System.IO.File]::Exists($catalogPath)) {
        throw "The immutable remote release '$versionRoot' has no referenced catalog."
    }
    if (-not ([string]$versionDocument.version).Equals($Version, [System.StringComparison]::Ordinal) -or
        -not ([string]$versionDocument.packageName).Equals(
            $packageName, [System.StringComparison]::Ordinal)) {
        throw "The $Version latest pointer and immutable version document disagree."
    }
    $catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json
    Assert-RemoteHasNoAot -Catalog $catalog
    Assert-RemoteProfileContent -Catalog $catalog -Version $Version
    Assert-WindowsCatalogShaderPruning -Catalog $catalog -Label "Remote $Version catalog"
    $imageAssets = @($catalog.assets | Where-Object {
        $_.address -eq 'Assets/Resources/HotUpdate/ReleaseBadge.png'
    })
    if ($imageAssets.Count -ne 1 -or
        [string]$imageAssets[0].sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "The $Version catalog does not contain one hashed HotUpdate badge asset."
    }
    return [pscustomobject]@{
        Name = $Version
        Pointer = $latest
        Latest = $versionDocument
        Catalog = $catalog
        VersionRoot = $versionRoot
        CatalogPath = $catalogPath
        Bundles = @($catalog.bundles)
        ImageHash = ([string]$imageAssets[0].sha256).ToLowerInvariant()
    }
}

function Get-ImmutableDirectorySnapshot {
    param([Parameter(Mandatory = $true)][string]$Root)

    if (-not [System.IO.Directory]::Exists($Root)) {
        throw "Cannot snapshot missing immutable release '$Root'."
    }
    $snapshot = @{}
    $rootItem = Get-Item -LiteralPath $Root -Force
    $snapshot['directory:.'] = "creation=$($rootItem.CreationTimeUtc.Ticks)|" +
        "write=$($rootItem.LastWriteTimeUtc.Ticks)"
    foreach ($directory in Get-ChildItem -LiteralPath $Root -Recurse -Directory -Force) {
        $relative = [System.IO.Path]::GetRelativePath($Root, $directory.FullName).Replace('\', '/')
        $snapshot["directory:$relative"] = "creation=$($directory.CreationTimeUtc.Ticks)|" +
            "write=$($directory.LastWriteTimeUtc.Ticks)"
    }
    [int]$fileCount = 0
    foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File -Force) {
        $relative = [System.IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $snapshot["file:$relative"] = "length=$($file.Length)|sha256=$hash|" +
            "creation=$($file.CreationTimeUtc.Ticks)|write=$($file.LastWriteTimeUtc.Ticks)"
        $fileCount++
    }
    if ($fileCount -eq 0) { throw "Immutable release '$Root' contains no files." }
    return ,$snapshot
}

function Assert-ImmutableDirectorySnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][hashtable]$Expected
    )

    $actual = Get-ImmutableDirectorySnapshot -Root $Root
    if ($actual.Count -ne $Expected.Count -or
        @($actual.Keys | Where-Object {
            -not $Expected.ContainsKey($_) -or $Expected[$_] -ne $actual[$_]
        }).Count -ne 0) {
        throw "Immutable release '$Root' changed or was rebuilt."
    }
}

function Assert-ProfilePayloadsDiffer {
    param(
        [Parameter(Mandatory = $true)]$V1Catalog,
        [Parameter(Mandatory = $true)]$V2Catalog
    )

    foreach ($address in @(
            'Assets/Resources/HotUpdate/ReleaseInfo.txt',
            'Assets/Resources/HotUpdate/Release.shader',
            'Assets/Resources/HotUpdate/ReleaseBadge.png',
            'Assets/__BEngine/HotUpdate/release.json',
            'Assets/__BEngine/HotUpdate/Game.dll')) {
        $v1 = @($V1Catalog.assets | Where-Object { $_.address -eq $address })
        $v2 = @($V2Catalog.assets | Where-Object { $_.address -eq $address })
        if ($v1.Count -ne 1 -or $v2.Count -ne 1 -or
            ([string]$v1[0].sha256).Equals(
                [string]$v2[0].sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "V1 and V2 must publish different resource/Shader/C# payloads for '$address'."
        }
    }
}

function Assert-DeclinedUpdateLog {
    param(
        [Parameter(Mandatory = $true)][string]$Contents,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$InstalledVersion,
        [Parameter(Mandatory = $true)][string]$DeclinedVersion,
        [Parameter(Mandatory = $true)][int]$ExpectedPlannedBundles,
        [Parameter(Mandatory = $true)][long]$ExpectedPlannedBytes
    )

    $ordered = @('01_SPLASH_SHOWN', '01_SPLASH_PLAYBACK_COMPLETED',
        '02_CORE_AOT_BOOTSTRAP_COMPLETED', '03_AOT_ASSEMBLY_ACTIVATED',
        '03_AOT_SCENE_STARTED', '01_SPLASH_CLOSED', '04_UPDATE_CHECK_STARTED',
        '04_UPDATE_PLAN_READY', '04_GAME_CONTENT_READY', '04_UPDATE_CONFIRMATION_REQUIRED',
        '04_UPDATE_DECLINED', '05_HOTUPDATE_INJECTED', '06_SCENE_LOADED', '07_GAME_STARTED')
    $cursor = -1
    foreach ($stage in $ordered) {
        $index = $Contents.IndexOf($stage, $cursor + 1, [System.StringComparison]::Ordinal)
        if ($index -lt 0) { throw "Player log '$Path' is missing ordered decline stage '$stage'." }
        $cursor = $index
    }
    foreach ($required in @(
            ("04_UPDATE_PLAN_READY|target=$DeclinedVersion;hasUpdates=True;" +
                "bundles=$ExpectedPlannedBundles;bytes=$ExpectedPlannedBytes"),
            ("04_UPDATE_CONFIRMATION_REQUIRED|target=$DeclinedVersion;" +
                "bundles=$ExpectedPlannedBundles;bytes=$ExpectedPlannedBytes;canDecline=True"),
            ("04_UPDATE_DECLINED|target=$DeclinedVersion;" +
                "bundles=$ExpectedPlannedBundles;bytes=$ExpectedPlannedBytes"),
            "04_GAME_CONTENT_READY|version=$InstalledVersion;")) {
        if ($Contents.IndexOf($required, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "Player log '$Path' does not prove '$required'."
        }
    }
    foreach ($forbidden in @('04_UPDATE_CONFIRMED', '04_CONTENT_ACTIVATED')) {
        if ($Contents.IndexOf($forbidden, [System.StringComparison]::Ordinal) -ge 0) {
            throw "Declining $DeclinedVersion unexpectedly ran '$forbidden'."
        }
    }
    Assert-PlayerHttpTransfers -Contents $Contents -ExpectedDownloadedBundles 0 `
        -ExpectedDownloadedBytes 0 -LogPath $Path
    foreach ($failureMarker in @('BENGINE_FAIL', 'BENGINE_UNHANDLED', 'BENGINE_LOG|level=Error|')) {
        if ($Contents.IndexOf($failureMarker, [System.StringComparison]::Ordinal) -ge 0) {
            throw "Player log '$Path' contains '$failureMarker' while declining $DeclinedVersion."
        }
    }
}

function Assert-WindowsCatalogShaderPruning {
    param(
        [Parameter(Mandatory = $true)]$Catalog,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $packageShaders = @($Catalog.assets | Where-Object {
        $address = [string]$_.address
        $address.StartsWith('Assets/Packages/', [System.StringComparison]::OrdinalIgnoreCase) -and
        $address.IndexOf('/Resources/Shaders/', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    } | ForEach-Object { [string]$_.address })
    if ($packageShaders.Count -eq 0 -or
        @($packageShaders | Where-Object {
            $_.IndexOf('.webgpu.', [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        }).Count -ne 0) {
        throw "$Label contains no package Shaders or still contains a WebGPU Shader."
    }
    foreach ($marker in @('.direct3d.', '.vulkan.', '.opengl.')) {
        if (@($packageShaders | Where-Object {
            $_.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        }).Count -eq 0) {
            throw "$Label omitted the Windows '$marker' Shader fallback."
        }
    }
}

try {
    Clear-ExactDirectory -Path $expectedBuildRoot -ExpectedPath $expectedBuildRoot
    Clear-ExactDirectory -Path $validationRoot -ExpectedPath $validationRoot
    [System.IO.Directory]::CreateDirectory($logsRoot) | Out-Null

    & $applyProfile -Profile V1
    if (-not $?) { throw 'Applying HotUpdate profile V1 failed.' }
    Invoke-LoggedProcess -FileName $EditorPath `
        -Arguments @('--build-player', $exampleRoot, $playerOutput, $TargetId, 'v1') `
        -WorkingDirectory $repositoryRoot -LogPath (Join-Path $logsRoot 'build-player.log') | Out-Null
    Assert-PlayerHasNoAssetBundles -PlayerRoot $playerOutput
    Assert-PlayerResourceArchives -PlayerRoot $playerOutput
    Assert-ColdSandboxHasNoGameContent -Root (Join-Path $playerOutput 'sandbox')
    Copy-PlayerForValidation -Source $playerOutput -Destination $runtimePlayerOutput

    [string]$emptySandboxOutput = Invoke-LoggedProcess -FileName (Join-Path $runtimePlayerOutput `
            'bengine 2d showcase.exe') -Arguments @('--validate') `
        -WorkingDirectory $runtimePlayerOutput `
        -LogPath (Join-Path $logsRoot 'player-empty-sandbox-console.log') `
        -Environment @{
            BENGINE_ASSET_BUNDLE_REMOTE_URL = 'http://127.0.0.1:1/bengine-2d-showcase'
            BENGINE_PLAYER_LOG_PATH = $emptySandboxLogPath
        } -TimeoutSeconds 30 -ExpectFailure
    $emptySandboxLog = Read-SharedText -Path $emptySandboxLogPath
    if ($emptySandboxOutput.IndexOf('BENGINE_PLAYER_VALIDATION_OK|',
            [System.StringComparison]::Ordinal) -ge 0 -or
        $emptySandboxLog.IndexOf('07_GAME_STARTED', [System.StringComparison]::Ordinal) -ge 0) {
        throw 'A Player with no sandbox game content entered the game main loop.'
    }
    Assert-ColdSandboxHasNoGameContent -Root $cacheRoot
    Clear-ExactDirectory -Path $cacheRoot -ExpectedPath $cacheRoot

    Invoke-LoggedProcess -FileName $EditorPath `
        -Arguments @('--build-content-update', $exampleRoot, $remoteOutput, $TargetId, 'v1') `
        -WorkingDirectory $repositoryRoot -LogPath (Join-Path $logsRoot 'build-content-v1.log') | Out-Null

    $remotePackage = Join-Path $remoteOutput $packageName
    $v1Release = Read-PublishedProfileRelease -RemotePackage $remotePackage -Version v1
    Assert-HfsPublication -LocalPackage $remotePackage -BaseUrl $RemoteBaseUrl `
        -EvidencePath $hfsV1PreflightLogPath
    Assert-CatalogHasNoManagedSymbols -Root $remotePackage -Label 'Remote V1'
    $v1Snapshot = Get-ImmutableDirectorySnapshot -Root $v1Release.VersionRoot
    $v1ObjectHashes = @($v1Release.Bundles | ForEach-Object {
        ([string]$_.sha256).ToLowerInvariant()
    })
    if ($v1Release.Bundles.Count -lt 1) {
        throw 'Expected V1 to publish at least one AB for resource/Shader and C# content.'
    }
    [long]$v1DownloadedBytes = 0
    foreach ($descriptor in $v1Release.Bundles) {
        $v1DownloadedBytes += [long]$descriptor.size
    }
    if ($v1DownloadedBytes -le 0) {
        throw 'The expected V1 HFS download size must be greater than zero.'
    }

    $executable = Join-Path $runtimePlayerOutput 'bengine 2d showcase.exe'
    if (-not [System.IO.File]::Exists($executable)) {
        throw "The packaged Windows executable is missing: '$executable'."
    }
    $v1Log = Invoke-WindowPlayerUntilProof -FileName $executable `
        -WorkingDirectory $runtimePlayerOutput -ProofPath $v1ProofPath -PlayerLogPath $v1LogPath `
        -Environment @{
            BENGINE_ASSET_BUNDLE_REMOTE_URL = $RemoteBaseUrl
            BENGINE_HOTUPDATE_PROOF_PATH = $v1ProofPath
            BENGINE_PLAYER_LOG_PATH = $v1LogPath
            BENGINE_AOT_AUTO_CHECK = '1'
            BENGINE_AOT_AUTO_CONFIRM_UPDATE = '1'
            BENGINE_AOT_AUTO_CONTINUE = '1'
        } -TimeoutSeconds 90
    $v1Proof = Assert-ProfileProof -Path $v1ProofPath -Profile v1 `
        -ExpectedImageHash $v1Release.ImageHash
    Assert-OrderedStartupLog -Path $v1LogPath -ExpectedTargetVersion v1 `
        -ExpectedPreviousVersion '' -ExpectedDownloadedBundles $v1Release.Bundles.Count `
        -ExpectedDownloadedBytes $v1DownloadedBytes -RequireSplash
    Assert-DownloadedCache -Root $cacheRoot -RemotePackage $remotePackage `
        -Version $v1Release.Latest -ExpectedBundles $v1Release.Bundles `
        -ExpectedObjectHashes $v1ObjectHashes `
        -ExpectedCatalogHashes @(([string]$v1Release.Latest.catalogSha256).ToLowerInvariant())

    & $applyProfile -Profile V2
    if (-not $?) { throw 'Applying HotUpdate profile V2 failed.' }
    Invoke-LoggedProcess -FileName $EditorPath `
        -Arguments @('--build-content-update', $exampleRoot, $remoteOutput, $TargetId, 'v2') `
        -WorkingDirectory $repositoryRoot -LogPath (Join-Path $logsRoot 'build-content-v2.log') | Out-Null

    $v2Release = Read-PublishedProfileRelease -RemotePackage $remotePackage -Version v2
    Assert-HfsPublication -LocalPackage $remotePackage -BaseUrl $RemoteBaseUrl `
        -EvidencePath $hfsV2PreflightLogPath
    Assert-CatalogHasNoManagedSymbols -Root $remotePackage -Label 'Remote V1/V2'
    Assert-ImmutableDirectorySnapshot -Root $v1Release.VersionRoot -Expected $v1Snapshot
    $v2Snapshot = Get-ImmutableDirectorySnapshot -Root $v2Release.VersionRoot
    Assert-ProfilePayloadsDiffer -V1Catalog $v1Release.Catalog -V2Catalog $v2Release.Catalog

    $publishedEntries = @(Get-ChildItem -LiteralPath $remotePackage -Force |
        Select-Object -ExpandProperty Name)
    $unexpectedPublishedEntries = @($publishedEntries | Where-Object {
        $_ -notin @('latest.json', 'v1', 'v2')
    })
    if ($publishedEntries.Count -ne 3 -or $unexpectedPublishedEntries.Count -ne 0) {
        throw "HotRes must contain only immutable v1/v2 plus latest.json: " +
            ($publishedEntries -join ', ')
    }

    $v1HashSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($hash in $v1ObjectHashes) { [void]$v1HashSet.Add($hash) }
    $v2Downloads = @($v2Release.Bundles | Where-Object {
        -not $v1HashSet.Contains(([string]$_.sha256).ToLowerInvariant())
    })
    if ($v2Downloads.Count -lt 1) {
        throw 'V2 must require at least one new AB for changed resource/Shader and C# content.'
    }
    [long]$v2DownloadedBytes = 0
    foreach ($descriptor in $v2Downloads) { $v2DownloadedBytes += [long]$descriptor.size }
    $v2ObjectHashes = @($v2Release.Bundles | ForEach-Object {
        ([string]$_.sha256).ToLowerInvariant()
    })
    $allObjectHashes = @($v1ObjectHashes + $v2ObjectHashes | Sort-Object -Unique)
    $v1CatalogHash = ([string]$v1Release.Latest.catalogSha256).ToLowerInvariant()
    $v2CatalogHash = ([string]$v2Release.Latest.catalogSha256).ToLowerInvariant()

    $declinedV2Log = Invoke-WindowPlayerUntilProof -FileName $executable `
        -WorkingDirectory $runtimePlayerOutput -ProofPath $declinedV2ProofPath `
        -PlayerLogPath $declinedV2LogPath -Environment @{
            BENGINE_ASSET_BUNDLE_REMOTE_URL = $RemoteBaseUrl
            BENGINE_HOTUPDATE_PROOF_PATH = $declinedV2ProofPath
            BENGINE_PLAYER_LOG_PATH = $declinedV2LogPath
            BENGINE_AOT_AUTO_CHECK = '1'
            BENGINE_AOT_AUTO_DECLINE_UPDATE = '1'
            BENGINE_AOT_AUTO_CONTINUE = '1'
        } -TimeoutSeconds 90
    $declinedProof = Assert-ProfileProof -Path $declinedV2ProofPath -Profile v1 `
        -ExpectedImageHash $v1Release.ImageHash
    Assert-DeclinedUpdateLog -Contents $declinedV2Log -Path $declinedV2LogPath `
        -InstalledVersion v1 -DeclinedVersion v2 `
        -ExpectedPlannedBundles $v2Downloads.Count -ExpectedPlannedBytes $v2DownloadedBytes
    Assert-DownloadedCache -Root $cacheRoot -RemotePackage $remotePackage `
        -Version $v1Release.Latest -ExpectedBundles $v1Release.Bundles `
        -ExpectedObjectHashes $v1ObjectHashes -ExpectedCatalogHashes @($v1CatalogHash)

    $v2Log = Invoke-WindowPlayerUntilProof -FileName $executable `
        -WorkingDirectory $runtimePlayerOutput -ProofPath $v2ProofPath -PlayerLogPath $v2LogPath `
        -Environment @{
            BENGINE_ASSET_BUNDLE_REMOTE_URL = $RemoteBaseUrl
            BENGINE_HOTUPDATE_PROOF_PATH = $v2ProofPath
            BENGINE_PLAYER_LOG_PATH = $v2LogPath
            BENGINE_AOT_AUTO_CHECK = '1'
            BENGINE_AOT_AUTO_CONFIRM_UPDATE = '1'
            BENGINE_AOT_AUTO_CONTINUE = '1'
        } -TimeoutSeconds 90
    $v2Proof = Assert-ProfileProof -Path $v2ProofPath -Profile v2 `
        -ExpectedImageHash $v2Release.ImageHash
    Assert-OrderedStartupLog -Path $v2LogPath -ExpectedTargetVersion v2 `
        -ExpectedPreviousVersion v1 -ExpectedDownloadedBundles $v2Downloads.Count `
        -ExpectedDownloadedBytes $v2DownloadedBytes -RequireSplash
    Assert-DownloadedCache -Root $cacheRoot -RemotePackage $remotePackage `
        -Version $v2Release.Latest -ExpectedBundles $v2Release.Bundles `
        -ExpectedObjectHashes $allObjectHashes `
        -ExpectedCatalogHashes @($v1CatalogHash, $v2CatalogHash) -ExpectedPreviousVersion v1

    $v1Assembly = [System.Text.RegularExpressions.Regex]::Match(
        $v1Proof, '\|assembly=(?<id>[0-9a-f]{32})\|').Groups['id'].Value
    $v2Assembly = [System.Text.RegularExpressions.Regex]::Match(
        $v2Proof, '\|assembly=(?<id>[0-9a-f]{32})\|').Groups['id'].Value
    if ([string]::IsNullOrWhiteSpace($v1Assembly) -or
        [string]::IsNullOrWhiteSpace($v2Assembly) -or
        $v1Assembly.Equals($v2Assembly, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'V1 and V2 proofs did not demonstrate different injected C# assemblies.'
    }
    if ($declinedProof.IndexOf("assembly=$v1Assembly", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw 'Declining V2 did not execute the exact installed V1 C# assembly.'
    }

    Assert-ImmutableDirectorySnapshot -Root $v1Release.VersionRoot -Expected $v1Snapshot
    Assert-ImmutableDirectorySnapshot -Root $v2Release.VersionRoot -Expected $v2Snapshot
    $latestPath = Join-Path $remotePackage 'latest.json'
    $v1VersionPath = Join-Path $v1Release.VersionRoot 'version.json'
    $v2VersionPath = Join-Path $v2Release.VersionRoot 'version.json'
    [byte[]]$latestBeforeRollbackBytes = [System.IO.File]::ReadAllBytes($latestPath)
    [byte[]]$v1VersionBytes = [System.IO.File]::ReadAllBytes($v1VersionPath)
    [byte[]]$v2VersionBytes = [System.IO.File]::ReadAllBytes($v2VersionPath)
    $latestBeforeRollbackHash = Get-BytesSha256 -Bytes $latestBeforeRollbackBytes
    $v1VersionHash = Get-BytesSha256 -Bytes $v1VersionBytes
    $v2VersionHash = Get-BytesSha256 -Bytes $v2VersionBytes
    $latestBeforeRollback = [System.Text.Encoding]::UTF8.GetString(
        $latestBeforeRollbackBytes) | ConvertFrom-Json
    if (-not ([string]$latestBeforeRollback.version).Equals(
            'v2', [System.StringComparison]::Ordinal) -or
        ($latestBeforeRollbackBytes.Length -eq $v2VersionBytes.Length -and
            $latestBeforeRollbackHash -eq $v2VersionHash)) {
        throw 'Before rollback, latest.json is not an independent version-only V2 pointer.'
    }
    if ($v1VersionHash -eq $v2VersionHash) {
        throw 'V1 and V2 version documents unexpectedly have the same SHA256.'
    }

    Set-RemoteLatestVersionNumber -Path $latestPath -FromVersion v2 -ToVersion v1 `
        -EvidencePath $manualLatestV1LogPath

    [byte[]]$latestAfterRollbackBytes = [System.IO.File]::ReadAllBytes($latestPath)
    $latestAfterRollbackHash = Get-BytesSha256 -Bytes $latestAfterRollbackBytes
    $latestAfterRollback = [System.Text.Encoding]::UTF8.GetString(
        $latestAfterRollbackBytes) | ConvertFrom-Json
    if (-not ([string]$latestAfterRollback.version).Equals(
            'v1', [System.StringComparison]::Ordinal) -or
        ($latestAfterRollbackBytes.Length -eq $v1VersionBytes.Length -and
            $latestAfterRollbackHash -eq $v1VersionHash)) {
        throw 'Editing the one latest.json version value did not create an independent V1 pointer.'
    }
    if ($latestAfterRollbackHash -eq $latestBeforeRollbackHash) {
        throw 'Editing the latest.json version value did not change the V2 pointer.'
    }
    Assert-ImmutableDirectorySnapshot -Root $v1Release.VersionRoot -Expected $v1Snapshot
    Assert-ImmutableDirectorySnapshot -Root $v2Release.VersionRoot -Expected $v2Snapshot
    $publishedAfterRollback = @(Get-ChildItem -LiteralPath $remotePackage -Force |
        Select-Object -ExpandProperty Name)
    $unexpectedAfterRollback = @($publishedAfterRollback | Where-Object {
        $_ -notin @('latest.json', 'v1', 'v2')
    })
    if ($publishedAfterRollback.Count -ne 3 -or $unexpectedAfterRollback.Count -ne 0) {
        throw "Changing latest must leave only immutable v1/v2 plus latest.json: " +
            ($publishedAfterRollback -join ', ')
    }
    $rollbackV1Release = Read-PublishedProfileRelease -RemotePackage $remotePackage -Version v1
    Assert-HfsPublication -LocalPackage $remotePackage -BaseUrl $RemoteBaseUrl `
        -EvidencePath $hfsRollbackV1PreflightLogPath

    $declinedV1Log = Invoke-WindowPlayerUntilProof -FileName $executable `
        -WorkingDirectory $runtimePlayerOutput -ProofPath $declinedV1ProofPath `
        -PlayerLogPath $declinedV1LogPath -Environment @{
            BENGINE_ASSET_BUNDLE_REMOTE_URL = $RemoteBaseUrl
            BENGINE_HOTUPDATE_PROOF_PATH = $declinedV1ProofPath
            BENGINE_PLAYER_LOG_PATH = $declinedV1LogPath
            BENGINE_AOT_AUTO_CHECK = '1'
            BENGINE_AOT_AUTO_DECLINE_UPDATE = '1'
            BENGINE_AOT_AUTO_CONTINUE = '1'
        } -TimeoutSeconds 90
    $declinedV1Proof = Assert-ProfileProof -Path $declinedV1ProofPath -Profile v2 `
        -ExpectedImageHash $v2Release.ImageHash
    Assert-DeclinedUpdateLog -Contents $declinedV1Log -Path $declinedV1LogPath `
        -InstalledVersion v2 -DeclinedVersion v1 -ExpectedPlannedBundles 0 `
        -ExpectedPlannedBytes 0
    Assert-DownloadedCache -Root $cacheRoot -RemotePackage $remotePackage `
        -Version $v2Release.Latest -ExpectedBundles $v2Release.Bundles `
        -ExpectedObjectHashes $allObjectHashes `
        -ExpectedCatalogHashes @($v1CatalogHash, $v2CatalogHash) -ExpectedPreviousVersion v1
    if ($declinedV1Proof.IndexOf("assembly=$v2Assembly",
            [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw 'Declining remote V1 did not execute the exact installed V2 C# assembly.'
    }
    Assert-ImmutableDirectorySnapshot -Root $v1Release.VersionRoot -Expected $v1Snapshot
    Assert-ImmutableDirectorySnapshot -Root $v2Release.VersionRoot -Expected $v2Snapshot

    $rollbackV1Log = Invoke-WindowPlayerUntilProof -FileName $executable `
        -WorkingDirectory $runtimePlayerOutput -ProofPath $rollbackV1ProofPath `
        -PlayerLogPath $rollbackV1LogPath -Environment @{
            BENGINE_ASSET_BUNDLE_REMOTE_URL = $RemoteBaseUrl
            BENGINE_HOTUPDATE_PROOF_PATH = $rollbackV1ProofPath
            BENGINE_PLAYER_LOG_PATH = $rollbackV1LogPath
            BENGINE_AOT_AUTO_CHECK = '1'
            BENGINE_AOT_AUTO_CONFIRM_UPDATE = '1'
            BENGINE_AOT_AUTO_CONTINUE = '1'
        } -TimeoutSeconds 90
    $rollbackV1Proof = Assert-ProfileProof -Path $rollbackV1ProofPath -Profile v1 `
        -ExpectedImageHash $rollbackV1Release.ImageHash
    Assert-OrderedStartupLog -Path $rollbackV1LogPath -ExpectedTargetVersion v1 `
        -ExpectedPreviousVersion v2 -ExpectedDownloadedBundles 0 `
        -ExpectedDownloadedBytes 0 -RequireSplash
    Assert-DownloadedCache -Root $cacheRoot -RemotePackage $remotePackage `
        -Version $rollbackV1Release.Latest -ExpectedBundles $rollbackV1Release.Bundles `
        -ExpectedObjectHashes $allObjectHashes `
        -ExpectedCatalogHashes @($v1CatalogHash, $v2CatalogHash) -ExpectedPreviousVersion v2
    if ($rollbackV1Proof.IndexOf("assembly=$v1Assembly",
            [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw 'Confirming remote V1 did not execute the exact cached V1 C# assembly.'
    }
    Assert-ImmutableDirectorySnapshot -Root $v1Release.VersionRoot -Expected $v1Snapshot
    Assert-ImmutableDirectorySnapshot -Root $v2Release.VersionRoot -Expected $v2Snapshot

    $buildEntries = @(Get-ChildItem -LiteralPath $expectedBuildRoot -Force |
        Select-Object -ExpandProperty Name)
    $unexpectedBuildEntries = @($buildEntries | Where-Object {
        $_ -notin @('bengine 2d showcase', 'hotres')
    })
    if ($buildEntries.Count -ne 2 -or $unexpectedBuildEntries.Count -ne 0) {
        throw "Build must contain only the lowercase Player and hotres directories: $($buildEntries -join ', ')."
    }

    $hotResEntries = @(Get-ChildItem -LiteralPath $remoteOutput -Force |
        Select-Object -ExpandProperty Name)
    if ($hotResEntries.Count -ne 1 -or
        -not ([string]$hotResEntries[0]).Equals(
            $packageName, [System.StringComparison]::Ordinal)) {
        throw "HotRes root must contain only '$packageName': $($hotResEntries -join ', ')."
    }

    $latestAfterRollback = Get-Content -Raw -LiteralPath $latestPath |
        ConvertFrom-Json
    if (-not ([string]$latestAfterRollback.version).Equals('v1', [System.StringComparison]::Ordinal)) {
        throw "HotRes latest.json must target v1 after rollback, not '$($latestAfterRollback.version)'."
    }

    $dataDirectory = Join-Path $runtimePlayerOutput 'bengine 2d showcase_data'
    $dataEntries = @(Get-ChildItem -LiteralPath $dataDirectory -Force |
        Select-Object -ExpandProperty Name)
    $unexpectedDataEntries = @($dataEntries | Where-Object { $_ -notin @('assembly', 'resources') })
    if ($dataEntries.Count -ne 2 -or $unexpectedDataEntries.Count -ne 0) {
        throw "The packaged _data directory was modified at runtime: $($dataEntries -join ', ')."
    }

    Assert-PlayerResourceArchives -PlayerRoot $playerOutput
    Assert-PlayerResourceArchives -PlayerRoot $runtimePlayerOutput
    Assert-PlayerHasNoAssetBundles -PlayerRoot $playerOutput
    Assert-ColdSandboxHasNoGameContent -Root (Join-Path $playerOutput 'sandbox')
    Assert-LowercaseTree -Root $expectedBuildRoot
    Assert-LowercaseTree -Root $runtimePlayerOutput
    Write-Output ("BENGINE_WINDOWS_HOTUPDATE_OK|player=$playerOutput|runtimeCopy=$runtimePlayerOutput|" +
        "remote=$remotePackage|latest=v1|active=v1|previous=v2|hfs=$RemoteBaseUrl|cache=$cacheRoot|" +
        "v1Proof=$v1ProofPath|declinedV2Proof=$declinedV2ProofPath|v2Proof=$v2ProofPath|" +
        "declinedV1Proof=$declinedV1ProofPath|rollbackV1Proof=$rollbackV1ProofPath|" +
        "hfsV1=$hfsV1PreflightLogPath|hfsV2=$hfsV2PreflightLogPath|" +
        "hfsRollbackV1=$hfsRollbackV1PreflightLogPath|manualLatestV1=$manualLatestV1LogPath")
}
catch {
    [System.IO.Directory]::CreateDirectory($logsRoot) | Out-Null
    $failureDetails = $_ | Out-String
    [System.IO.File]::WriteAllText((Join-Path $logsRoot 'validation-failure.log'),
        $failureDetails, [System.Text.UTF8Encoding]::new($false))
    throw
}
finally {
    & $applyProfile -Profile V1 | Out-Null
}
