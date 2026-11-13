using System;
using System.Collections.Generic;
using System.IO;

namespace TitanGameHub
{
    /// <summary>
    /// Basit iki dilli katman (TR/EN). titan_user.cfg: LANGUAGE=tr|en
    /// Sözlükte olmayan metinler olduğu gibi döner, yani kısmi çeviri güvenlidir.
    /// </summary>
    public static class LanguageService
    {
        public static string Current = "tr";

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Üst menü
            { "KÜTÜPHANE", "LIBRARY" },
            { "TÜM SİTE (5000+)", "STORE (5000+)" },
            { "İNDİRMELER", "DOWNLOADS" },
            { "ZERO-VPN", "ZERO-VPN" },
            { "MODLAR & SDK", "MODS & SDK" },
            { "TELEMETRİ", "TELEMETRY" },
            { "LOBİ & ARKADAŞLAR", "LOBBY & FRIENDS" },
            // Kütüphane filtreleri
            { "TÜMÜ", "ALL" },
            { "YÜKLÜLER", "INSTALLED" },
            { "FAVORİLER", "FAVORITES" },
            { "GÜNCELLEME", "UPDATES" },
            // Ana aksiyonlar
            { "OYNA", "PLAY" },
            { "İNDİR", "DOWNLOAD" },
            { "GÜNCELLE", "UPDATE" },
            { "KÜTÜPHANEYE EKLE", "ADD TO LIBRARY" },
            { "KÜTÜPHANEDE", "IN LIBRARY" },
            { "TAKİP", "WISHLIST" },
            { "TAKİPTE", "WISHLISTED" },
            { "FAVORİLERE EKLE", "ADD TO FAVORITES" },
            { "FAVORİLERDE", "FAVORITED" },
            // İndirme ekranı
            { "İPTAL (KISMI KORU)", "CANCEL (KEEP PARTIAL)" },
            { "DURAKLAT", "PAUSE" },
            { "DEVAM ET", "RESUME" },
            { "TORRENT İLE İNDİR", "DOWNLOAD VIA TORRENT" },
            { "MANUEL LİNK", "MANUAL LINK" },
            { "KALDIR", "UNINSTALL" },
            { "DİSK", "DISK" },
            { "BANT", "BANDWIDTH" },
            { "ZAMANLA", "SCHEDULE" },
            // Mağaza
            { "OYUN MAĞAZASI", "GAME STORE" },
            { "TÜM SİTELER (BİRLEŞİK CANLI)", "ALL SITES (LIVE MERGED)" },
            { "CANLI ARA", "LIVE SEARCH" },
            { "SON EKLENENLER", "NEW RELEASES" },
            { "URL'DEN EKLE", "ADD FROM URL" },
            { "TÜM SİTELERİ EKLE", "IMPORT ALL SITES" },
            // Ülke/durum
            { "HAZIR", "READY" },
            { "YÜKLÜ", "INSTALLED" },
            { "TARANIYOR", "SCANNING" },
        };

        public static void Load()
        {
            try
            {
                string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg");
                if (!File.Exists(cfg)) return;
                foreach (string raw in File.ReadAllLines(cfg))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("LANGUAGE", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string v = line.Substring(eq + 1).Trim().ToLowerInvariant();
                    if (v == "en" || v == "english") { Current = "en"; return; }
                }
            }
            catch { }
            Current = "tr";
        }

        public static bool IsEnglish { get { return Current == "en"; } }

        /// <summary>Türkçe metni (gerekiyorsa) İngilizceye çevirir.</summary>
        public static string T(string turkish)
        {
            if (string.IsNullOrEmpty(turkish) || Current != "en") return turkish;
            string english;
            return En.TryGetValue(turkish.Trim(), out english) ? english : turkish;
        }
    }
}
