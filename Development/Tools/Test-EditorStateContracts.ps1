[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineBinaries,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$binaries=[IO.Path]::GetFullPath($EngineBinaries)
New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach($probe in @(
    @{Name='Core'; Project='EditorStateContracts'; Assembly='EditorStateContractProbe'},
    @{Name='Workflow'; Project='Workflow/Workflow'; Assembly='EditorStateWorkflowProbe'}
)) {
    $folder=Join-Path $output $probe.Name
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $project=Join-Path $PSScriptRoot "Tests/EditorStateContracts/$($probe.Project).csproj"
    & dotnet build $project -c Release --artifacts-path "$folder/Build" "-p:EngineBinaries=$binaries" -p:NuGetAudit=false *> "$folder/Build.log"
    if($LASTEXITCODE -ne 0){throw "Editor state $($probe.Name) build failed: $folder/Build.log"}
    $projectName=[IO.Path]::GetFileNameWithoutExtension($probe.Project)
    & dotnet "$folder/Build/bin/$projectName/release/$($probe.Assembly).dll" $folder *> "$folder/Tests.log"
    if($LASTEXITCODE -ne 0){throw "Editor state $($probe.Name) tests failed: $folder/Tests.log"}
    Get-Content -LiteralPath "$folder/Result.json" -Raw
}
