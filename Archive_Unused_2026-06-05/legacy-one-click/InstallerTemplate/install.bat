@echo off
setlocal
cd /d "%~dp0"

echo ==================================================
echo   Development tools - Revit add-in one-click install
echo ==================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set INSTALL_EXIT_CODE=%ERRORLEVEL%

echo.
if not "%INSTALL_EXIT_CODE%"=="0" (
    echo Installation failed. Please send the message above to the add-in maintainer.
) else (
    echo Installation completed. Restart Revit 2024 before using the add-in.
)
echo.
pause
exit /b %INSTALL_EXIT_CODE%
