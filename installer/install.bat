@echo off
title Development Tools - 免管理員一鍵安裝程式
chcp 65001 >nul
echo ====================================================
echo   正在為您安裝 Development Tools Revit 增益集... (免管理員版)
echo ====================================================

set TARGET_DIR=%LOCALAPPDATA%\DevelopmentTools
set ADDIN_NAME=DevelopmentTools.addin

echo 1. 建立目標資料夾...
if not exist "%TARGET_DIR%\App" mkdir "%TARGET_DIR%\App"
if not exist "%TARGET_DIR%\Config" mkdir "%TARGET_DIR%\Config"
if not exist "%TARGET_DIR%\Updater" mkdir "%TARGET_DIR%\Updater"

echo 2. 複製外掛檔案...
copy /Y "DevelopmentTools.Addin.dll" "%TARGET_DIR%\App\" >nul
copy /Y "TileJointSharedParam.txt" "%TARGET_DIR%\App\" >nul
copy /Y "version.json" "%TARGET_DIR%\App\" >nul
copy /Y "platform_config.json" "%TARGET_DIR%\App\" >nul

if not exist "%TARGET_DIR%\Config\appsettings.json" (
    copy /Y "appsettings.json" "%TARGET_DIR%\Config\" >nul
)

echo 3. 複製更新程式...
copy /Y "DevelopmentTools.Updater.exe" "%TARGET_DIR%\Updater\" >nul
copy /Y "DevelopmentTools.Updater.exe.config" "%TARGET_DIR%\Updater\" >nul
copy /Y "System.Text.Json.dll" "%TARGET_DIR%\Updater\" >nul
copy /Y "System.Memory.dll" "%TARGET_DIR%\Updater\" >nul
copy /Y "System.Buffers.dll" "%TARGET_DIR%\Updater\" >nul
copy /Y "System.Numerics.Vectors.dll" "%TARGET_DIR%\Updater\" >nul
copy /Y "System.Runtime.CompilerServices.Unsafe.dll" "%TARGET_DIR%\Updater\" >nul
copy /Y "System.Text.Encodings.Web.dll" "%TARGET_DIR%\Updater\" >nul
copy /Y "System.Threading.Tasks.Extensions.dll" "%TARGET_DIR%\Updater\" >nul
copy /Y "System.ValueTuple.dll" "%TARGET_DIR%\Updater\" >nul
copy /Y "Microsoft.Bcl.AsyncInterfaces.dll" "%TARGET_DIR%\Updater\" >nul

echo 4. 產生與註冊 Revit Addin 描述檔...
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
        echo   [已註冊] Revit %%V 增益集
        
        rem 清理舊版更名前的殘留
        if exist "%REVIT_ADDINS_BASE%\%%V\RoomTileSystem.addin" (
            del /F "%REVIT_ADDINS_BASE%\%%V\RoomTileSystem.addin"
        )
    )
)

rem 清理舊版 Updater
if exist "%TARGET_DIR%\Updater\RoomTileSystem.Updater.exe" (
    del /F "%TARGET_DIR%\Updater\RoomTileSystem.Updater.exe"
)

del "%TEMP%\%ADDIN_NAME%"

echo ====================================================
echo   安裝完成！現在請開啟 Revit 即可開始使用！
echo ====================================================
pause
