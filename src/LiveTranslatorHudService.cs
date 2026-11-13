using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;

namespace TitanGameHub
{
    /// <summary>
    /// Phase 6 (v9.0): Real-Time In-Game Turkish Subtitle & Dialog Translation Engine.
    /// Translates game dialog lines, mission objectives, or clipboard/memory strings
    /// into Turkish instantaneously using a persistent local dictionary cache + cloud endpoint.
    /// </summary>
    public static class LiveTranslatorHudService
    {
        private static readonly Dictionary<string, string> _subtitleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new object();
        private static bool _isEnabled = false;
        private static string _currentTurkishSubtitle = " [F6] Canlı Türkçe Altyazı Motoru Hazır — Oyun içi diyaloglar otomatik çevrilir.";
        private static string _lastInputText = "";

        public static event Action<string> OnSubtitleUpdated;

        public static bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                _isEnabled = value;
                if (_isEnabled)
                {
                    LoadCacheFromDisk();
                }
            }
        }

        public static string CurrentTurkishSubtitle
        {
            get { return _currentTurkishSubtitle; }
        }

        static LiveTranslatorHudService()
        {
            SeedCommonGameDialogs();
            LoadCacheFromDisk();
        }

        private static void SeedCommonGameDialogs()
        {
            _subtitleCache["Press E to interact"] = "Etkileşime geçmek için [E] tuşuna basın";
            _subtitleCache["Waiting for players..."] = "Diğer oyuncular bekleniyor...";
            _subtitleCache["Host has left the game"] = "Oda kurucusu oyundan ayrıldı (Host Migration devrede)";
            _subtitleCache["Connection lost"] = "Bağlantı koptu — Yeniden bağlanılıyor";
            _subtitleCache["Objective Updated"] = "Görev Hedefi Güncellendi";
            _subtitleCache["Inventory Full"] = "Envanter Dolu!";
            _subtitleCache["Checkpoint Reached"] = "Kontrol Noktasına Ulaşıldı (Oyun Kaydedildi)";
            _subtitleCache["You are being hunted"] = "Avlanıyorsun! Hemen saklan!";
            _subtitleCache["Quota reached"] = "Şirket kotasına ulaşıldı!";
        }

        private static string GetCacheFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tr_subtitle_cache.cfg");
        }

        public static void LoadCacheFromDisk()
        {
            try
            {
                string path = GetCacheFilePath();
                if (!File.Exists(path)) return;

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                lock (_cacheLock)
                {
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                        int sep = line.IndexOf("||");
                        if (sep > 0)
                        {
                            string src = line.Substring(0, sep).Trim();
                            string tr = line.Substring(sep + 2).Trim();
                            if (!string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(tr))
                            {
                                _subtitleCache[src] = tr;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void SaveToDiskCache(string sourceText, string translatedText)
        {
            try
            {
                string path = GetCacheFilePath();
                string entry = sourceText.Replace("\r", " ").Replace("\n", " ") + "||" + translatedText.Replace("\r", " ").Replace("\n", " ") + "\r\n";
                File.AppendAllText(path, entry, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// Asynchronously translates English/foreign game text into Turkish and updates the In-Game Overlay HUD.
        /// </summary>
        public static void TranslateDialogAsync(string englishText)
        {
            if (string.IsNullOrEmpty(englishText)) return;
            string clean = englishText.Trim();
            if (clean.Length < 2 || clean.Length > 450) return;
            if (string.Equals(clean, _lastInputText, StringComparison.Ordinal)) return;
            _lastInputText = clean;

            lock (_cacheLock)
            {
                string cached;
                if (_subtitleCache.TryGetValue(clean, out cached))
                {
                    _currentTurkishSubtitle = "🇹🇷 " + cached;
                    if (OnSubtitleUpdated != null) OnSubtitleUpdated(_currentTurkishSubtitle);
                    return;
                }
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                string translated = QueryFastTranslateEndpoint(clean);
                if (!string.IsNullOrEmpty(translated))
                {
                    lock (_cacheLock)
                    {
                        _subtitleCache[clean] = translated;
                    }
                    SaveToDiskCache(clean, translated);
                    _currentTurkishSubtitle = "🇹🇷 " + translated;
                    if (OnSubtitleUpdated != null) OnSubtitleUpdated(_currentTurkishSubtitle);
                }
            });
        }

        private static string QueryFastTranslateEndpoint(string text)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
                string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=tr&dt=t&q=" + Uri.EscapeDataString(text);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                req.Timeout = 3500;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    // Extract first string inside [[["translated","source",...]]]
                    int firstQuote = json.IndexOf('"');
                    if (firstQuote >= 0)
                    {
                        int secondQuote = json.IndexOf('"', firstQuote + 1);
                        while (secondQuote > 0 && json[secondQuote - 1] == '\\')
                        {
                            secondQuote = json.IndexOf('"', secondQuote + 1);
                        }
                        if (secondQuote > firstQuote + 1)
                        {
                            string extracted = json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                            return extracted.Replace("\\\"", "\"").Replace("\\n", " ");
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
