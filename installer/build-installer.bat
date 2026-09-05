@echo off
setlocal

cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File "%~dp0publish.ps1"

echo.
echo Publish finished.
echo Open installer.iss in Inno Setup Compiler and press Ctrl+F9.
echo.
pause