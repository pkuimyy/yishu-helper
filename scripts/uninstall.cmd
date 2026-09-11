@echo off
setlocal EnableExtensions DisableDelayedExpansion
fltmc >nul 2>&1
if errorlevel 1 (
    echo Administrator privileges are required.
    echo Right-click uninstall.cmd and select "Run as administrator".
    pause
    exit /b 1
)

set "SERVICE_NAME=YiShuHelper"
set "INSTALL_DIR=%ProgramFiles%\YiShuHelper"
set "DATA_DIR=%ProgramData%\YiShuHelper"

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if not errorlevel 1 (
    sc.exe stop "%SERVICE_NAME%" >nul 2>&1
    call :wait_for_service_stop
    if errorlevel 1 exit /b 1
    sc.exe delete "%SERVICE_NAME%" >nul
    if errorlevel 1 goto command_failed
)

taskkill.exe /F /IM YiShuHelper.Tray.exe >nul 2>&1
reg.exe delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v YiShuHelper /f >nul 2>&1

if /i "%INSTALL_DIR%"=="%ProgramFiles%\YiShuHelper" if exist "%INSTALL_DIR%" rd /S /Q "%INSTALL_DIR%"
if exist "%INSTALL_DIR%" goto command_failed

if /i "%~1"=="--remove-data" (
    if /i "%DATA_DIR%"=="%ProgramData%\YiShuHelper" if exist "%DATA_DIR%" rd /S /Q "%DATA_DIR%"
    if exist "%DATA_DIR%" goto command_failed
)

echo Uninstallation completed. Pass --remove-data to also remove configuration, state, and logs.
exit /b 0

:wait_for_service_stop
for /L %%I in (1,1,40) do (
    sc.exe query "%SERVICE_NAME%" 2>nul | findstr.exe /C:"STOPPED" >nul && exit /b 0
    timeout.exe /T 1 /NOBREAK >nul
)
echo Timed out waiting for the service to stop.
exit /b 1

:command_failed
echo Uninstallation failed because a system command returned an error.
exit /b 1
