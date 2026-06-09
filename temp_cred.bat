@echo off
(
echo protocol=https
echo host=github.com
echo.
) | git credential fill > temp_cred.txt
