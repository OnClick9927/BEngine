param([Parameter(Mandatory = $true)][string]$ProjectPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project "$root/src/BEngine.Player/BEngine.Player.csproj" -- "$ProjectPath"
