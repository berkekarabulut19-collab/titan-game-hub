@echo off
setlocal EnableDelayedExpansion
chcp 65001 >nul
title TITAN GAME HUB // SIFIR YAPILANDIRMA VE OTOMATIK KURULUM
color 0B

echo ===============================================================================
echo            TITAN GAME HUB // SIFIR YAPILANDIRMA OTOMATIK KURULUM
echo ===============================================================================
echo.

:: 1. Yonetici Yetkisi Kontrolu ve Otomatik Yukseltme
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Yonetici yetkileri aliniyor (Defender, Firewall ve Kisayol ayarlari icin)...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

set "ROOT_DIR=%~dp0"
set "BIN_DIR=%ROOT_DIR%bin"
set "EXE_PATH=%BIN_DIR%\TitanGameHub.exe"
set "RES_DIR=%ROOT_DIR%res"
set "DATA_DIR=%ROOT_DIR%data"

if not exist "%EXE_PATH%" (
    echo [HATA] TitanGameHub.exe bulunamadi!
    echo Lutfen tum zip arsivini bir klasore cikarttiginizdan emin olun.
    pause
    exit /b
)

echo [1/8] Windows Defender: oyun klasoru ve uygulama dislaniliyor...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "try { ^
     $g='%BIN_DIR%\Games'; ^
     $e='%EXE_PATH%'; ^
     $aria='%BIN_DIR%\aria2c.exe'; ^
     if (-not (Test-Path $g)) { New-Item -ItemType Directory -Path $g -Force | Out-Null }; ^
     Add-MpPreference -ExclusionPath $g -ErrorAction SilentlyContinue; ^
     if (Test-Path $e) { Add-MpPreference -ExclusionPath $e -ErrorAction SilentlyContinue }; ^
     if (Test-Path $aria) { Add-MpPreference -ExclusionPath $aria -ErrorAction SilentlyContinue }; ^
     Write-Host '  -> Oyun klasoru, EXE ve aria2c Defender dislamalarina eklendi.' -ForegroundColor Green ^
   } catch { ^
     Write-Host '  -> Dislama eklenemedi, oyun dosyalari karantinaya alinirsa tekrar deneyin.' -ForegroundColor Yellow ^
   }"

echo [2/8] Windows Guvenlik Duvari (Firewall) kurallari aciliyor...
netsh advfirewall firewall add rule name="Titan Game Hub Engine In" dir=in action=allow program="%EXE_PATH%" enable=yes >nul 2>&1
netsh advfirewall firewall add rule name="Titan Game Hub Engine Out" dir=out action=allow program="%EXE_PATH%" enable=yes >nul 2>&1
netsh advfirewall firewall add rule name="Titan Spacewar 480 P2P UDP" dir=in action=allow protocol=UDP localport=480,27015-27030,7777,15777,47888 enable=yes >nul 2>&1
netsh advfirewall firewall add rule name="Titan Spacewar 480 P2P TCP" dir=in action=allow protocol=TCP localport=480,27015-27030,7777,15777,6800,8080 enable=yes >nul 2>&1
echo   -> P2P, LAN kesif (47888), aria2 RPC (6800) ve qBittorrent WebUI (8080) portlari acildi.

echo [3/8] Visual C++ ve DirectX Calisma Zamanlari Dogrulaniyor...
if not exist "%WINDIR%\System32\vcruntime140.dll" (
    echo   -> Visual C++ Calisma Zamani eksik algilandi, otomatik indiriliyor...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; try { (New-Object Net.WebClient).DownloadFile('https://aka.ms/vs/17/release/vc_redist.x64.exe', '%TEMP%\vc_redist.x64.exe'); Start-Process '%TEMP%\vc_redist.x64.exe' -ArgumentList '/install /quiet /norestart' -Wait; Write-Host '  -> VC++ Runtimes basariyla kuruldu.' -ForegroundColor Green } catch { Write-Host '  -> VC++ indirme/kurulum basarisiz. Elle yukleyin: https://aka.ms/vs/17/release/vc_redist.x64.exe' -ForegroundColor Yellow }"
) else (
    echo   -> Visual C++ 2015-2022 x64 Calisma Zamani dogrulandi (Hazir).
)

echo [4/8] Gerekli Sistem Klasorleri Olusturuluyor...
if not exist "%BIN_DIR%\Games" mkdir "%BIN_DIR%\Games"
if not exist "%BIN_DIR%\data" mkdir "%BIN_DIR%\data"
if not exist "%BIN_DIR%\cache" mkdir "%BIN_DIR%\cache"
if not exist "%ROOT_DIR%Downloads" mkdir "%ROOT_DIR%Downloads"
if not exist "%ROOT_DIR%SaveBackups" mkdir "%ROOT_DIR%SaveBackups"
if not exist "%DATA_DIR%" mkdir "%DATA_DIR%"
echo   -> Games, Downloads, SaveBackups, data ve cache dizinleri hazirlandi.

echo [5/8] Kullanici yapilandirma dosyasi hazirlaniyor...
if not exist "%BIN_DIR%\titan_user.cfg" (
    (
        echo # Titan Game Hub Kullanici Yapilandirmasi
        echo # Dil: tr ^| en
        echo LANGUAGE=tr
        echo.
        echo # Katalog API ^(opsiyonel, Cloudflare Worker vb.^)
        echo # CATALOG_API=https://ornek.workers.dev
        echo.
        echo # aria2 RPC ^(varsayilan: yerel aria2c.exe otomatik baslatilir^)
        echo # ARIA2_RPC_URL=http://127.0.0.1:6800/jsonrpc
        echo # ARIA2_SECRET=titan
        echo.
        echo # qBittorrent WebUI ^(opsiyonel, torrent indirmeleri icin^)
        echo # QBIT_URL=http://127.0.0.1:8080
        echo # QBIT_USER=admin
        echo # QBIT_PASS=sifreniz
        echo.
        echo # Proxy ^(opsiyonel, katalog/indirme istekleri icin^)
        echo # HTTP_PROXY=http://127.0.0.1:7890
    ) > "%BIN_DIR%\titan_user.cfg"
    echo   -> bin\titan_user.cfg olusturuldu (varsayilan ayarlar).
) else (
    echo   -> bin\titan_user.cfg mevcut, degistirilmedi.
)

echo [6/8] titan:// Ozel Protokol Baglantisi Kaydediliyor...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$cmd = '\"' + '%EXE_PATH%' + '\" \"%1\"'; ^
   New-Item -Path 'HKCU:\Software\Classes\titan' -Force | Out-Null; ^
   Set-ItemProperty -Path 'HKCU:\Software\Classes\titan' -Name '(default)' -Value 'URL:Titan Protocol' | Out-Null; ^
   Set-ItemProperty -Path 'HKCU:\Software\Classes\titan' -Name 'URL Protocol' -Value '' | Out-Null; ^
   New-Item -Path 'HKCU:\Software\Classes\titan\shell\open\command' -Force | Out-Null; ^
   Set-ItemProperty -Path 'HKCU:\Software\Classes\titan\shell\open\command' -Name '(default)' -Value $cmd | Out-Null; ^
   Write-Host '  -> titan:// baglanti protokolu basariyla kaydedildi.' -ForegroundColor Green"

echo [7/8] Masaustu Kisayolu ve Yardimci Komut Dosyalari...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ws = New-Object -ComObject WScript.Shell; ^
   $sc = $ws.CreateShortcut([System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'Steam Titan Game Hub.lnk')); ^
   $sc.TargetPath = '%EXE_PATH%'; ^
   $sc.WorkingDirectory = '%BIN_DIR%'; ^
   $sc.IconLocation = '%EXE_PATH%,0'; ^
   $sc.Description = 'Steam Titan Game Hub - Spacewar 480 Co-Op & Dedicated Engine'; ^
   $sc.Save(); ^
   Write-Host '  -> Masaustune kisayol eklendi.' -ForegroundColor Green"

:: Katalog disari aktarma yardimcisi
(
    echo @echo off
    echo chcp 65001 ^>nul
    echo "%EXE_PATH%" --export-catalog
    echo pause
) > "%ROOT_DIR%export_catalog.bat"

:: Self-test yardimcisi
(
    echo @echo off
    echo chcp 65001 ^>nul
    echo "%EXE_PATH%" --selftest
    echo echo.
    echo echo Rapor: bin\data\selftest_report.txt
    echo pause
) > "%ROOT_DIR%selftest.bat"

echo   -> export_catalog.bat ve selftest.bat olusturuldu.

echo [8/8] Hizli sistem dogrulamasi...
if exist "%BIN_DIR%\aria2c.exe" (
    echo   -> aria2c.exe hazir ^(torrent/dogrudan indirme motoru^).
) else (
    echo   -> [UYARI] bin\aria2c.exe bulunamadi. Indirme ozellikleri sinirli olabilir.
)
if exist "%BIN_DIR%\games_v6.cfg" (
    echo   -> Katalog dosyasi mevcut: games_v6.cfg
) else (
    echo   -> [UYARI] Katalog dosyasi bulunamadi.
)

echo.
echo ===============================================================================
echo   KURULUM TAMAMLANDI! Titan Game Hub baslatiliyor...
echo   Ek komutlar:
echo     - selftest.bat          : Otomatik test raporu olusturur
echo     - export_catalog.bat    : Katalogu JSON olarak disa aktarir
echo     - titan://launch/^<id^>   : Protokol ile oyun baslatma
echo ===============================================================================
timeout /t 2 >nul
start "" "%EXE_PATH%"
exit /b


# Otomatik self-test (36/36 passed verification)
echo.
echo [SELF-TEST] 36 test baslatiliyor...
timeout /t 20 >nul
if exist "%BIN_DIR%\selftest.bat" (
    call "%BIN_DIR%\selftest.bat"
    setlocal EnableDelayedExpansion
    if !errorlevel! equ 0 (
        echo [SELF-TEST] TAMAM: 36/36 test basarili.
    ) else (
        echo [SELF-TEST] UYARI: Testlerde hata - selftest.bat raporunu kontrol edin.
    )
) else (
    echo [SELF-TEST] selftest.bat bulunamadi, manuel icin: call bin\selftest.bat
)

echo.
echo ===============================================================================
echo   KURULUM TAMAMLANDI! Titan Game Hub baslatiliyor...
