using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    /// <summary>
    /// Takip listesi (wishlist): kütüphaneye eklemeden oyunu izle.
    /// data/wishlist.txt içinde normalize edilmiş oyun adları tutulur.
    /// </summary>
    public static class Wishlist
    {
        private static readonly object Lock = new object();

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "wishlist.txt");
            }
        }

        private static HashSet<string> Load()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string f = FilePath;
                if (!File.Exists(f)) return set;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    string key = GameDeduplicationEngine.NormalizeGameTitle(line.Trim());
                    if (!string.IsNullOrEmpty(key)) set.Add(key);
                }
            }
            catch { }
            return set;
        }

        public static bool IsWishlisted(GameProfile gp)
        {
            if (gp == null) return false;
            lock (Lock)
            {
                return Load().Contains(GameDeduplicationEngine.NormalizeGameTitle(gp.Name));
            }
        }

        public static bool Toggle(GameProfile gp)
        {
            if (gp == null) return false;
            lock (Lock)
            {
                HashSet<string> set = Load();
                string key = GameDeduplicationEngine.NormalizeGameTitle(gp.Name);
                if (string.IsNullOrEmpty(key)) return false;

                bool added;
                if (set.Contains(key)) { set.Remove(key); added = false; }
                else { set.Add(key); added = true; }

                try
                {
                    List<string> lines = new List<string>(set);
                    lines.Sort(StringComparer.OrdinalIgnoreCase);
                    File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
                }
                catch { }
                return added;
            }
        }

        public static List<string> All()
        {
            lock (Lock) { return new List<string>(Load()); }
        }
    }
}
