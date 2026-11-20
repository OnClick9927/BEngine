[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineBinaries,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../Temp/Engine/Verification/PlayerExtensionServices')
)
$ErrorActionPreference = 'Stop'
$binaries = [IO.Path]::GetFullPath($EngineBinaries)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $binaries BEngine.Player.dll))) { throw 'EngineBinaries must contain an exported engine.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$project = Join-Path $PSScriptRoot 'Tests/PlayerExtensionServices/PlayerExtensionServices.csproj'
$artifacts = Join-Path $output Build
$packageNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($manifest in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../../src/Packages') -Filter package.yaml -Recurse -File) {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($manifest.FullName), '(?m)^  assembly:\s*([^\r\n#]+)')) {
        [void]$packageNames.Add($match.Groups[1].Value.Trim().Trim('"', "'"))
    }
}
foreach ($tooling in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../../src/Packages') -Filter tooling.json -Recurse -File) {
    foreach ($projectFile in Get-ChildItem -LiteralPath $tooling.Directory.FullName -Filter *.csproj -Recurse -File) {
        [xml]$metadata = [IO.File]::ReadAllText($projectFile.FullName)
        $names = @($metadata.Project.PropertyGroup.AssemblyName | Where-Object { $_ })
        [void]$packageNames.Add($(if ($names.Count -eq 1) { $names[0] } else { $projectFile.BaseName }))
    }
}
$packageFile = Join-Path $output PackageAssemblies.txt
$packageNames | Sort-Object | Set-Content -LiteralPath $packageFile
& dotnet build $project -c Release --artifacts-path $artifacts "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> (Join-Path $output Build.log)
if ($LASTEXITCODE -ne 0) { throw "Player extension test build failed; see $output/Build.log" }
& dotnet (Join-Path $artifacts 'bin/PlayerExtensionServices/release/PlayerExtensionServices.dll') (Join-Path $output Result.json) $packageFile *> (Join-Path $output Tests.log)
if ($LASTEXITCODE -ne 0) { throw "Player extension tests failed; see $output/Tests.log" }
Get-Content -LiteralPath (Join-Path $output Result.json) -Raw
