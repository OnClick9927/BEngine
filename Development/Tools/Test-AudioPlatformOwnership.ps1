[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach ($name in @('Legacy','Platform','Consumer')) {
    $binaries = if ($name -eq 'Legacy') { [IO.Path]::GetFullPath($LegacyBinaries) } else { [IO.Path]::GetFullPath($EngineBinaries) }
    & dotnet build "$PSScriptRoot/Tests/AudioPlatformOwnership/$name/$name.csproj" -c Release --artifacts-path "$output/$name/Build" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> "$output/$name-Build.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$output/$name-Build.log" -Tail 14; throw "Audio boundary consumer build failed: $name" }
    & dotnet "$output/$name/Build/bin/$name/release/$name.dll" "$output/$name" *> "$output/$name.log"
    if ($LASTEXITCODE -ne 0) { Get-Content "$output/$name.log" -Tail 18; throw "Audio boundary consumer failed: $name" }
}
$old = Get-Content "$output/Legacy/Queue.json" -Raw | ConvertFrom-Json
$new = Get-Content "$output/Platform/Queue.json" -Raw | ConvertFrom-Json
if ($old.SHA256 -ne $new.SHA256 -or ($old.Counters | ConvertTo-Json -Compress) -ne ($new.Counters | ConvertTo-Json -Compress)) { throw 'PCM queue samples or accounting changed during ownership migration' }
if ($old.WarmAllocatedBytes -ne 0 -or $new.WarmAllocatedBytes -ne 0) { throw 'PCM queue steady allocation regression' }
$platform = Get-Content "$output/Platform/Result.json" -Raw | ConvertFrom-Json
$consumer = Get-Content "$output/Consumer/Result.json" -Raw | ConvertFrom-Json
if (!$platform.Passed -or !$consumer.Passed) { throw 'Incomplete platform audio contracts' }
@{Passed=$true;Checks=4;PCMBytesAndCountersUnchanged=$true;PCMFrames=$new.Frames;PCMSHA256=$new.SHA256;LegacyWarmAllocatedBytes=$old.WarmAllocatedBytes;CurrentWarmAllocatedBytes=$new.WarmAllocatedBytes;PlatformChecks=$platform.Count;ConsumerChecks=$consumer.Count} | ConvertTo-Json | Set-Content -LiteralPath "$output/Parity.json"
'AUDIO_PLATFORM_OWNERSHIP_AND_PCM_PARITY_OK'
