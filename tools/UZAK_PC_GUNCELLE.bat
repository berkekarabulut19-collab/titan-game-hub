@echo off
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "HEDEF_DOSYA=%ROOT%uzak_pc_hedef.txt"
set "HEDEF="

if not exist "%HEDEF_DOSYA%" (
  echo [UZAK] Hedef dosyasi bulunamadi: uzak_pc_hedef.txt
  echo [UZAK] Uzak guncelleme atlandi. Dosyaya diger PC'deki TitanGameHub yolunu yazin.
  exit /b 0
)

for /f "usebackq eol=; tokens=* delims=" %%L in ("%HEDEF_DOSYA%") do (
  if not defined HEDEF (
    set "SATIR=%%L"
    if defined SATIR (
      set "HEDEF=!SATIR!"
    )
  )
)

if not defined HEDEF (
  echo [UZAK] Uzak PC hedefi ayarlanmamis, guncelleme atlandi.
  echo [UZAK] Ayarlamak icin: uzak_pc_hedef.txt icine hedef yolu yazin (orn: \\DIGER-PC\TitanGameHub)
  exit /b 0
)

rem Satir sonu bosluklarini temizle
for /f "tokens=* delims=" %%A in ("!HEDEF!") do set "HEDEF=%%A"

set "BIN_HEDEF=!HEDEF!"
if /i "!HEDEF:~-4!"=="\bin" set "BIN_HEDEF=!HEDEF!"
if /i "!HEDEF:~-4!"=="/bin" set "BIN_HEDEF=!HEDEF!"
if /i not "!HEDEF:~-4!"=="\bin" if /i not "!HEDEF:~-4!"=="/bin" set "BIN_HEDEF=!HEDEF!\bin"

echo [UZAK] Hedef: !BIN_HEDEF!
if not exist "!BIN_HEDEF!" (
  echo [UZAK] Hedef bin klasoru yok, olusturuluyor...
  mkdir "!BIN_HEDEF!" >nul 2>nul
)
if not exist "!BIN_HEDEF!" (
  echo [UZAK][HATA] Hedef klasore erisilemiyor: !BIN_HEDEF!
  exit /b 1
)

echo [UZAK] Dosyalar kopyalaniyor (cache/data/Games/Backups haric)...
robocopy "%ROOT%bin" "!BIN_HEDEF!" /E /XD cache data Games Backups /R:2 /W:2 /NFL /NDL /NJH /NJS
set "RC=%ERRORLEVEL%"
if %RC% GEQ 8 (
  echo [UZAK][HATA] Uzak kopyalama basarisiz. Robocopy kodu: %RC%
  echo [UZAK] Hedef PC acik mi, yol/paylasim erisilebilir mi kontrol edin.
  exit /b %RC%
)

echo [UZAK] Uzak PC guncellemesi tamamlandi.
exit /b 0
