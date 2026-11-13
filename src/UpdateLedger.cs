using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    /// <summary>
    /// "Bu sürüm kuruldu" defteri. Amaç: güncelleme sonrası aynı uzak sürümün
    /// tekrar tekrar "GÜNCELLE" olarak görünmesini engellemek.
    ///
    /// data/uptodate.txt  →  <oyun anahtarı>|<kurulu sürüm>|<uzak sürüm imzası>
    /// </summary>
    public static class UpdateLedger
    {
        private static readonly object Lock = new object();

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "uptodate.txt");
            }
        }

        private static string Key(GameProfile gp)
        {
            if (gp == null) return "";
            string id = !string.IsNullOrEmpty(gp.Id) ? gp.Id : gp.Name;
            return (id ?? "").Trim().ToLowerInvariant().Replace('|', '-');
        }

        private static Dictionary<string, string> Load()
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string f = FilePath;
                if (!File.Exists(f)) return map;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    string[] p = line.Split('|');
                    if (p.Length >= 3) map[p[0]] = p[1] + "|" + p[2];
                }
            }
            catch { }
            return map;
        }

        /// <summary>Kurulum/güncelleme bittiğinde çağrılır.</summary>
        public static void MarkUpToDate(GameProfile gp, string remoteSignature)
        {
            if (gp == null) return;
            lock (Lock)
            {
                try
                {
                    Dictionary<string, string> map = Load();
                    map[Key(gp)] = (gp.Version ?? "") + "|" + (remoteSignature ?? gp.RemoteVersion ?? "");

                    List<string> lines = new List<string>();
                    foreach (KeyValuePair<string, string> kv in map)
                        lines.Add(kv.Key + "|" + kv.Value);
                    File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>Bu uzak sürüm zaten kuruldu mu? (tekrar uyarı çıkmasın)</summary>
        public static bool IsUpToDate(GameProfile gp, string remoteVersion)
        {
            if (gp == null || string.IsNullOrEmpty(remoteVersion)) return false;
            lock (Lock)
            {
                try
                {
                    Dictionary<string, string> map = Load();
                    string rec;
                    if (!map.TryGetValue(Key(gp), out rec)) return false;

                    string[] p = rec.Split('|');
                    if (p.Length < 2) return false;

                    return string.Equals(p[1].Trim(), remoteVersion.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }
    }
}
