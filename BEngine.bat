@echo off
setlocal

if /i not "%~1"=="--background" (
    start "" wscript.exe "%~dp0BEngine.Hidden.vbs" "%~f0"
    exit /b 0
)

cd /d "%~dp0"
set "LAUNCHER_EXE=%CD%\src\BEngine.Launcher\bin\Debug\net9.0-windows\BEngine.Launcher.exe"
set "EDITOR_EXE=%CD%\src\BEngine.Editor\bin\Debug\net9.0-windows\BEngine.Editor.exe"
set "BUILD_LOG_DIR=%LOCALAPPDATA%\BEngine"
set "BUILD_LOG=%BUILD_LOG_DIR%\LauncherBuild.log"
set "DOTNET_CLI_UI_LANGUAGE=en-US"

if not exist "%BUILD_LOG_DIR%" mkdir "%BUILD_LOG_DIR%"

dotnet build "%CD%\BEngine.sln" --nologo --verbosity minimal --disable-build-servers -m:1 -nr:false > "%BUILD_LOG%" 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -WindowStyle Hidden -Command ^
        "Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show('BEngine build failed. See: %BUILD_LOG%', 'BEngine', 'OK', 'Error') | Out-Null"
    exit /b 1
)

start "BEngine Project Manager" "%LAUNCHER_EXE%" --editor "%EDITOR_EXE%"
exit /b 0
