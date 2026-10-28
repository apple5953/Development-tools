@echo off
title Development Tools - One-Click Installer
chcp 65001 >nul
echo ====================================================
echo   Installing Development Tools Revit Addin... (Non-Admin)
echo ====================================================

rem Company antivirus blocks downloads/executables under the system drive user profile (C:).
rem Install to the first writable fixed non-system drive (e.g. D:\DevelopmentTools).
rem IT can force a location with the DEVELOPMENTTOOLS_ROOT environment variable.
set TARGET_DIR=
if defined DEVELOPMENTTOOLS_ROOT set "TARGET_DIR=%DEVELOPMENTTOOLS_ROOT%"
if not defined TARGET_DIR (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$sys=$env:SystemDrive; foreach($d in [System.IO.DriveInfo]::GetDrives()){ try { if($d.DriveType -eq 'Fixed' -and $d.IsReady -and $d.Name.Substring(0,2) -ne $sys){ $p=Join-Path $d.Name 'DevelopmentTools'; New-Item -ItemType Directory -Force -Path $p | Out-Null; $f=Join-Path $p '.write_probe'; Set-Content -Path $f -Value ok -ErrorAction Stop; Remove-Item $f; Write-Output $p; break } } catch {} }" > "%~dp0dt_install_root.txt" 2>nul
    if exist "%~dp0dt_install_root.txt" (
        set /p TARGET_DIR=<"%~dp0dt_install_root.txt"
        del /F /Q "%~dp0dt_install_root.txt" >nul 2>nul
    )
)
if not defined TARGET_DIR set "TARGET_DIR=%LOCALAPPDATA%\DevelopmentTools"
echo Install location: %TARGET_DIR%
set ADDIN_NAME=DevelopmentTools.addin

echo 0. Validating extracted package...
if not exist "DevelopmentTools.Addin.dll" (
    echo.
    echo [ERROR] Missing DevelopmentTools.Addin.dll.
    echo Please right-click the ZIP file, choose "Extract All", then run install.bat from the extracted folder.
    echo.
    pause
    exit /b 1
)
if not exist "DevelopmentTools.Updater.exe" (
    echo.
    echo [ERROR] Missing DevelopmentTools.Updater.exe.
    echo Please extract the full ZIP package before running install.bat.
    echo.
    pause
    exit /b 1
)
if not exist "Resources\RibbonIcons" (
    echo.
    echo [ERROR] Missing Resources folder.
    echo Please extract the full ZIP package before running install.bat.
    echo.
    pause
    exit /b 1
)
if not exist "Runtime\Worker\v0001\BEAI.QueryWorker.exe" (
    echo.
    echo [ERROR] Missing Runtime worker.
    echo Please extract the full ZIP package before running install.bat.
    echo.
    pause
    exit /b 1
)

echo 1. Creating target directories...
if not exist "%TARGET_DIR%\App" mkdir "%TARGET_DIR%\App"
if not exist "%TARGET_DIR%\Config" mkdir "%TARGET_DIR%\Config"
if not exist "%TARGET_DIR%\Updater" mkdir "%TARGET_DIR%\Updater"

echo 2. Copying plugin files...
copy /Y "*.dll" "%TARGET_DIR%\App\" >nul
copy /Y "TileJointSharedParam.txt" "%TARGET_DIR%\App\" >nul
copy /Y "version.json" "%TARGET_DIR%\App\" >nul
copy /Y "platform_config.json" "%TARGET_DIR%\App\" >nul

echo   Copying Resources (icons/templates) and Rules...
if not exist "%TARGET_DIR%\App\Resources\RibbonIcons" mkdir "%TARGET_DIR%\App\Resources\RibbonIcons"
if not exist "%TARGET_DIR%\App\Resources\Templates" mkdir "%TARGET_DIR%\App\Resources\Templates"
if not exist "%TARGET_DIR%\App\Rules" mkdir "%TARGET_DIR%\App\Rules"
xcopy /Y /E /Q "Resources\RibbonIcons\*" "%TARGET_DIR%\App\Resources\RibbonIcons\" >nul
xcopy /Y /E /Q "Resources\Templates\*" "%TARGET_DIR%\App\Resources\Templates\" >nul
if exist "Rules" (
    xcopy /Y /E /Q "Rules\*" "%TARGET_DIR%\App\Rules\" >nul
)
if exist "Config" (
    if not exist "%TARGET_DIR%\App\Config" mkdir "%TARGET_DIR%\App\Config"
    xcopy /Y /E /Q "Config\*" "%TARGET_DIR%\App\Config\" >nul
)
if exist "Runtime" (
    xcopy /Y /E /Q "Runtime\*" "%TARGET_DIR%\App\Runtime\" >nul
)
if exist "MCP\dist\index.js" (
    if not exist "%TARGET_DIR%\App\MCP" mkdir "%TARGET_DIR%\App\MCP"
    xcopy /Y /E /Q "MCP\*" "%TARGET_DIR%\App\MCP\" >nul
)

echo   Cleaning conflicting system DLLs from App folder...
del /F /Q "%TARGET_DIR%\App\System.Buffers.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\App\System.Memory.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\App\System.Numerics.Vectors.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\App\System.Runtime.CompilerServices.Unsafe.dll" >nul 2>nul

if not exist "%TARGET_DIR%\Config\appsettings.json" (
    if exist "%LOCALAPPDATA%\DevelopmentTools\Config\appsettings.json" (
        copy /Y "%LOCALAPPDATA%\DevelopmentTools\Config\appsettings.json" "%TARGET_DIR%\Config\" >nul
    )
)
if not exist "%TARGET_DIR%\Config\appsettings.json" (
    copy /Y "appsettings.json" "%TARGET_DIR%\Config\" >nul
)

echo 3. Copying updater files...
copy /Y "DevelopmentTools.Updater.exe" "%TARGET_DIR%\Updater\" >nul
copy /Y "DevelopmentTools.Updater.exe.config" "%TARGET_DIR%\Updater\" >nul
copy /Y "*.dll" "%TARGET_DIR%\Updater\" >nul

echo   Keeping the updater's private System.Text.Json dependencies...

if exist "%TARGET_DIR%\Updater\DevelopmentTools.Addin.dll" (
    del /F /Q "%TARGET_DIR%\Updater\DevelopmentTools.Addin.dll" >nul 2>nul
)

echo 4. Registering Revit Addin manifest...
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<RevitAddIns^>
echo   ^<AddIn Type="Application"^>
echo     ^<Name^>Development Tools^</Name^>
echo     ^<Assembly^>%TARGET_DIR%\App\DevelopmentTools.Addin.dll^</Assembly^>
echo     ^<FullClassName^>DevelopmentTools.App^</FullClassName^>
echo     ^<ClientId^>c2d5d85c-4d33-4f9e-a89e-21ef1ea3b361^</ClientId^>
echo     ^<VendorId^>RoomTileSystem^</VendorId^>
echo     ^<VendorDescription^>Room Tile System v3^</VendorDescription^>
echo   ^</AddIn^>
echo ^</RevitAddIns^>
) > "%TARGET_DIR%\%ADDIN_NAME%"

set REVIT_ADDINS_BASE=%APPDATA%\Autodesk\Revit\Addins
set REVIT_COMMON_ADDINS_BASE=%ProgramData%\Autodesk\Revit\Addins
rem The add-in is built for Revit 2024 (.NET Framework 4.8). Revit 2025+ runs on .NET 8 and cannot load it,
rem so remove any registration left there by older installers.
for %%V in (2025 2026) do (
    if exist "%REVIT_ADDINS_BASE%\%%V\%ADDIN_NAME%" (
        del /F "%REVIT_ADDINS_BASE%\%%V\%ADDIN_NAME%"
        echo   [Removed] Revit %%V registration - not supported
    )
)
for %%V in (2024) do (
    rem Remove legacy BEAI Runtime manifests regardless of their old filename or registration scope.
    for %%R in ("%REVIT_ADDINS_BASE%\%%V" "%REVIT_COMMON_ADDINS_BASE%\%%V") do (
        if exist "%%~R" (
            for %%F in ("%%~R\*.addin") do (
                findstr /I /C:"DevelopmentTools.Addin.App" /C:"BEAI Runtime (DevelopmentTools)" "%%~fF" >nul 2>nul
                if not errorlevel 1 del /F "%%~fF" >nul 2>nul
            )
        )
    )
    if exist "%REVIT_ADDINS_BASE%\%%V" (
        copy /Y "%TARGET_DIR%\%ADDIN_NAME%" "%REVIT_ADDINS_BASE%\%%V\" >nul
        echo   [Registered] Revit %%V Addin
        
        rem Clean legacy RoomTileSystem.addin
        if exist "%REVIT_ADDINS_BASE%\%%V\RoomTileSystem.addin" (
            del /F "%REVIT_ADDINS_BASE%\%%V\RoomTileSystem.addin"
        )
        rem BEAI is embedded in Development Tools; remove the old standalone registration.
        if exist "%REVIT_ADDINS_BASE%\%%V\BEAI.QueryHost.addin" (
            del /F "%REVIT_ADDINS_BASE%\%%V\BEAI.QueryHost.addin"
        )
        
    )
)

rem Clean legacy Updater
if exist "%TARGET_DIR%\Updater\RoomTileSystem.Updater.exe" (
    del /F "%TARGET_DIR%\Updater\RoomTileSystem.Updater.exe"
)

del "%TARGET_DIR%\%ADDIN_NAME%"

if exist "setup-mcp-clients.ps1" if exist "%TARGET_DIR%\App\MCP\dist\index.js" (
    echo 5. Detecting and registering MCP clients...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "setup-mcp-clients.ps1" -InstallRoot "%TARGET_DIR%"
    if errorlevel 1 echo [WARNING] MCP client setup incomplete. See "%TARGET_DIR%\Logs\mcp-client-setup.log".
)

echo ====================================================
echo   Installation complete! Please open Revit to start using the plugin.
echo ====================================================
pause
