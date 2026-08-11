$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet run --project "$root/src/BEngine.Launcher/BEngine.Launcher.csproj" -- --editor "$root/src/BEngine.Editor/bin/Debug/net9.0/BEngine.Editor.exe"
