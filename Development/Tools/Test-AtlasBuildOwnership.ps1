[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
foreach($mode in @('Legacy','Current')){
 $binaries=if($mode -eq 'Legacy'){[IO.Path]::GetFullPath($LegacyBinaries)}else{[IO.Path]::GetFullPath($EngineBinaries)}
 $extra=@();if($mode -eq 'Legacy'){$extra+='-p:DefineConstants=LEGACY'}
 $folder=Join-Path $output $mode
 New-Item -ItemType Directory -Force -Path $folder | Out-Null
 & dotnet build (Join-Path $PSScriptRoot 'Tests/AtlasBuildOwnership/Producer/Producer.csproj') -c Release --artifacts-path "$folder/ProducerBuild" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false @extra *> "$folder/ProducerBuild.log"
 if($LASTEXITCODE -ne 0){throw "Atlas compiler fixture build failed: $mode"}
 & dotnet "$folder/ProducerBuild/bin/Producer/release/Producer.dll" "$folder/Producer" *> "$folder/Producer.log"
 if($LASTEXITCODE -ne 0){throw "Atlas compiler contracts failed: $mode"}
 & dotnet build (Join-Path $PSScriptRoot 'Tests/AtlasBuildOwnership/Consumer/Consumer.csproj') -c Release --artifacts-path "$folder/ConsumerBuild" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> "$folder/ConsumerBuild.log"
 if($LASTEXITCODE -ne 0){throw "Atlas runtime fixture build failed: $mode"}
 & dotnet "$folder/ConsumerBuild/bin/Consumer/release/Consumer.dll" "$folder/Consumer" "$folder/Producer" *> "$folder/Consumer.log"
 if($LASTEXITCODE -ne 0){throw "Atlas runtime contracts failed: $mode"}
}
$old=Get-Content "$output/Legacy/Producer/Result.json" -Raw | ConvertFrom-Json
$new=Get-Content "$output/Current/Producer/Result.json" -Raw | ConvertFrom-Json
if(!$old.Passed -or !$new.Passed -or $old.Fixtures.Count -ne $new.Fixtures.Count){throw 'Incomplete atlas fixtures'}
foreach($fixture in $old.Fixtures){
 $match=@($new.Fixtures | Where-Object Name -EQ $fixture.Name)
 if($match.Count -ne 1 -or $match[0].SHA256 -ne $fixture.SHA256 -or $match[0].SourceSHA256 -ne $fixture.SourceSHA256){throw "Atlas publication bytes changed: $($fixture.Name)"}
}
if(!$new.EditorRetiredCompilerWasRemoved){throw 'Current Editor retirement retained its compiler'}
@{Passed=$true;Checks=3;FixturesCompared=$old.Fixtures.Count;SourceAndRuntimeBytesUnchanged=$true;LegacyEditorRetirementRemovedCompiler=$old.EditorRetiredCompilerWasRemoved;CurrentEditorRetirementRemovedCompiler=$new.EditorRetiredCompilerWasRemoved} | ConvertTo-Json | Set-Content -LiteralPath "$output/Parity.json"
'ATLAS_EDITOR_OWNERSHIP_AND_RUNTIME_PARITY_OK'
