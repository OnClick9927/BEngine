@echo off
setlocal
set "ENGINE_ROOT=%~dp0BEgine"
set "LAUNCHER=%ENGINE_ROOT%\BEngine.Launcher.exe"
set "EDITOR=%ENGINE_ROOT%\BEngine.Editor.exe"
set "PENDING_UPDATE=%ENGINE_ROOT%\.update"
set "EDITOR_DATA=%~dp0EditorData"
set "BENGINE_EDITOR_DATA_PATH=%EDITOR_DATA%"
set "BENGINE_PACKAGES_PATH=%~dp0Packages"

if not exist "%EDITOR_DATA%\Logs" mkdir "%EDITOR_DATA%\Logs"

if exist "%PENDING_UPDATE%\*" (
    xcopy "%PENDING_UPDATE%\*" "%ENGINE_ROOT%\" /E /I /Y /Q >nul
    if errorlevel 2 (
        echo BEngine pending update could not be applied. Close running engine instances and try again.
        pause
        exit /b 1
    )
    rmdir /S /Q "%PENDING_UPDATE%"
)

if not exist "%LAUNCHER%" (
    echo BEngine Launcher was not found: %LAUNCHER%
    pause
    exit /b 1
)

start "BEngine Project Manager" "%LAUNCHER%" --editor "%EDITOR%"
exit /b 0
