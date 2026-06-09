@echo off
title Development Tools - One-Click Installer
chcp 65001 >nul
echo ====================================================
echo   Installing Development Tools Revit Addin... (Non-Admin)
echo ====================================================

set TARGET_DIR=%LOCALAPPDATA%\DevelopmentTools
set ADDIN_NAME=DevelopmentTools.addin

echo 1. Creating target directories...
if not exist "%TARGET_DIR%\App" mkdir "%TARGET_DIR%\App"
if not exist "%TARGET_DIR%\Config" mkdir "%TARGET_DIR%\Config"
if not exist "%TARGET_DIR%\Updater" mkdir "%TARGET_DIR%\Updater"

echo 2. Copying plugin files...
copy /Y "*.dll" "%TARGET_DIR%\App\" >nul
copy /Y "TileJointSharedParam.txt" "%TARGET_DIR%\App\" >nul
copy /Y "version.json" "%TARGET_DIR%\App\" >nul
copy /Y "platform_config.json" "%TARGET_DIR%\App\" >nul

echo   Copying Resources (icons/templates)...
if not exist "%TARGET_DIR%\App\Resources\RibbonIcons" mkdir "%TARGET_DIR%\App\Resources\RibbonIcons"
if not exist "%TARGET_DIR%\App\Resources\Templates" mkdir "%TARGET_DIR%\App\Resources\Templates"
xcopy /Y /E /Q "Resources\RibbonIcons\*" "%TARGET_DIR%\App\Resources\RibbonIcons\" >nul
xcopy /Y /E /Q "Resources\Templates\*" "%TARGET_DIR%\App\Resources\Templates\" >nul

echo   Cleaning conflicting system DLLs from App folder...
del /F /Q "%TARGET_DIR%\App\System.Buffers.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\App\System.Memory.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\App\System.Numerics.Vectors.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\App\System.Runtime.CompilerServices.Unsafe.dll" >nul 2>nul

if not exist "%TARGET_DIR%\Config\appsettings.json" (
    copy /Y "appsettings.json" "%TARGET_DIR%\Config\" >nul
)

echo 3. Copying updater files...
copy /Y "DevelopmentTools.Updater.exe" "%TARGET_DIR%\Updater\" >nul
copy /Y "DevelopmentTools.Updater.exe.config" "%TARGET_DIR%\Updater\" >nul
copy /Y "*.dll" "%TARGET_DIR%\Updater\" >nul

echo   Cleaning conflicting system DLLs from Updater folder...
del /F /Q "%TARGET_DIR%\Updater\System.Buffers.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\Updater\System.Memory.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\Updater\System.Numerics.Vectors.dll" >nul 2>nul
del /F /Q "%TARGET_DIR%\Updater\System.Runtime.CompilerServices.Unsafe.dll" >nul 2>nul

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
echo     ^<VendorId^>MAYOUCHR^</VendorId^>
echo     ^<VendorDescription^>MAYOUCHR, Revit Tile Tool Developer^</VendorDescription^>
echo   ^</AddIn^>
echo ^</RevitAddIns^>
) > "%TEMP%\%ADDIN_NAME%"

set REVIT_ADDINS_BASE=%APPDATA%\Autodesk\Revit\Addins
for %%V in (2024 2025 2026) do (
    if exist "%REVIT_ADDINS_BASE%\%%V" (
        copy /Y "%TEMP%\%ADDIN_NAME%" "%REVIT_ADDINS_BASE%\%%V\" >nul
        echo   [Registered] Revit %%V Addin
        
        rem Clean legacy RoomTileSystem.addin
        if exist "%REVIT_ADDINS_BASE%\%%V\RoomTileSystem.addin" (
            del /F "%REVIT_ADDINS_BASE%\%%V\RoomTileSystem.addin"
        )
        
        rem Clean legacy system-wide addin if exists to prevent double loading
        if exist "C:\ProgramData\Autodesk\Revit\Addins\%%V\DevelopmentTools.addin" (
            del /F "C:\ProgramData\Autodesk\Revit\Addins\%%V\DevelopmentTools.addin" >nul 2>nul
        )
    )
)

rem Clean legacy Updater
if exist "%TARGET_DIR%\Updater\RoomTileSystem.Updater.exe" (
    del /F "%TARGET_DIR%\Updater\RoomTileSystem.Updater.exe"
)

del "%TEMP%\%ADDIN_NAME%"

echo ====================================================
echo   Installation complete! Please open Revit to start using the plugin.
echo ====================================================
pause
