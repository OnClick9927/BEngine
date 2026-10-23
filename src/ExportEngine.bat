@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "EXPORT_SCRIPT_DIRECTORY=%~dp0"
set "EXPORT_CONFIGURATION=Release"
set "EXPORT_PAUSE=1"
set "EXPORT_EXIT_CODE=0"

:parse_args
if "%~1"=="" goto prerequisites
if /i "%~1"=="Release" (
    set "EXPORT_CONFIGURATION=Release"
    shift
    goto parse_args
)
if /i "%~1"=="Debug" (
    set "EXPORT_CONFIGURATION=Debug"
    shift
    goto parse_args
)
if /i "%~1"=="--no-pause" (
    set "EXPORT_PAUSE=0"
    shift
    goto parse_args
)
if /i "%~1"=="--help" goto usage
if /i "%~1"=="/?" goto usage
echo [ERROR] Unknown argument: "%~1"
set "EXPORT_EXIT_CODE=2"
goto usage

:prerequisites
if not exist "%EXPORT_SCRIPT_DIRECTORY%ExportEngine.ps1" (
    echo [ERROR] ExportEngine.ps1 is missing beside this batch file.
    set "EXPORT_EXIT_CODE=1"
    goto finish
)
set "EXPORT_POWERSHELL=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
where pwsh.exe >nul 2>&1
if not errorlevel 1 set "EXPORT_POWERSHELL=pwsh.exe"
where dotnet.exe >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Install the .NET SDK required by global.json, then run this file again.
    set "EXPORT_EXIT_CODE=1"
    goto finish
)

rem Anchor SDK selection and relative paths to the repository, including UNC checkouts.
pushd "%EXPORT_SCRIPT_DIRECTORY%..\.."
if errorlevel 1 (
    echo [ERROR] Cannot enter the repository directory.
    set "EXPORT_EXIT_CODE=1"
    goto finish
)
rem ExportEngine.ps1 configures the local tool profile before the first SDK call.
echo.
echo Exporting BEngine, Hub and all engine packages [%EXPORT_CONFIGURATION%]...
echo Output: Engine\Engine\BEngine.Hub.exe, Engine\Engine\BEgine\BEgine-version, Engine\Engine\Packages
echo.
"%EXPORT_POWERSHELL%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%EXPORT_SCRIPT_DIRECTORY%ExportEngine.ps1" -Configuration "%EXPORT_CONFIGURATION%"
set "EXPORT_EXIT_CODE=%ERRORLEVEL%"
if not "%EXPORT_EXIT_CODE%"=="0" goto export_failed
echo.
echo [OK] Engine, Hub and Packages exported successfully.
echo Launch Hub with Engine\Engine\BEngine.Hub.exe.
goto restore_directory

:export_failed
echo.
echo [ERROR] Export failed with exit code %EXPORT_EXIT_CODE%. See the error above.

:restore_directory
popd
goto finish

:usage
echo Usage: ExportEngine.bat [Release^|Debug] [--no-pause]
echo Default: Release, with a pause after completion.
echo Exports Editor, Player, Hub, build runtimes/templates and all engine packages.
echo Close applications running from the export directory before exporting.
exit /b %EXPORT_EXIT_CODE%

:finish
if "%EXPORT_PAUSE%"=="1" pause
exit /b %EXPORT_EXIT_CODE%
