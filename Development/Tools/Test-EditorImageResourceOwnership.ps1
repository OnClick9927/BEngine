[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$LegacyBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path "$output/System","$output/DotnetHome" | Out-Null
$env:NUGET_PACKAGES='C:/Users/OnClick/.nuget/packages'
$env:DOTNET_CLI_HOME="$output/DotnetHome";$env:TEMP="$output/System";$env:TMP=$env:TEMP
foreach($name in @('Legacy','Neutral','Workflow')){
 $bins=if($name -eq 'Legacy'){$LegacyBinaries}else{$EngineBinaries}
 & dotnet build "$PSScriptRoot/Tests/EditorImageResourceOwnership/$name/$name.csproj" -c Release --artifacts-path "$output/$name-Build" "-p:EngineBinaries=$bins" -p:NuGetAudit=false -p:BEnginePackageExportOwner=false -p:BEngineEngineExportOwner=false *> "$output/Build-$name.log"
 if($LASTEXITCODE){Get-Content "$output/Build-$name.log" -Tail 24;throw "$name build failed"}
 if($name -eq 'Legacy'){$legacy="$output/Legacy-Build/bin/Legacy/release/LegacyEditorImageConsumer.dll";continue}
 $image=if($name -eq 'Neutral'){'NeutralEditorImages'}else{'EditorImageWorkflow'}
 & dotnet "$output/$name-Build/bin/$name/release/$image.dll" "$output/$name" $legacy *> "$output/$name.log"
 if($LASTEXITCODE){Get-Content "$output/$name.log" -Tail 24;throw "$name failed"}
 Get-Content "$output/$name/Result.json" -Raw
}
