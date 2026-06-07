@echo off
setlocal
cd /d "%~dp0"

echo ==================================================
echo   Development tools - Revit add-in uninstall
echo ==================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall %*
set UNINSTALL_EXIT_CODE=%ERRORLEVEL%

echo.
if not "%UNINSTALL_EXIT_CODE%"=="0" (
    echo Uninstall failed. Please send the message above to the add-in maintainer.
) else (
    echo Uninstall completed. Restart Revit 2024 if it is currently open.
)
echo.
pause
exit /b %UNINSTALL_EXIT_CODE%
