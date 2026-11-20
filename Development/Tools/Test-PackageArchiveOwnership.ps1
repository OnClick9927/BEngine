[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
 [Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$CoreOnly,[string]$LegacyArchive)
$ErrorActionPreference='Stop'
foreach($name in $(if($CoreOnly){@('Core')}else{@('Core','Workflow')})){
 $folder=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $name
 New-Item -ItemType Directory -Force -Path $folder | Out-Null
 & dotnet build "$PSScriptRoot/Tests/PackageArchiveOwnership/$name/$name.csproj" -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$EngineBinaries" -p:NuGetAudit=false *> "$folder/Build.log"
 if($LASTEXITCODE -ne 0){throw "Archive $name fixture build failed: $folder/Build.log"}
 $arguments=@("$folder/Build/bin/$name/release/PackageArchive${name}Probe.dll",$folder)
 if($name -eq 'Workflow' -and $LegacyArchive){$arguments+=$LegacyArchive}
 & dotnet @arguments *> "$folder/Tests.log"
 if($LASTEXITCODE -ne 0){throw "Archive $name tests failed: $folder/Tests.log"}
 Get-Content "$folder/Result.json" -Raw
}
