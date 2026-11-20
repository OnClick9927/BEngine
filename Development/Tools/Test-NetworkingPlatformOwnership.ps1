[CmdletBinding()]
param([Parameter(Mandatory)][string]$EngineBinaries,
      [Parameter(Mandatory)][string]$OriginalSource,
      [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($name in @('Legacy','Consumer')) {
    & dotnet build "$PSScriptRoot/Tests/NetworkingPlatformOwnership/$name/$name.csproj" -c Release --artifacts-path "$output/Build" "-p:EngineBinaries=$EngineBinaries" "-p:OriginalSource=$OriginalSource" -p:NuGetAudit=false *> "$output/Build-$name.log"
    if($LASTEXITCODE -ne 0){throw "Network verification build failed: $name"}
    & dotnet "$output/Build/bin/$name/release/$name.dll" "$output/$name.json" *> "$output/$name.log"
    if($LASTEXITCODE -ne 0){throw "Network verification failed: $name"}
}
$old=Get-Content "$output/Legacy.json" -Raw | ConvertFrom-Json -AsHashtable
$new=Get-Content "$output/Consumer.json" -Raw | ConvertFrom-Json -AsHashtable
foreach($key in $old.Payloads.Keys){if($old.Payloads[$key] -ne $new.Payloads[$key]){throw "Network payload changed: $key"}}
@{Passed=$true;PayloadsCompared=$old.Payloads.Count;LegacyChecks=$old.Count;CurrentChecks=$new.Count} | ConvertTo-Json | Set-Content "$output/Parity.json"
'NETWORK_PLATFORM_OWNERSHIP_AND_PAYLOAD_PARITY_OK'
