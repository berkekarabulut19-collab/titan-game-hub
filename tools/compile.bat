@echo off
chcp 65001 >nul
cd /d "%~dp0.."
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set RES_DIR=res
set BIN_DIR=bin
if not exist %BIN_DIR% mkdir %BIN_DIR%

echo [DERLEME] C# WPF kaynak kodlar derleniyor...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:0 /codepage:65001 ^
  /win32icon:"%RES_DIR%\app.ico" ^
  /out:"%BIN_DIR%\TitanGameHub.exe" ^
  /r:System.dll ^
  /r:System.Core.dll ^
  /r:System.Net.dll ^
  /r:System.Web.Extensions.dll ^
  /r:System.Xaml.dll ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" ^
  /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" ^
  /r:System.Windows.Forms.dll ^
  /r:System.Drawing.dll ^
  /r:System.IO.Compression.dll ^
  /r:System.IO.Compression.FileSystem.dll ^
  /r:System.ServiceProcess.dll ^
  /r:"%RES_DIR%\webview2\Microsoft.Web.WebView2.Core.dll" ^
  /r:"%RES_DIR%\webview2\Microsoft.Web.WebView2.Wpf.dll" ^
  /recurse:"src\*.cs"

if %ERRORLEVEL% EQU 0 (
  echo [BASARILI] Derleme basarili: %BIN_DIR%\TitanGameHub.exe
  if exist "%RES_DIR%\app.ico" copy /Y "%RES_DIR%\app.ico" "%BIN_DIR%\" >nul
  if exist "%RES_DIR%\app_logo.png" copy /Y "%RES_DIR%\app_logo.png" "%BIN_DIR%\" >nul
  if exist "%RES_DIR%\aria2\aria2c.exe" copy /Y "%RES_DIR%\aria2\aria2c.exe" "%BIN_DIR%\" >nul
  if exist "%RES_DIR%\webview2\*.dll" copy /Y "%RES_DIR%\webview2\*.dll" "%BIN_DIR%\" >nul
  if exist "%~dp0UZAK_PC_GUNCELLE.bat" call "%~dp0UZAK_PC_GUNCELLE.bat"
) else (
  echo [HATA] Derleme basarisiz, kod %ERRORLEVEL%
  exit /b %ERRORLEVEL%
)
