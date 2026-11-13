@echo off
:: ==============================================================================
:: TITAN GAME HUB // BUILD SCRIPT
:: Derlenir: build.bat ile src/*.cs -> bin/TitanGameHub.exe
:: ==============================================================================

:: CSC (C# Compiler) yolu tespiti: Çoklu yeri dene, en son .NET SDK'yu kullan
set "CSC_FOUND=0"

:: 1. .NET SDK varsayılan yolları dene
for %%N in (
    "C:\Program Files\dotnet\dotnet.exe"
    "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0\.NET Framework\bin\csc.exe"
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    "%ProgramFiles%\dotnet\dotnet.exe"
    "%ProgramFiles(x86)%\Microsoft SDKs\Windows\v10.0\.NET Framework\bin\csc.exe"
) do (
    if not exist "%%N" (
        echo [BILGI] CSC yolunda %%N bulunamadi, denetleniyor...
    ) else (
        echo [BILINFO] CSC bulundu: %%N
        set "CSC=%%N"
        set "CSC_FOUND=1"
        goto :CSC_DETECTED
    )
)

:: 2. PATH'deki csc ara
if %CSC_FOUND% EQU 0 (
    for /f "tokens=2 delims==" %%C in ('where csc.exe 2>nul') do (
        echo [BILINFO] PATH'den csc bulundu: %%C
        set "CSC=%%C"
        set "CSC_FOUND=1"
        goto :CSC_DETECTED
    )
)

:: 3. dotnet build kullan (modern .NET 6/8/9 tercihi)
if %CSC_FOUND% EQU 0 (
    echo [BILINFO] csc.exe bulunamadi, dotnet build deneniyor...
    where dotnet >nul 2>&1
    if %errorlevel% EQU 0 (
        echo [BILINFO] dotnet CLI mevcut, dotnet build ile derleme yapiliyor...
        set "BUILD_CMD=dotnet build -c Release --output bin"
    ) else (
        echo [HATA] CSC veya dotnet bulunamadi! Lutfen .NET SDK kurulu olsun.
        pause
        exit /b 1
    )
)

:CSC_DETECTED
if %CSC_FOUND% NEQ 1 (
    echo [HATA] Derleyici bulunamadi.
    pause
    exit /b 1
)

:: WPF/WinForms kaynak kodlarindan exe olustur
:: Build klasorlerini temizle ve yeniden olustur
set "SRC_DIR=%~dp0src"
set "BIN_DIR=%~dp0bin"
set "DATA_DIR=%~dp0data"
set "RES_DIR=%~dp0res"

if not exist "%BIN_DIR%" mkdir "%BIN_DIR%"
if not exist "%DATA_DIR%" mkdir "%DATA_DIR%"

:: Kaynak dosyalarini kopyala (ikonlar vb.)
copy /y "%RES_DIR%\app.ico" "%BIN_DIR%\app.ico" >nul 2>&1
copy /y "%RES_DIR%\app_logo.png" "%BIN_DIR%\app_logo.png" >nul 2>&1

:: WebView2 DLL'leri (varsa) kopyala
if exist "%RES_DIR%\webview2" (
    copy /y "%RES_DIR%\webview2\Microsoft.Web.WebView2.Core.dll" "%BIN_DIR\" >nul 2>&1
    copy /y "%RES_DIR%\webview2\Microsoft.Web.WebView2.Wpf.dll" "%BIN_DIR\" >nul 2>&1
)

:: aria2c.exe (varsa) kopyala
if exist "%RES_DIR%\aria2\aria2c.exe" (
    copy /y "%RES_DIR%\aria2\aria2c.exe" "%BIN_DIR\" >nul 2>&1
    echo [BILINFO] aria2c.exe derleme yardimci kopyalandi.
)

echo.
echo [DERLEME] C# WPF kaynak kodlar derleniyor...

:: Derleme komutunu belirle
if %CSC_FOUND% EQU 1 (
    set "CSC_EXE=%CSC%"
    :: Dosya ismini temizle (tam yolla olabilir)
    for /f "tokens=*" %%F in ("%CSC_EXE%") do set "CSC_EXE_NAME=%%~nxF"
    echo [BILINFO] Derleyici: %CSC_EXE_NAME%
    
    if defined CSC_EXE_NAME (
        if /i "%CSC_EXE_NAME%"=="dotnet.exe" (
            :: dotnet path'i
            "%CSC_EXE%" build -c Release --output "%BIN_DIR%" ^
                /r:System.dll ^
                /r:System.Core.dll ^
                /r:System.Net.dll ^
                /r:System.Web.Extensions.dll ^
                /r:System.Xaml.dll ^
                /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\PresentationFramework.dll" ^
                /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\PresentationCore.dll" ^
                /r:"%WINDIR%\Microsoft.NET\Framework\v4.0.30319\WindowsBase.dll" ^
                /r:System.Windows.Forms.dll ^
                /r:System.Drawing.dll ^
                /r:System.IO.Compression.dll ^
                /r:System.IO.Compression.FileSystem.dll ^
                /r:System.ServiceProcess.dll ^
                "%SRC_DIR%\Program.cs" "%SRC_DIR%\*.cs" >nul 2>&1
            set "ERR=%ERRORLEVEL%"
        ) else (
            :: Klasik csc.exe path'i
            "%CSC_EXE%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /codepage:65001 ^
                /win32icon:"%RES_DIR%\app.ico" ^
                /out:"%BIN_DIR%\TitanGameHub.exe" ^
                /r:System.dll ^
                /r:System.Core.dll ^
                /r:System.Net.dll ^
                /r:System.Web.Extensions.dll ^
                /r:System.Xaml.dll ^
                /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" ^
                /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" ^
                /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" ^
                /r:System.Windows.Forms.dll ^
                /r:System.Drawing.dll ^
                /r:System.IO.Compression.dll ^
                /r:System.IO.Compression.FileSystem.dll ^
                /r:System.ServiceProcess.dll ^
                /r:"%RES_DIR%\webview2\Microsoft.Web.WebView2.Core.dll" ^
                /r:"%RES_DIR%\webview2\Microsoft.Web.WebView2.Wpf.dll" ^
                /recurse:"%SRC_DIR%\*.cs"
            set "ERR=%ERRORLEVEL%"
        )
    )
) else (
    echo [HATA] CSC yolu belirtilamadi.
    exit /b 1
)

if %ERR% EQU 0 (
    echo.
    echo [BASARILI] TitanGameHub.exe v8.1 DirectX basariyla olusturuldu: %BIN_DIR%\TitanGameHub.exe
) else (
    echo.
    echo [HATA] Derleme basarisiz oldu (ERRORLEVEL=%ERR%).
    echo Lutfen kaynak dosyalarini ve referanslari kontrol edin.
    exit /b 1
)

echo.
echo [EK] Ilave kontroller...
if exist "%BIN_DIR%\TitanGameHub.exe" (
    echo   -> Executable olusturuldu: %BIN_DIR%\TitanGameHub.exe
    if exist "%BIN_DIR%\aria2c.exe" (
        echo   -> aria2c.exe yan klasorde mevcut.
    )
)

if exist "%~dp0UZAK_PC_GUNCELLE.bat" call "%~dp0UZAK_PC_GUNCELLE.bat"

echo.
pause