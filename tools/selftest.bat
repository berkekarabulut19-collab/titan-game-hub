@echo off
chcp 65001 >nul
"%~dp0bin\TitanGameHub.exe" --selftest
echo.
if exist "%~dp0bin\data\selftest_report.txt" (
    echo Rapor: %~dp0bin\data\selftest_report.txt
) else if exist "%~dp0data\selftest_report.txt" (
    echo Rapor: %~dp0data\selftest_report.txt
)
pause
