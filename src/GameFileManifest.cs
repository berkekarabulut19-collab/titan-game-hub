using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TitanGameHub
{
    /// <summary>
    /// Steam'in "manifest" mantığının hafif karşılığı:
    /// Kurulum/güncelleme sonrası oyun klasörünün dosya listesi + boyut + zaman damgası
    /// snapshot'ı alınır. Sonraki "Bütünlüğü Doğrula" işlemi bu snapshot ile karşılaştırılır:
    ///  - silinmiş dosyalar  → eksik
    ///  - boyutu değişmiş / 0 bayta düşmüş dosyalar → bozuk
    /// Böylece doğrulama "her şey var mı" değil, "kurulumdan bu yana ne bozuldu" sorusunu yanıtlar.
    /// </summary>
    public static class GameFileManifest
    {
        private static readonly string ManifestRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "integrity");

        public static string GetManifestPath(GameProfile gp)
        {
            if (gp == null) return null;
            string id = !string.IsNullOrEmpty(gp.Id) ? gp.Id : Regex.Replace(gp.Name ?? "game", @"[^\w\-]", "");
            id = Regex.Replace(id, @"[^\w\-]", "");
            return Path.Combine(ManifestRoot, id + ".manifest");
        }

        public static bool HasSnapshot(GameProfile gp)
        {
            try
            {
                string p = GetManifestPath(gp);
                return !string.IsNullOrEmpty(p) && File.Exists(p);
            }
            catch { return false; }
        }

        /// <summary>Kurulum sonrası çağrılır. Büyük oyunlarda hızlı olsun diye sadece meta okur.</summary>
        public static int SaveSnapshot(GameProfile gp)
        {
            try
            {
                if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                    return 0;

                if (!Directory.Exists(ManifestRoot)) Directory.CreateDirectory(ManifestRoot);

                string root = gp.WorkingDirectory;
                List<string> lines = new List<string>();
                lines.Add("# TitanGameHub integrity manifest");
                lines.Add("# path|size|lastWriteUtcTicks");

                string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                foreach (string f in files)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        string rel = f.Substring(root.Length).TrimStart('\\', '/');
                        lines.Add(rel + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks);
                    }
                    catch { }
                }

                string path = GetManifestPath(gp);
                string tmp = path + ".tmp";
                File.WriteAllLines(tmp, lines.ToArray(), Encoding.UTF8);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return files.Length;
            }
            catch { return 0; }
        }

        /// <summary>Snapshot ile karşılaştırır: eksik ve değişmiş dosyalar.</summary>
        public static void CompareToSnapshot(GameProfile gp, out List<string> missing, out List<string> changed)
        {
            missing = new List<string>();
            changed = new List<string>();

            try
            {
                string path = GetManifestPath(gp);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                string root = gp.WorkingDirectory;
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] p = line.Split('|');
                    if (p.Length < 2) continue;

                    string rel = p[0];
                    long expectedSize;
                    if (!long.TryParse(p[1], out expectedSize)) continue;

                    string full;
                    try { full = Path.Combine(root, rel); }
                    catch { continue; }

                    if (!File.Exists(full))
                    {
                        missing.Add(rel);
                        continue;
                    }

                    try
                    {
                        long actual = new FileInfo(full).Length;
                        if (expectedSize > 0 && actual != expectedSize)
                            changed.Add(rel + "  (" + expectedSize + " -> " + actual + " bayt)");
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
