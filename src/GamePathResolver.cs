using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TitanGameHub
{
    /// <summary>
    /// Portability layer: locates the real install root of every catalog entry on ANY machine.
    /// The catalog ships with the author's absolute paths (D:\Games\...); this resolver rewrites
    /// them to whatever exists locally (portable Games folder, extra drives, Steam/Epic libraries).
    /// No hard dependency on a drive letter, so the package works when handed to someone else.
    /// </summary>
    public static class GamePathResolver
    {
        private static readonly object _lock = new object();
        private static List<string> _cachedRoots = null;
        private static DateTime _cacheStamp = DateTime.MinValue;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(3);

        public static readonly string[] CommonLeafFolders = new string[]
        {
            "Games",
            "Oyunlar",
            Path.Combine("SteamLibrary", "steamapps", "common"),
            Path.Combine("SteamLibrary2", "steamapps", "common"),
            Path.Combine("Epic Games"),
            "GOG Games",
            "GameLibrary"
        };

        /// <summary>Allowed library roots, ordered by trust/priority.</summary>
        public static List<string> GetLibraryRoots(bool forceRefresh)
        {
            lock (_lock)
            {
                if (!forceRefresh && _cachedRoots != null && (DateTime.UtcNow - _cacheStamp) < CacheLifetime)
                    return new List<string>(_cachedRoots);

                List<string> roots = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Action<string> add = delegate(string p)
                {
                    if (string.IsNullOrEmpty(p)) return;
                    string full;
                    try { full = Path.GetFullPath(p).TrimEnd('\\'); }
                    catch { return; }
                    if (full.Length < 4) return;
                    if (!seen.Add(full)) return;
                    try { if (Directory.Exists(full)) roots.Add(full); }
                    catch { }
                };

                // 1. Portable root next to the executable (the package's own Games folder).
                add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Games"));

                // 2. Explicit override set by the UI / environment.
                try { add(Environment.GetEnvironmentVariable("TITAN_GAMES_ROOT")); }
                catch { }
                try { add(DownloadInstallService.CustomGamesRootDirectory); }
                catch { }

                // 3. Steam libraries (registry + libraryfolders.vdf, all drives).
                foreach (string steamRoot in GetSteamLibraryRoots())
                    add(steamRoot);

                // 4. Every mounted fixed drive: standard game leaf folders only (no full-disk walk).
                foreach (string drive in GetFixedDrives())
                {
                    foreach (string leaf in CommonLeafFolders)
                        add(Path.Combine(drive, leaf));
                }

                // 5. Legacy absolute paths kept for backwards compatibility with existing installs.
                add(@"D:\Games");
                add(@"D:\Oyunlar");
                add(@"C:\Games");

                _cachedRoots = roots;
                _cacheStamp = DateTime.UtcNow;
                return new List<string>(roots);
            }
        }

        public static void InvalidateCache()
        {
            lock (_lock) { _cachedRoots = null; _cacheStamp = DateTime.MinValue; }
        }

        private static IEnumerable<string> GetFixedDrives()
        {
            List<string> drives = new List<string>();
            DriveInfo[] all = null;
            try { all = DriveInfo.GetDrives(); }
            catch { }
            if (all == null) return drives;

            foreach (DriveInfo d in all)
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                    if (!d.IsReady) continue;
                    drives.Add(d.RootDirectory.FullName);
                }
                catch { }
            }
            return drives;
        }

        public static List<string> GetSteamLibraryRoots()
        {
            List<string> result = new List<string>();
            List<string> steamRoots = new List<string>();

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("SteamPath");
                        if (v != null && !string.IsNullOrEmpty(v.ToString()))
                            steamRoots.Add(v.ToString().Replace('/', '\\'));
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("InstallPath");
                        if (v != null && !string.IsNullOrEmpty(v.ToString()))
                            steamRoots.Add(v.ToString().Replace('/', '\\'));
                    }
                }
            }
            catch { }

            foreach (string root in steamRoots)
            {
                try
                {
                    string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        foreach (string line in File.ReadAllLines(vdf))
                        {
                            Match m = Regex.Match(line, "\"path\"\\s+\"([^\"]+)\"");
                            if (m.Success)
                            {
                                string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                                string common = Path.Combine(lib, "steamapps", "common");
                                if (Directory.Exists(common)) result.Add(common);
                            }
                        }
                    }
                    string ownCommon = Path.Combine(root, "steamapps", "common");
                    if (Directory.Exists(ownCommon)) result.Add(ownCommon);
                }
                catch { }
            }

            return result;
        }

        private static Dictionary<string, string> _indexCache = null;
        private static DateTime _indexStamp = DateTime.MinValue;

        /// <summary>
        /// Tüm kütüphane köklerini BİR kez tarayıp "klasör adı -> ana exe" indeksi kurar.
        /// 600+ oyunluk katalogda her profil için ayrı dizin taraması yapılmasını engeller.
        /// </summary>
        public static Dictionary<string, string> BuildLibraryIndex(bool forceRefresh)
        {
            lock (_lock)
            {
                if (!forceRefresh && _indexCache != null && (DateTime.UtcNow - _indexStamp).TotalMinutes < 5)
                    return _indexCache;

                Dictionary<string, string> index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string root in GetLibraryRoots(forceRefresh))
                {
                    string[] children;
                    try { children = Directory.GetDirectories(root); }
                    catch { continue; }

                    foreach (string child in children)
                    {
                        string key = NormalizeKey(Path.GetFileName(child));
                        if (key.Length < 2 || index.ContainsKey(key)) continue;

                        string exe = FindMainExeShallow(child);
                        if (!string.IsNullOrEmpty(exe)) index[key] = exe;
                    }
                }

                _indexCache = index;
                _indexStamp = DateTime.UtcNow;
                return index;
            }
        }

        /// <summary>
        /// Hızlı (derin olmayan) ana exe bulucu: kök + bin/binaries/win64/game klasörlerinde
        /// en fazla iki seviye gezer. İndeks kurulumunda kullanılır (600+ klasörde saniyeler kazandırır).
        /// </summary>
        private static string FindMainExeShallow(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return null;

                List<string> exes = new List<string>();
                try { exes.AddRange(Directory.GetFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)); }
                catch { }

                if (exes.Count == 0)
                {
                    string[] subDirs;
                    try { subDirs = Directory.GetDirectories(folder); }
                    catch { subDirs = new string[0]; }

                    foreach (string sd in subDirs)
                    {
                        string n = Path.GetFileName(sd).ToLowerInvariant();
                        if (n != "bin" && n != "binaries" && n != "win64" && n != "game" && n != "build" && n != "retail") continue;
                        try
                        {
                            exes.AddRange(Directory.GetFiles(sd, "*.exe", SearchOption.TopDirectoryOnly));
                            foreach (string sub2 in Directory.GetDirectories(sd))
                            {
                                try { exes.AddRange(Directory.GetFiles(sub2, "*.exe", SearchOption.TopDirectoryOnly)); }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                string folderKey = NormalizeKey(Path.GetFileName(folder.TrimEnd('\\', '/')));
                string best = null;
                int bestScore = -1;
                long bestSize = -1;

                foreach (string exe in exes)
                {
                    if (!IsPlausibleMainExe(exe)) continue;

                    string exeKey = NormalizeKey(Path.GetFileNameWithoutExtension(exe));
                    int score = 1;
                    if (exeKey.Length > 0 && exeKey == folderKey) score = 3;
                    else if (folderKey.Length >= 4 && exeKey.Length >= 4 &&
                             (exeKey.Contains(folderKey) || folderKey.Contains(exeKey))) score = 2;

                    long size = 0;
                    try { size = new FileInfo(exe).Length; } catch { }

                    if (score > bestScore || (score == bestScore && size > bestSize))
                    {
                        bestScore = score;
                        bestSize = size;
                        best = exe;
                    }
                }
                return best;
            }
            catch { return null; }
        }

        /// <summary>İndeks üzerinden hızlı eşleştirme (contaiment toleranslı).</summary>
        public static bool TryRelocateFromIndex(GameProfile gp, Dictionary<string, string> index)
        {
            if (gp == null || index == null || index.Count == 0) return false;
            if (!string.IsNullOrEmpty(gp.ExecutablePath) && File.Exists(gp.ExecutablePath)) return true;

            string wanted = NormalizeKey(gp.Name);
            string wantedId = NormalizeKey(gp.Id);

            string exe;
            if (wanted.Length >= 3 && index.TryGetValue(wanted, out exe)) { Apply(gp, exe); return true; }
            if (wantedId.Length >= 3 && index.TryGetValue(wantedId, out exe)) { Apply(gp, exe); return true; }

            if (wanted.Length >= 5)
            {
                foreach (KeyValuePair<string, string> kv in index)
                {
                    if (kv.Key.Length < 4) continue;
                    if (kv.Key.Contains(wanted) || wanted.Contains(kv.Key))
                    {
                        Apply(gp, kv.Value);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Lowercase, punctuation-free comparison key ("R.E.P.O." -> "repo").</summary>
        public static string NormalizeKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string n = s.ToLowerInvariant();
            n = Regex.Replace(n, @"[^a-z0-9]", "");
            return n;
        }

        /// <summary>
        /// Attempts to locate an installed copy of the profile on this machine and rewrite
        /// ExecutablePath / WorkingDirectory in place. Returns true when a valid exe was found.
        /// </summary>
        public static bool TryRelocate(GameProfile gp)
        {
            if (gp == null) return false;

            // 0. Already valid.
            if (!string.IsNullOrEmpty(gp.ExecutablePath) && File.Exists(gp.ExecutablePath))
                return true;

            string exeName = null;
            if (!string.IsNullOrEmpty(gp.ExecutablePath))
            {
                try { exeName = Path.GetFileName(gp.ExecutablePath); } catch { }
            }

            string wanted = NormalizeKey(gp.Name);
            string wantedId = NormalizeKey(gp.Id);

            List<string> roots = GetLibraryRoots(false);

            // 1. Direct child folder with a matching normalized name.
            foreach (string root in roots)
            {
                string direct = Path.Combine(root, SanitizeFolder(gp.Name));
                string exe = SafeFindExe(direct);
                if (!string.IsNullOrEmpty(exe)) { Apply(gp, exe); return true; }
            }

            // 2. Bounded scan: root\child (1 level), name containment.
            foreach (string root in roots)
            {
                string[] children;
                try { children = Directory.GetDirectories(root); }
                catch { continue; }

                foreach (string child in children)
                {
                    string key = NormalizeKey(Path.GetFileName(child));
                    bool hit = (key.Length > 0 && key == wanted) ||
                               (wanted.Length >= 4 && key.Contains(wanted)) ||
                               (wanted.Length >= 4 && wanted.Contains(key) && key.Length >= 4) ||
                               (wantedId.Length >= 4 && key.Contains(wantedId));
                    if (!hit) continue;

                    string exe = SafeFindExe(child);
                    if (!string.IsNullOrEmpty(exe)) { Apply(gp, exe); return true; }
                }
            }

            // 3. Known exe filename anywhere inside the portable root (bounded depth 2).
            if (!string.IsNullOrEmpty(exeName))
            {
                foreach (string root in roots)
                {
                    string found = SearchByName(root, exeName, 2);
                    if (!string.IsNullOrEmpty(found)) { Apply(gp, found); return true; }
                }
            }

            return false;
        }

        private static void Apply(GameProfile gp, string exe)
        {
            gp.ExecutablePath = exe;
            string dir;
            try { dir = Path.GetDirectoryName(exe); } catch { dir = null; }
            gp.WorkingDirectory = dir;
        }

        private static string SafeFindExe(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return null;
                string exe = ProfileStore.FindMainExeInFolder(folder);
                if (!IsPlausibleMainExe(exe)) return null;
                return exe;
            }
            catch { return null; }
        }

        /// <summary>
        /// Rejects installer / redistributable / anti-cheat helper binaries that are often the
        /// largest .exe in a game folder (e.g. EpicOnlineServicesInstaller.exe, 304 MB).
        /// </summary>
        public static bool IsPlausibleMainExe(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            string name;
            try { name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant(); }
            catch { return false; }
            if (name.Length == 0) return false;

            string[] badTokens = new string[]
            {
                "installer", "unins", "setup", "redist", "dxsetup", "vcredist", "vc_redist",
                "epiconlineservices", "eossdk", "easyanticheat", "battleye", "prereq",
                "dotnet", "directx", "crashreport", "crashhandler", "unitycrash", "subprocess",
                "launcher", "dsymutil", "uninstall", "vconsole", "touchup", "benchmark", "crashpad",
                "steamerrorreporter", "eac_", "easyanticheat_eos", "unrealcefsubprocess"
            };
            for (int i = 0; i < badTokens.Length; i++)
            {
                if (name.IndexOf(badTokens[i], StringComparison.Ordinal) >= 0)
                    return false;
            }
            return true;
        }

        public static string SanitizeFolder(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Game";
            return Regex.Replace(name, @"[^\w\s\.-]", "").Trim();
        }

        private static string SearchByName(string root, string fileName, int maxDepth)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                return SearchByNameRecursive(root, fileName, 0, maxDepth);
            }
            catch { return null; }
        }

        private static string SearchByNameRecursive(string dir, string fileName, int depth, int maxDepth)
        {
            try
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
                if (depth >= maxDepth) return null;

                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string n = Path.GetFileName(sub).ToLowerInvariant();
                    if (n == "windows" || n == "$recycle.bin" || n == "system volume information") continue;
                    string hit = SearchByNameRecursive(sub, fileName, depth + 1, maxDepth);
                    if (!string.IsNullOrEmpty(hit)) return hit;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Free bytes available on the volume that would hold the target folder.</summary>
        public static long GetFreeBytes(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root)) return -1;
                DriveInfo di = new DriveInfo(root);
                if (!di.IsReady) return -1;
                return di.AvailableFreeSpace;
            }
            catch { return -1; }
        }
    }
}
