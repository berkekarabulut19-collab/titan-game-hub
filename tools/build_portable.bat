@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0.."

call "%~dp0compile.bat"
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

if exist "res\webview2" (
    copy /y "res\webview2\*" "bin\" >nul 2>&1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$srcRoot = (Get-Location).Path; " ^
  "$stage = Join-Path $env:TEMP 'TitanGameHub_Stage'; " ^
  "$zipOut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'TitanGameHub_Portable_Guncel.zip'; " ^
  "if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }; " ^
  "New-Item -ItemType Directory -Path $stage -Force | Out-Null; " ^
  "$topFiles = @('TitanGameSetup.exe', 'README.md', 'TitanGameHub.csproj'); " ^
  "foreach ($f in $topFiles) { $p = Join-Path $srcRoot $f; if (Test-Path $p) { Copy-Item $p -Destination $stage -Force } }; " ^
  "Copy-Item (Join-Path $srcRoot 'src') -Destination (Join-Path $stage 'src') -Recurse -Force; " ^
  "Copy-Item (Join-Path $srcRoot 'res') -Destination (Join-Path $stage 'res') -Recurse -Force; " ^
  "$binStage = Join-Path $stage 'bin'; New-Item -ItemType Directory -Path $binStage -Force | Out-Null; " ^
  "Get-ChildItem (Join-Path $srcRoot 'bin') -File | Where-Object { $_.Name -notin @('titan_user.cfg') } | Copy-Item -Destination $binStage -Force; " ^
  "New-Item -ItemType Directory -Path (Join-Path $binStage 'Games') -Force | Out-Null; " ^
  "New-Item -ItemType Directory -Path (Join-Path $binStage 'data') -Force | Out-Null; " ^
  "New-Item -ItemType Directory -Path (Join-Path $stage 'SaveBackups') -Force | Out-Null; " ^
  "if (Test-Path $zipOut) { Remove-Item $zipOut -Force }; " ^
  "Add-Type -AssemblyName System.IO.Compression.FileSystem; " ^
  "[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipOut, [System.IO.Compression.CompressionLevel]::Optimal, $false); " ^
  "Remove-Item $stage -Recurse -Force; " ^
  "Write-Host ('[PAKET HAZIR] ' + $zipOut) -ForegroundColor Green"
