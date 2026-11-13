@echo off
chcp 65001 >nul
title TITAN GAME HUB // HIZLI KURULUM VE BASLAT
cd /d "%~dp0"

if not exist "bin\Games" mkdir "bin\Games"
if not exist "bin\data" mkdir "bin\data"
if not exist "bin\cache" mkdir "bin\cache"

if not exist "bin\TitanGameHub.exe" (
    echo [1/2] Uygulama derleniyor...
    call "%~dp0compile.bat"
    if errorlevel 1 (
        echo [HATA] Derleme basarisiz. .NET Framework 4.x yuklu oldugundan emin olun.
        pause
        exit /b 1
    )
)

echo [2/2] Titan Game Hub baslatiliyor...
start "" "%~dp0bin\TitanGameHub.exe"
exit /b 0
