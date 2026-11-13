@echo off
chcp 65001 >nul
"%~dp0bin\TitanGameHub.exe" --export-catalog "%~dp0data\catalog_export.json"
echo.
echo Cikti: %~dp0data\catalog_export.json
pause
