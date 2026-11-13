# Titan Game Hub

Modern masaüstü oyun kütüphanesi, canlı katalog ve indirme yöneticisi.

## Hızlı Kurulum (Tek Tık)

1. Repoyu indirin (`Code -> Download ZIP`) veya klonlayın:
   ```bat
   git clone https://github.com/berkekarabulut19-collab/titan-game-hub.git
   ```
2. Klasördeki **`KURULUM.bat`** dosyasına çift tıklayın.
   - Eksikse `bin\TitanGameHub.exe` otomatik derlenir (.NET Framework 4.x).
   - Gerekli klasörler (`Games`, `data`, `cache`) oluşturulur ve uygulama başlar.

## Özellikler

- **Canlı Katalog:** SteamRIP, FitGirl, CroTorrents, FreeGOG ve OvaGames kaynaklarını paralel tarar ve çift kayıtları tekilleştirir.
- **Uygulama İçi İndirme:** Doğrudan HTTP akışı, `aria2c` (magnet/torrent) ve arka plan WebView2 ile site penceresi açılmadan indirir.
- **Kuyruk & Kurulum:** Çoklu indirme kuyruğu, arşiv güvenlik taraması, otomatik çıkarma ve başlatma.
- **Otomatik Test:** `selftest.bat` (`TitanGameHub.exe --selftest`).
- **Hazır EXE:** `bin/TitanGameHub.exe` doğrudan çalışmaya hazır olarak repoda yer alır.

---
**Sürüm:** `v9.4` (`v8.1 STEAM`)
