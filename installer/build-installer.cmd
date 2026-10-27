@echo off
rem Builds the icono installer. Double-click it, or run it from any folder. Extra arguments go to the script (for example -SkipTests).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" %*
echo.
pause
