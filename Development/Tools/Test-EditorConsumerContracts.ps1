[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($group in @('Paths','Publishing','Reflection','Animation')){
    $folder=Join-Path $output $group
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    & dotnet build "$PSScriptRoot/Tests/EditorConsumerContracts/Probe.csproj" -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> "$folder/Build.log"
    if($LASTEXITCODE -ne 0){throw "Editor consumer $group build failed: $folder/Build.log"}
    if($group -eq 'Animation'){
        # Preserve the actual old Core/provider combination for the baseline.
        # Its private-access metadata cannot be borrowed from the new Core.
        $worker=Join-Path $folder 'BaselineWorker'
        New-Item -ItemType Directory -Force -Path $worker | Out-Null
        Get-ChildItem -LiteralPath "$folder/Build/bin/Probe/release" -File | Copy-Item -Destination $worker
        Copy-Item -LiteralPath "$LegacyBinaries/BEngine/release/BEngine.dll" -Destination "$worker/BEngine.dll"
        & dotnet "$worker/EditorConsumerContractProbe.dll" AnimationBaseline "$folder/Baseline" $LegacyBinaries *> "$folder/Baseline.log"
        if($LASTEXITCODE -ne 0){throw "Editor consumer Animation baseline failed: $folder/Baseline.log"}
    }
    & dotnet "$folder/Build/bin/Probe/release/EditorConsumerContractProbe.dll" $group $folder $LegacyBinaries *> "$folder/Tests.log"
    if($LASTEXITCODE -ne 0){throw "Editor consumer $group tests failed: $folder/Tests.log"}
    Get-Content "$folder/Result.json" -Raw
}
