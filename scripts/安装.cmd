@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "SCRIPT_DIR=%~dp0"
set "VALIDATE_ONLY="
if /i "%~1"=="--validate" (
    set "VALIDATE_ONLY=1"
    shift
)

if not "%~1"=="" (
    set "ARTIFACTS=%~f1"
) else if exist "%SCRIPT_DIR%service\YiShuHelper.Service.exe" (
    set "ARTIFACTS=%SCRIPT_DIR%"
) else (
    for %%I in ("%SCRIPT_DIR%..\artifacts") do set "ARTIFACTS=%%~fI"
)

if not exist "%ARTIFACTS%\service\YiShuHelper.Service.exe" goto missing_artifact
if not exist "%ARTIFACTS%\tray\YiShuHelper.Tray.exe" goto missing_artifact
if not exist "%ARTIFACTS%\yishu-split-config.json" goto missing_artifact

if defined VALIDATE_ONLY (
    echo CMD package validation passed.
    exit /b 0
)

fltmc >nul 2>&1
if errorlevel 1 (
    echo Administrator privileges are required.
    echo Right-click this CMD file and select "Run as administrator".
    pause
    exit /b 1
)

set "SERVICE_NAME=YiShuHelper"
set "INSTALL_DIR=%ProgramFiles%\YiShuHelper"
set "DATA_DIR=%ProgramData%\YiShuHelper"
set "SERVICE_EXE=%INSTALL_DIR%\service\YiShuHelper.Service.exe"

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if not errorlevel 1 (
    sc.exe stop "%SERVICE_NAME%" >nul 2>&1
    call :wait_for_service_stop
    if errorlevel 1 exit /b 1
    sc.exe delete "%SERVICE_NAME%" >nul
    if errorlevel 1 goto command_failed
    call :wait_for_service_delete
    if errorlevel 1 exit /b 1
)

taskkill.exe /F /IM YiShuHelper.Tray.exe >nul 2>&1
if not exist "%INSTALL_DIR%\service" mkdir "%INSTALL_DIR%\service"
if errorlevel 1 goto command_failed
if not exist "%INSTALL_DIR%\tray" mkdir "%INSTALL_DIR%\tray"
if errorlevel 1 goto command_failed
if not exist "%DATA_DIR%" mkdir "%DATA_DIR%"
if errorlevel 1 goto command_failed

robocopy.exe "%ARTIFACTS%\service" "%INSTALL_DIR%\service" /E /R:2 /W:1 >nul
if errorlevel 8 goto command_failed
robocopy.exe "%ARTIFACTS%\tray" "%INSTALL_DIR%\tray" /E /R:2 /W:1 >nul
if errorlevel 8 goto command_failed
if not exist "%DATA_DIR%\yishu-split-config.json" (
    copy /Y "%ARTIFACTS%\yishu-split-config.json" "%DATA_DIR%\yishu-split-config.json" >nul
    if errorlevel 1 goto command_failed
)

icacls.exe "%DATA_DIR%" /grant "%USERDOMAIN%\%USERNAME%:(OI)(CI)M" /T /Q >nul
if errorlevel 1 goto command_failed

sc.exe create "%SERVICE_NAME%" binPath= "\"%SERVICE_EXE%\"" DisplayName= "YiShu Split Helper" start= auto >nul
if errorlevel 1 goto command_failed
sc.exe description "%SERVICE_NAME%" "Maintains YiShu WireGuard split routes." >nul
if errorlevel 1 goto command_failed
sc.exe config "%SERVICE_NAME%" start= delayed-auto >nul
if errorlevel 1 goto command_failed
sc.exe failure "%SERVICE_NAME%" reset= 86400 actions= restart/5000/restart/15000/none/0 >nul
if errorlevel 1 goto command_failed

reg.exe add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v YiShuHelper /t REG_SZ /d "\"%INSTALL_DIR%\tray\YiShuHelper.Tray.exe\"" /f >nul
if errorlevel 1 goto command_failed
sc.exe start "%SERVICE_NAME%" >nul
if errorlevel 1 goto command_failed

echo Installation completed. The service is running and the tray app will start at the next sign-in.
exit /b 0

:wait_for_service_stop
for /L %%I in (1,1,40) do (
    sc.exe query "%SERVICE_NAME%" 2>nul | findstr.exe /C:"STOPPED" >nul && exit /b 0
    timeout.exe /T 1 /NOBREAK >nul
)
echo Timed out waiting for the service to stop.
exit /b 1

:wait_for_service_delete
for /L %%I in (1,1,20) do (
    sc.exe query "%SERVICE_NAME%" >nul 2>&1 || exit /b 0
    timeout.exe /T 1 /NOBREAK >nul
)
echo Timed out waiting for the old service to be deleted.
exit /b 1

:missing_artifact
echo Missing published files in: %ARTIFACTS%
exit /b 1

:command_failed
echo Installation failed because a system command returned an error.
exit /b 1
