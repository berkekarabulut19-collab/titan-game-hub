using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TitanGameHub
{
    /// <summary>
    /// İndirme geçmişi: tamamlanan/başarısız indirmeleri kaydeder ve listeler.
    /// data/download_history.json dosyasına yazılır (son 200 kayıt tutulur).
    /// </summary>
    public class DownloadHistoryEntry
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string Url { get; set; }
        public string Host { get; set; }
        public string Status { get; set; }       // "completed", "failed", "cancelled"
        public string Error { get; set; }
        public long BytesDownloaded { get; set; }
        public long BytesTotal { get; set; }
        public double SpeedMBps { get; set; }
        public string StartedAt { get; set; }
        public string FinishedAt { get; set; }
        public int DurationSeconds { get; set; }
    }

    public static class DownloadHistoryService
    {
        private static readonly object _lock = new object();
        private static readonly string HistoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "download_history.json");
        private const int MaxEntries = 200;

        public static void Record(DownloadHistoryEntry entry)
        {
            if (entry == null) return;
            try
            {
                lock (_lock)
                {
                    List<DownloadHistoryEntry> list = LoadInternal();
                    list.Insert(0, entry);
                    if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
                    SaveInternal(list);
                }
            }
            catch { }
        }

        public static List<DownloadHistoryEntry> GetRecent(int count)
        {
            try
            {
                lock (_lock)
                {
                    List<DownloadHistoryEntry> list = LoadInternal();
                    if (count <= 0 || count >= list.Count) return list;
                    return list.GetRange(0, count);
                }
            }
            catch { return new List<DownloadHistoryEntry>(); }
        }

        public static void Clear()
        {
            try
            {
                lock (_lock)
                {
                    string dir = Path.GetDirectoryName(HistoryPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(HistoryPath, "[]", Encoding.UTF8);
                }
            }
            catch { }
        }

        private static List<DownloadHistoryEntry> LoadInternal()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return new List<DownloadHistoryEntry>();
                string json = File.ReadAllText(HistoryPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return new List<DownloadHistoryEntry>();
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                var arr = ser.Deserialize<List<DownloadHistoryEntry>>(json);
                return arr ?? new List<DownloadHistoryEntry>();
            }
            catch { return new List<DownloadHistoryEntry>(); }
        }

        private static void SaveInternal(List<DownloadHistoryEntry> list)
        {
            try
            {
                string dir = Path.GetDirectoryName(HistoryPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                File.WriteAllText(HistoryPath, ser.Serialize(list), Encoding.UTF8);
            }
            catch { }
        }
    }
}
