@echo off
setlocal
cd /d "%~dp0.."
if not exist build-logs mkdir build-logs
echo Building DevelopmentTools... please wait. Log: %CD%\build-logs\dev-deploy.log
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-deploy.ps1" > "build-logs\dev-deploy.log" 2>&1
set RC=%ERRORLEVEL%
type "build-logs\dev-deploy.log"
echo.
echo ===== EXIT CODE %RC% =====
pause
