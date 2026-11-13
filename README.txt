

## New Features Added

### Download History (DownloadHistoryService)
- Completed/incomplete downloads are tracked in `data/download_history.json`
- Records game name, host, status, speed, duration, and timestamp
- Can be viewed via the new "Download History" tab in the application
- Exported/importable via the settings profile system

### Settings Profile Export/Import
- `SettingsProfileService` allows exporting/importing user configuration
- Supports `titan_user.cfg` export/import with all settings
- File format: JSON at `data/settings_profile.json`
- Useful for reinstalling or sharing settings

### Fuzzy Search Helper
- `FuzzySearchHelper` provides tolerant search with Turkish character normalization
- `Score()` returns match quality (0-1000)
- `Matches()` checks against a threshold
- Integrated into game search for typo tolerance

### Background Job Queue
- `BackgroundJobQueue` manages asynchronous tasks like downloads, scans, updates
- Shows pending count and running count in UI
- Jobs are tracked with success/failure status and error messages

### Enhanced Crash Reporting
- `CrashReporter` now tracks last crash path and summary
- Includes OS bits, memory usage, machine name
- `GetLastCrashPath()` and `GetLastCrashSummary()` available
- Crashes stored in `data/crashes/` with auto-cleanup

### New Site Categories
- Added categories -30 to -33: 1337x, ElAmigos, SkidrowCodex, Dodi
- "Tüm Siteler" (-20) merges results from all connected sources
- Automatic deduplication via `GameDeduplicationEngine.Deduplicate()`

### Invite Protocol Support
- `titan://invite/TITAN-XXXXXX` and `titan://join/TITAN-XXXXXX` supported
- Invite codes stored in `data/pending_invite.txt`
- Main window can parse and process invites on startup

### GameProfile Enhancements
- Added `Tags` (comma-separated), `Collection`, `UpdatePolicy` (`auto`/`notify`/`manual`), `LastIntegrityCheck` fields
- Supports user-defined collections and update preferences
========================================================================
   TITAN GAME HUB // BİRLESIK OYUN VE TELEMETRI İSTEMCİSİ
========================================================================

BU UYGULAMA NE YAPAR?
------------------------------------------------------------------------
1. OYUN YONETICISI (STEAM TARZI TEK BASIS):
   - Katalogdaki oyunu secip "Oyna" tusuna basin.
   - Oyun kurulu degilse: indirme otomatik baslar, kurulum biter bitmez
     oyun ayni basista acilir (indir -> kur -> oyuna gir zinciri).
   - Oyun kuruluysa: dogrudan acilir.
   - Guncelleme varsa: indirilir, kurulur, oyun otomatik baslatilir.

2. TASINABILIR KURULUM YOLU COZUMLEME:
   - Katalogdaki yollar makineye ozel degildir. Uygulama her acilista
     oyunu su konumlarda arar: <uygulama>\Games, TITAN_GAMES_ROOT,
     Steam kutuphaneleri (libraryfolders.vdf), tum disklerdeki
     Games / Oyunlar / SteamLibrary\steamapps\common / Epic Games.
   - Baska bilgisayara tasindiginda yollari elle duzeltmeniz gerekmez.

3. MAGAZA (TUM SITELER BIRLESIK):
   - "TUM SITELER" sekmesi SteamRIP + FitGirl + OvaGames + FreeGOG
     kaynaklarini paralel cekip tek vitrinde birlestirir (sayfa basi ~24-90 oyun,
     "SONRAKI SAYFA" ile tum katalog gezilir).
   - Her kartta "INDIR" (tek tik) ve "+ KUTUPHANEYE EKLE" (indirmeden kaydet)
     butonlari vardir; kurulu oyunlarda "OYNA" / "FAVORILERE EKLE" gorunur.
   - Afisi olmayan oyunlar icin Steam mazerat aramasindan resmi header.jpg
     otomatik cekilir; bulunamazsa gradient + oyun adi yer tutucusu gosterilir.

4. CEVRIM ICI LOBI & STEAMWORKS KOPRUSU:
   - steam_api64.dll ve OnlineFix.ini yapilandirmalarini tek tikla entegre eder.
   - Spacewar (AppID 480) uzerinden arkadaslarinizla esli oturum kurar.
   - Steam istemcisinin acik olup olmadigini anlik denetler.

5. TORRENT ILE INDIRME (aria2c + qBittorrent):
   - Indirme ekraninda "TORRENT ILE INDIR" butonu. Sayfadaki magnet/.torrent
     baglantisini bulur.
   - qBittorrent WebUI entegrasyonu: bin\titan_user.cfg icinde QBIT_URL, QBIT_USER,
     QBIT_PASS ayarlanirsa "qBittorrent ile indir" butonu aktif olur.
   - Indirme bitince aria2 veya qBittorrent durdurulur, Defender tarafindan
     taratilir (bin\Games dislama ayari aktif).
   - Indirme: HTTP Range ile devam, iptal ise kisim korunur.
   - aria2c yolu: bin\aria2c.exe veya ARIA2_PATH ortam degiskeni.

6. ARKA PLAN GUNCELLEME + SAVE ZAMAN TUNELI:
   - Uygulama bostayken guncellemeleri indirir (Games\<oyun>_pkg.rar);
     "Oyna" dediginizde tekrar indirmez, dogrudan kurar.
   - "ZAMAN TUNELI" butonu kayit yedeklerini tarih sirasiyla listeler,
     secilen yedege doner.

7. OTOMATIK TEST:
   - TitanGameHub.exe --selftest  -> 36 kontrol (dedupe, ayna politikasi,
     host puani, parca uretimi, manifest, OTA imza, URL host, surum, magnet).
   - Rapor: bin\data\selftest_report.txt

8. GUVENLIK / DOGRULAMA:
   - Indirilen arsivler: sira numarasi (magic byte) dogrulamasi, eksik
     indirme (truncation) tespiti, otomatik devam (HTTP Range resume),
     disk alani on kontrolu.
   - Uygulama guncellemeleri (OTA) RSA-2048 + SHA-256 imza ile dogrulanir.
     Imzasiz veya hash'i uymayan paket REDDEDILIR ve silinir.
   - Ozel imza anahtari (tools\ota_private_key.xml) dagitim paketine
     ASLA dahil edilmez.

9. TAMAMEN TASINABILIR (PORTABLE):
   - Kurulum gerektirmez. Harici Python veya kutuphane zorunlulugu yoktur.
   - Bu klasoru baska bilgisayara aktarip "run.bat" ile calistirabilirsiniz.
   - Ilk acilista kendi arkadas kodunuzu ve kimliginizi otomatik olusturur.
     (titan_user.cfg / friends.cfg dagitim paketinde yer almaz).

DOSYA DUZENI
------------------------------------------------------------------------
  run.bat                Uygulamayi baslatir
  KURULUM_VE_BASLAT.bat  Ilk kurulum (kisayol, firewall, klasorler)
  bin\TitanGameHub.exe   Ana uygulama
  bin\Games             Portatif oyun kurulum koku
  bin\games_v6.cfg      Katalog (yollar bos birakilir, otomatik bulunur)
  bin\aria2c.exe        Indirme motoru (varsayilan)
  src\                 Kaynak kod (build.bat ile derlenir)
  bin\titan_user.cfg    Kullanici yapilandirmasi (dil, proxy, qBittorrent)
  bin\data\            Veri ve raporlar (manifest, test raporlari)
  bin\cache\           Kapak görseller ve cache dosyalari

KENDI KATALOG API'N (OPSIYONAL, UCRETSIZ)
------------------------------------------------------------------------
  1) Cloudflare > Workers > Create Worker, tools\catalog-worker.js icerigini yapistir.
  2) KV namespace olustur (CATALOG) ve binding ekle.
  3) bin\itan_user.cfg icine:  CATALOG_API=https://<worker>.workers.dev
  4) Uygulama bu adres tanimliysa katalogu API'den ceker (hizli + stabil),
     tanimli degilse mevcut site taramasina duser.

DIL (TR/EN)
------------------------------------------------------------------------
  bin\itan_user.cfg:  LANGUAGE=en   -> arayuz ana etiketleri Ingilizce.

YENI SURUM YAYINLAMA (YAYINCI ICIN)
------------------------------------------------------------------------
  1) tools\ota_private_key.xml dosyasinin yedegini alin (kaybederseniz
     tum istemciler guncelleme kabul etmez).
  2) package_zip.ps1 -> bin\TitanGameHub.exe derlenir, temiz zip uretir.
  3) Uygulama ici "OTA Gonder" dugmesi zip'i imzalar ve yayinlar.
     Anahtar yoksa yayin REDDEDILIR (fail-closed).
  4) Istemciler imzayi dogrular; gecersizse uygulama degistirilmez.

CI / SELFTEST (Geliştirici)
------------------------------------------------------------------------
  TitanGameHub.exe --selftest  : 36 motor testi, data\selftest_report.txt
  TitanGameHub.exe --export-catalog [dosya] : Katalogu JSON olarak dışa aktar
  .github/workflows/selftest.yml : GitHub Actions selftest pipeline

İLİŞKİ / OYUNLA BAŞLATMA
------------------------------------------------------------------------
  titan://launch/<oyun-id>    : Protokol ile oyun doğrudan başlatma
  titan://catalog              : Katalog ekranını açma (varsayılan davranış)

AĞ / BAĞLANTI SORUNLARI
------------------------------------------------------------------------
- Aynı Wi-Fi: LAN modu otomatik çalışır (UDP 47888 keşif + oyun portu).
- Farklı şehir/internet: "TAKIMI EŞİTLE & P2P GÜNCELLE" butonuna bas.
  Router'da UPnP açık olmalı (uygulama port yönlendirmeyi kendisi dener).
- Windows Firewall: KURULUM_VE_BASLAT.bat gerekli kuralları ekler.
- Defender oyun DLL'lerini (steam_api64.dll) karantinaya alıyorsa:
  bin\Games klasörü otomatik dışlanır; almıyorsa tekrar KURULUM çalıştır.
- qBittorrent WebUI portu 8080 varsayılan; config dosyasını kontrol edin.

YAZILIM GELİŞİM NOTLARI
------------------------------------------------------------------------
- AOB Bellek Yaması: src\AobMemoryPatcherService.cs aslında bellek yaması
  dener (VirtualProtectEx + WriteProcessMemory). Başarı oyun imzasına ve
  oyunun bellek düzenine bağlıdır. UI butonları gerçeği yansıtır.
- InjectionSyscallEngine: ReadStructure/WriteStructure tam yapsa da InjectDll
  ve WriteStructure artık işlevsel; WriteProcessMemory yolu kullanılır.
- Defender dışlama: KURULUM_VE_BASLAT.bat sadece Games klasörünü değil,
  EXE dosyasını ve aria2c.exe'yi de dışlayarak false positive'leri azaltır.
- Build script: CSC yolunu .NET SDK, where csc.exe, veya dotnet build ile
  otomatik tespit eder; mutlak yollar yerine.
- Torrent: aria2c JSON-RPC + qBittorrent WebUI API (multipart/urls) desteği.
  Config: bin\titan_user.cfg QBIT_URL / QBIT_USER / QBIT_PASS ayarları.
========================================================================
