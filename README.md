# Titan Game Hub

Modern masaüstü oyun kütüphanesi, canlı katalog ve indirme yöneticisi.

## Hızlı Kurulum (Tek Tık)

1. Repoyu indirin (`Code -> Download ZIP`) veya klonlayın:
   ```bat
   git clone https://github.com/berkekarabulut19-collab/titan-game-hub.git
   ```
2. Klasördeki **`TitanGameSetup.exe`** dosyasına çift tıklayın ve **KURULUMU BAŞLAT** butonuna basın.
   - Gerekli klasörler (`bin\Games`, `bin\data`, `bin\cache`) ve WebView2/araç dosyaları hazırlanır.
   - Eksikse `bin\TitanGameHub.exe` otomatik derlenir (.NET Framework 4.x).
   - Masaüstü ve Başlat Menüsü'ne **`Titan Game Hub`** kısayolu oluşturulur, ardından uygulama başlatılır.

## Özellikler

- **Canlı Katalog:** SteamRIP, FitGirl, CroTorrents, FreeGOG ve OvaGames kaynaklarını paralel tarar ve çift kayıtları tekilleştirir.
- **Uygulama İçi İndirme:** Doğrudan HTTP akışı, `aria2c` (magnet/torrent) ve arka plan WebView2 ile site penceresi açılmadan indirir.
- **Kuyruk & Kurulum:** Çoklu indirme kuyruğu, arşiv güvenlik taraması, otomatik çıkarma ve başlatma.
- **Otomatik Test:** `tools\selftest.bat` (`bin\TitanGameHub.exe --selftest`).
- **Hazır EXE:** `bin/TitanGameHub.exe` doğrudan çalışmaya hazır olarak repoda yer alır.

---
**Sürüm:** `v9.4` (`v8.1 STEAM`)
