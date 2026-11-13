using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TitanGameHub
{
    public static class SettingsProfileService
    {
        public class SettingsSnapshot
        {
            public string ExportedAt { get; set; }
            public string AppVersion { get; set; }
            public Dictionary<string, string> UserConfig { get; set; }
            public string Notes { get; set; }
        }

        public static string ExportToFile(string outPath)
        {
            try
            {
                SettingsSnapshot snap = new SettingsSnapshot
                {
                    ExportedAt = DateTime.UtcNow.ToString("o"),
                    AppVersion = AutoUpdateService.CurrentAppVersion,
                    UserConfig = ReadKeyValueFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "titan_user.cfg")),
                    Notes = "Titan Game Hub ayar profili"
                };

                string dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                File.WriteAllText(outPath, ser.Serialize(snap), Encoding.UTF8);
                return outPath;
            }
            catch { return null; }
        }

        public static bool ImportFromFile(string inPath, out string message)
        {
            message = "";
            try
            {
                if (!File.Exists(inPath)) { message = "Dosya bulunamadı."; return false; }
                string json = File.ReadAllText(inPath, Encoding.UTF8);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                SettingsSnapshot snap = ser.Deserialize<SettingsSnapshot>(json);
                if (snap == null || snap.UserConfig == null) { message = "Geçersiz ayar dosyası."; return false; }

                string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "titan_user.cfg");
                string dir = Path.GetDirectoryName(cfgPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Titan Game Hub Kullanıcı Yapılandırması");
                sb.AppendLine("# İçe aktarıldı: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                foreach (var kv in snap.UserConfig)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                    sb.AppendLine(kv.Key + "=" + (kv.Value ?? ""));
                }
                File.WriteAllText(cfgPath, sb.ToString(), Encoding.UTF8);
                message = "Ayarlar içe aktarıldı: " + cfgPath;
                return true;
            }
            catch (Exception ex)
            {
                message = "İçe aktarma hatası: " + ex.Message;
                return false;
            }
        }

        private static Dictionary<string, string> ReadKeyValueFile(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return dict;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k.Length > 0) dict[k] = v;
                }
            }
            catch { }
            return dict;
        }
    }
}
