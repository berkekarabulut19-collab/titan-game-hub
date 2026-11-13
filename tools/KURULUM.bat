@echo off
chcp 65001 >nul
title TITAN GAME HUB // TEK TIK KURULUM
cd /d "%~dp0"

if not exist "bin\Games" mkdir "bin\Games"
if not exist "bin\data" mkdir "bin\data"
if not exist "bin\cache" mkdir "bin\cache"

if not exist "bin\TitanGameHub.exe" (
    echo [1/3] Uygulama derleniyor ve bilesenler kopyalaniyor...
    call "%~dp0tools\compile.bat"
    if errorlevel 1 (
        echo [HATA] Derleme basarisiz. .NET Framework 4.x yuklu oldugundan emin olun.
        pause
        exit /b 1
    )
) else (
    if exist "res\app.ico" copy /Y "res\app.ico" "bin\" >nul
    if exist "res\app_logo.png" copy /Y "res\app_logo.png" "bin\" >nul
    if exist "res\aria2\aria2c.exe" if not exist "bin\aria2c.exe" copy /Y "res\aria2\aria2c.exe" "bin\" >nul
    if exist "res\webview2\*.dll" copy /Y "res\webview2\*.dll" "bin\" >nul
)

echo [2/3] Masaustu ve Baslat Menusu kisayolu olusturuluyor...
set "VBS_TMP=%TEMP%\titan_shortcut.vbs"
(
    echo Set ws = CreateObject("WScript.Shell"^)
    echo desktopPath = ws.SpecialFolders("Desktop"^)
    echo startPath = ws.SpecialFolders("Programs"^)
    echo For Each p In Array(desktopPath, startPath, "%~dp0"^)
    echo   Set sc = ws.CreateShortcut(p ^& "\Titan Game Hub.lnk"^)
    echo   sc.TargetPath = "%~dp0bin\TitanGameHub.exe"
    echo   sc.WorkingDirectory = "%~dp0bin"
    echo   sc.IconLocation = "%~dp0bin\app.ico,0"
    echo   sc.Description = "Titan Game Hub"
    echo   sc.Save
    echo Next
) > "%VBS_TMP%"
cscript //nologo "%VBS_TMP%" >nul 2>&1
del /q "%VBS_TMP%" >nul 2>&1

echo [3/3] Titan Game Hub baslatiliyor...
start "" "%~dp0bin\TitanGameHub.exe"
exit /b 0
