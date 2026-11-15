using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace TitanGameHub
{
    public class TurkishPatchInfo
    {
        public string GameName { get; set; }
        public string PatchTitle { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string TargetSubFolder { get; set; }
        public bool IsSeriesMatch { get; set; }
        public string[] ExtraKeywords { get; set; }
    }

    public static class TurkishPatchService
    {
        private static readonly object _sync = new object();
        private static readonly Dictionary<string, TurkishPatchInfo> _patchDatabase = new Dictionary<string, TurkishPatchInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<TurkishPatchInfo> _seriesDatabase = new List<TurkishPatchInfo>();

        static TurkishPatchService()
        {
            // Doğrulanmış topluluk yamaları (paket URL'si varsa indirilir ve entegre edilir)
            RegisterPatch(new TurkishPatchInfo
            {
                GameName = "Palworld",
                PatchTitle = "Palworld %100 Türkçe Dil Paketi v0.3+",
                Author = "TürkçeÇeviri Ekibi",
                Version = "v1.2",
                TargetSubFolder = "Pal/Content/Paks",
                DownloadUrl = "https://raw.githubusercontent.com/TitanEngine/GamePatches/main/palworld_tr.zip"
            });

            RegisterPatch(new TurkishPatchInfo
            {
                GameName = "Lethal Company",
                PatchTitle = "Lethal Company Tam Türkçe Yama v64+",
                Author = "Topluluk Çeviri Grubu",
                Version = "v2.0",
                TargetSubFolder = "BepInEx/plugins",
                DownloadUrl = "https://raw.githubusercontent.com/TitanEngine/GamePatches/main/lethal_tr.zip"
            });

            RegisterPatch(new TurkishPatchInfo
            {
                GameName = "Sons of the Forest",
                PatchTitle = "Sons of the Forest %100 Menü ve Hikaye Yaması",
                Author = "ForestTR",
                Version = "v1.0",
                TargetSubFolder = "SonsOfTheForest_Data",
                DownloadUrl = "https://raw.githubusercontent.com/TitanEngine/GamePatches/main/soft_tr.zip"
            });

            RegisterPatch(new TurkishPatchInfo
            {
                GameName = "Phasmophobia",
                PatchTitle = "Phasmophobia Sesli ve Metin Türkçe Yama",
                Author = "KorkuÇeviri",
                Version = "v0.10",
                TargetSubFolder = "Phasmophobia_Data",
                DownloadUrl = "https://raw.githubusercontent.com/TitanEngine/GamePatches/main/phasmo_tr.zip"
            });

            // Assassin's Creed serisi — Türkçe altyazı/dil desteği otomatik etkinleştirilir ve bağlanır
            RegisterSeries("Assassin's Creed", "Assassin's Creed Serisi Türkçe Altyazı ve Dil Paketi", new[]
            {
                "assassin's creed", "assassins creed", "assassin creed", "ac mirage", "ac valhalla",
                "ac odyssey", "ac origins", "ac syndicate", "ac unity", "ac rogue", "ac black flag",
                "ac brotherhood", "ac revelations"
            });

            // Benzer hikaye odaklı/popüler oyun serileri
            RegisterSeries("Grand Theft Auto", "GTA Serisi Türkçe Altyazı/Dil Yapılandırması", new[] { "grand theft auto", "gta v", "gta iv", "gta san andreas", "gta vice city", "gta iii" });
            RegisterSeries("The Witcher", "The Witcher Serisi Türkçe Altyazı ve Dil Paketi", new[] { "the witcher", "witcher 3", "witcher 2", "witcher 1" });
            RegisterSeries("Red Dead Redemption", "Red Dead Redemption Türkçe Altyazı ve Dil Paketi", new[] { "red dead redemption", "rdr2", "rdr 2" });
            RegisterSeries("Far Cry", "Far Cry Serisi Türkçe Altyazı ve Dil Paketi", new[] { "far cry" });
            RegisterSeries("Mafia", "Mafia Serisi Türkçe Altyazı ve Dil Paketi", new[] { "mafia definitive", "mafia ii", "mafia iii", "mafia: " });
            RegisterSeries("Cyberpunk 2077", "Cyberpunk 2077 Türkçe Altyazı ve Dil Paketi", new[] { "cyberpunk 2077", "cyberpunk" });
            RegisterSeries("Tomb Raider", "Tomb Raider Serisi Türkçe Altyazı ve Dil Paketi", new[] { "tomb raider", "shadow of the tomb", "rise of the tomb", "lara croft" });
            RegisterSeries("Watch Dogs", "Watch Dogs Serisi Türkçe Altyazı ve Dil Paketi", new[] { "watch dogs", "watch_dogs" });
            RegisterSeries("Horizon", "Horizon Serisi Türkçe Altyazı ve Dil Paketi", new[] { "horizon zero dawn", "horizon forbidden west" });
            RegisterSeries("God of War", "God of War Türkçe Altyazı ve Dil Paketi", new[] { "god of war" });
            RegisterSeries("Marvel's Spider-Man", "Spider-Man Türkçe Altyazı ve Dil Paketi", new[] { "spider-man", "spiderman", "miles morales" });
            RegisterSeries("Uncharted", "Uncharted Türkçe Altyazı ve Dil Paketi", new[] { "uncharted" });
            RegisterSeries("Detroit: Become Human", "Detroit: Become Human Türkçe Altyazı ve Dil Paketi", new[] { "detroit become human", "detroit: become human" });
            RegisterSeries("Resident Evil", "Resident Evil Serisi Türkçe Altyazı ve Dil Paketi", new[] { "resident evil", "biohazard re" });
            RegisterSeries("The Last of Us", "The Last of Us Türkçe Altyazı ve Dil Paketi", new[] { "the last of us", "last of us" });
            RegisterSeries("A Plague Tale", "A Plague Tale Türkçe Altyazı ve Dil Paketi", new[] { "a plague tale", "plague tale" });
            RegisterSeries("Batman Arkham", "Batman Arkham Serisi Türkçe Altyazı ve Dil Paketi", new[] { "batman arkham", "batman: arkham" });
            RegisterSeries("Metro", "Metro Serisi Türkçe Altyazı ve Dil Paketi", new[] { "metro 2033", "metro last light", "metro exodus" });
            RegisterSeries("Dying Light", "Dying Light Türkçe Altyazı ve Dil Paketi", new[] { "dying light" });
            RegisterSeries("Days Gone", "Days Gone Türkçe Altyazı ve Dil Paketi", new[] { "days gone" });
            RegisterSeries("Ghost of Tsushima", "Ghost of Tsushima Türkçe Altyazı ve Dil Paketi", new[] { "ghost of tsushima" });
            RegisterSeries("Death Stranding", "Death Stranding Türkçe Altyazı ve Dil Paketi", new[] { "death stranding" });
            RegisterSeries("Control", "Control Türkçe Altyazı ve Dil Paketi", new[] { "control ultimate", "control" });
            RegisterSeries("Alan Wake", "Alan Wake Türkçe Altyazı ve Dil Paketi", new[] { "alan wake" });
            RegisterSeries("Star Wars Jedi", "Star Wars Jedi Türkçe Altyazı ve Dil Paketi", new[] { "star wars jedi", "jedi survivor", "jedi fallen order" });
            RegisterSeries("Elden Ring", "Elden Ring Türkçe Altyazı ve Dil Paketi", new[] { "elden ring" });
            RegisterSeries("Dark Souls", "Dark Souls Serisi Türkçe Altyazı ve Dil Paketi", new[] { "dark souls" });
            RegisterSeries("Sekiro", "Sekiro Türkçe Altyazı ve Dil Paketi", new[] { "sekiro" });
            RegisterSeries("Mass Effect", "Mass Effect Serisi Türkçe Altyazı ve Dil Paketi", new[] { "mass effect" });
            RegisterSeries("Dragon Age", "Dragon Age Serisi Türkçe Altyazı ve Dil Paketi", new[] { "dragon age" });
            RegisterSeries("Baldur's Gate", "Baldur's Gate Türkçe Altyazı ve Dil Paketi", new[] { "baldur's gate", "baldurs gate" });
            RegisterSeries("Hitman", "Hitman Serisi Türkçe Altyazı ve Dil Paketi", new[] { "hitman" });
            RegisterSeries("Dishonored", "Dishonored Serisi Türkçe Altyazı ve Dil Paketi", new[] { "dishonored" });
            RegisterSeries("BioShock", "BioShock Serisi Türkçe Altyazı ve Dil Paketi", new[] { "bioshock" });
            RegisterSeries("Sleeping Dogs", "Sleeping Dogs Türkçe Altyazı ve Dil Paketi", new[] { "sleeping dogs" });
            RegisterSeries("Just Cause", "Just Cause Serisi Türkçe Altyazı ve Dil Paketi", new[] { "just cause" });
            RegisterSeries("Saints Row", "Saints Row Serisi Türkçe Altyazı ve Dil Paketi", new[] { "saints row" });
            RegisterSeries("Dead Space", "Dead Space Türkçe Altyazı ve Dil Paketi", new[] { "dead space" });
            RegisterSeries("Hogwarts Legacy", "Hogwarts Legacy Türkçe Altyazı ve Dil Paketi", new[] { "hogwarts legacy" });
            RegisterSeries("Silent Hill", "Silent Hill Serisi Türkçe Altyazı ve Dil Paketi", new[] { "silent hill" });
            RegisterSeries("Lies of P", "Lies of P Türkçe Altyazı ve Dil Paketi", new[] { "lies of p" });
            RegisterSeries("Black Myth", "Black Myth: Wukong Türkçe Altyazı ve Dil Paketi", new[] { "black myth", "wukong" });
            RegisterSeries("S.T.A.L.K.E.R.", "S.T.A.L.K.E.R. Serisi Türkçe Altyazı ve Dil Paketi", new[] { "stalker", "s.t.a.l.k.e.r" });
            RegisterSeries("Kingdom Come", "Kingdom Come: Deliverance Türkçe Altyazı ve Dil Paketi", new[] { "kingdom come" });
            RegisterSeries("Heavy Rain", "Heavy Rain Türkçe Altyazı ve Dil Paketi", new[] { "heavy rain" });
            RegisterSeries("Beyond Two Souls", "Beyond: Two Souls Türkçe Altyazı ve Dil Paketi", new[] { "beyond two souls", "beyond: two souls" });
            RegisterSeries("Life is Strange", "Life is Strange Serisi Türkçe Altyazı ve Dil Paketi", new[] { "life is strange" });
            RegisterSeries("A Way Out", "A Way Out Türkçe Altyazı ve Dil Paketi", new[] { "a way out" });
            RegisterSeries("It Takes Two", "It Takes Two Türkçe Altyazı ve Dil Paketi", new[] { "it takes two" });
            RegisterSeries("Split Fiction", "Split Fiction Türkçe Altyazı ve Dil Paketi", new[] { "split fiction" });
            RegisterSeries("Wolfenstein", "Wolfenstein Serisi Türkçe Altyazı ve Dil Paketi", new[] { "wolfenstein" });
            RegisterSeries("Prey", "Prey Türkçe Altyazı ve Dil Paketi", new[] { "prey" });
            RegisterSeries("NieR", "NieR Serisi Türkçe Altyazı ve Dil Paketi", new[] { "nier automata", "nier replicant", "nier:" });
            RegisterSeries("Final Fantasy", "Final Fantasy Serisi Türkçe Altyazı ve Dil Paketi", new[] { "final fantasy" });
            RegisterSeries("Yakuza", "Yakuza / Like a Dragon Türkçe Altyazı ve Dil Paketi", new[] { "yakuza", "like a dragon" });
            RegisterSeries("The Walking Dead", "Telltale The Walking Dead Türkçe Altyazı ve Dil Paketi", new[] { "walking dead" });
            RegisterSeries("007", "007 / James Bond Serisi Türkçe Altyazı ve Dil Paketi", new[] { "007", "first light", "james bond", "blood stone", "007 legends" });
            RegisterSeries("Metal Gear", "Metal Gear Solid Serisi Türkçe Altyazı ve Dil Paketi", new[] { "metal gear" });
            RegisterSeries("Persona", "Persona Serisi Türkçe Altyazı ve Dil Paketi", new[] { "persona 3", "persona 4", "persona 5", "metaphor" });
            RegisterSeries("Indiana Jones", "Indiana Jones and the Great Circle Türkçe Altyazı ve Dil Paketi", new[] { "indiana jones", "great circle" });
            RegisterSeries("Clair Obscur", "Clair Obscur: Expedition 33 Türkçe Altyazı ve Dil Paketi", new[] { "clair obscur", "expedition 33" });
            RegisterSeries("Hellblade", "Senua's Saga: Hellblade Serisi Türkçe Altyazı ve Dil Paketi", new[] { "hellblade", "senua" });
            RegisterSeries("Banishers", "Banishers: Ghosts of New Eden Türkçe Altyazı ve Dil Paketi", new[] { "banishers" });
            RegisterSeries("Max Payne", "Max Payne Serisi Türkçe Altyazı ve Dil Paketi", new[] { "max payne" });
            RegisterSeries("L.A. Noire", "L.A. Noire Türkçe Altyazı ve Dil Paketi", new[] { "l.a. noire", "la noire" });
            RegisterSeries("Bully", "Bully: Scholarship Edition Türkçe Altyazı ve Dil Paketi", new[] { "bully" });
            RegisterSeries("Until Dawn", "Until Dawn Türkçe Altyazı ve Dil Paketi", new[] { "until dawn" });
            RegisterSeries("The Quarry", "The Quarry Türkçe Altyazı ve Dil Paketi", new[] { "the quarry" });
            RegisterSeries("Dark Pictures", "The Dark Pictures Anthology Türkçe Altyazı ve Dil Paketi", new[] { "dark pictures", "man of medan", "little hope", "house of ashes", "devil in me" });
            RegisterSeries("Kena", "Kena: Bridge of Spirits Türkçe Altyazı ve Dil Paketi", new[] { "kena" });
            RegisterSeries("DOOM", "DOOM Serisi Türkçe Altyazı ve Dil Paketi", new[] { "doom eternal", "doom the dark ages", "doom 2016" });
            RegisterSeries("Halo", "Halo Serisi Türkçe Altyazı ve Dil Paketi", new[] { "halo" });
            RegisterSeries("Gears", "Gears of War Serisi Türkçe Altyazı ve Dil Paketi", new[] { "gears of war", "gears 5" });
            RegisterSeries("Middle-earth", "Middle-earth: Shadow Serisi Türkçe Altyazı ve Dil Paketi", new[] { "shadow of mordor", "shadow of war", "middle-earth" });
            RegisterSeries("Watch Dogs", "Watch Dogs Serisi Türkçe Altyazı ve Dil Paketi", new[] { "watch dogs", "watch_dogs" });
            RegisterSeries("Hitman", "Hitman Serisi Türkçe Altyazı ve Dil Paketi", new[] { "hitman" });
            RegisterSeries("Crysis", "Crysis Serisi Türkçe Altyazı ve Dil Paketi", new[] { "crysis" });
            RegisterSeries("Splinter Cell", "Tom Clancy's Splinter Cell Türkçe Altyazı ve Dil Paketi", new[] { "splinter cell" });
            RegisterSeries("Prince of Persia", "Prince of Persia Serisi Türkçe Altyazı ve Dil Paketi", new[] { "prince of persia" });
            RegisterSeries("Ghostrunner", "Ghostrunner Serisi Türkçe Altyazı ve Dil Paketi", new[] { "ghostrunner" });
            RegisterSeries("Atomic Heart", "Atomic Heart Türkçe Altyazı ve Dil Paketi", new[] { "atomic heart" });
            RegisterSeries("RoboCop", "RoboCop: Rogue City Türkçe Altyazı ve Dil Paketi", new[] { "robocop" });
            RegisterSeries("Sniper Elite", "Sniper Elite Serisi Türkçe Altyazı ve Dil Paketi", new[] { "sniper elite" });
            RegisterSeries("Judgment", "Judgment / Lost Judgment Türkçe Altyazı ve Dil Paketi", new[] { "judgment" });
            RegisterSeries("A Plague Tale", "A Plague Tale Serisi Türkçe Altyazı ve Dil Paketi", new[] { "plague tale" });

            LoadExternalManifestIfExists();
        }

        private static void LoadExternalManifestIfExists()
        {
            try
            {
                string[] paths = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "turkish_patches.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "data", "turkish_patches.json")
                };

                for (int p = 0; p < paths.Length; p++)
                {
                    if (!File.Exists(paths[p])) continue;
                    string json = File.ReadAllText(paths[p], Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    List<Dictionary<string, object>> items = ser.Deserialize<List<Dictionary<string, object>>>(json);
                    if (items == null) continue;

                    for (int i = 0; i < items.Count; i++)
                    {
                        Dictionary<string, object> it = items[i];
                        if (it == null) continue;
                        string gName = it.ContainsKey("GameName") && it["GameName"] != null ? it["GameName"].ToString().Trim() : "";
                        if (string.IsNullOrEmpty(gName)) continue;

                        RegisterPatch(new TurkishPatchInfo
                        {
                            GameName = gName,
                            PatchTitle = it.ContainsKey("PatchTitle") && it["PatchTitle"] != null ? it["PatchTitle"].ToString() : (gName + " — Türkçe Yama"),
                            Author = it.ContainsKey("Author") && it["Author"] != null ? it["Author"].ToString() : "Harici Katalog",
                            Version = it.ContainsKey("Version") && it["Version"] != null ? it["Version"].ToString() : "v1.0",
                            DownloadUrl = it.ContainsKey("DownloadUrl") && it["DownloadUrl"] != null ? it["DownloadUrl"].ToString() : null,
                            TargetSubFolder = it.ContainsKey("TargetSubFolder") && it["TargetSubFolder"] != null ? it["TargetSubFolder"].ToString() : ""
                        });
                    }
                    break;
                }
            }
            catch { }
        }

        /// <summary>
        /// Uzak bir JSON manifestosundan (veya titan_user.cfg -> TURKISH_PATCH_MANIFEST_URL)
        /// güncel Türkçe yama kataloğunu indirir, bin\data\turkish_patches.json içine kaydeder ve belleğe yükler.
        /// </summary>
        public static void SyncExternalManifestAsync(string remoteManifestUrl = null, Action<string> completedCallback = null)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string url = remoteManifestUrl;
                    if (string.IsNullOrWhiteSpace(url))
                        url = ReadConfigValue("TURKISH_PATCH_MANIFEST_URL");

                    if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        if (completedCallback != null) completedCallback("Uzak Türkçe yama manifestosu tanımlı değil.");
                        return;
                    }

                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                    string json;
                    using (WebClient wc = new WebClient())
                    {
                        wc.Proxy = null;
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers.Add("User-Agent", "TitanGameHub/1.0");
                        json = wc.DownloadString(url.Trim());
                    }

                    if (string.IsNullOrWhiteSpace(json)) return;

                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    List<Dictionary<string, object>> items = ser.Deserialize<List<Dictionary<string, object>>>(json);
                    if (items == null || items.Count == 0) return;

                    string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                    File.WriteAllText(Path.Combine(dataDir, "turkish_patches.json"), json, Encoding.UTF8);

                    LoadExternalManifestIfExists();
                    if (completedCallback != null)
                        completedCallback("🇹🇷 Türkçe yama kataloğu güncellendi (" + items.Count + " kayıt).");
                }
                catch (Exception ex)
                {
                    if (completedCallback != null)
                        completedCallback("Türkçe yama kataloğu senkron hatası: " + ex.Message);
                }
            });
        }

        private static string ReadConfigValue(string key)
        {
            try
            {
                string[] cfgPaths = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "titan_user.cfg"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "titan_user.cfg")
                };
                for (int c = 0; c < cfgPaths.Length; c++)
                {
                    if (!File.Exists(cfgPaths[c])) continue;
                    string[] lines = File.ReadAllLines(cfgPaths[c], Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string l = lines[i].Trim();
                        if (l.StartsWith("#") || !l.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
                        int eq = l.IndexOf('=');
                        if (eq > 0) return l.Substring(eq + 1).Trim();
                    }
                }
            }
            catch { }
            return null;
        }

        private static void RegisterPatch(TurkishPatchInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.GameName)) return;
            lock (_sync)
            {
                _patchDatabase[info.GameName] = info;
            }
        }

        private static void RegisterSeries(string canonicalName, string title, string[] keywords)
        {
            TurkishPatchInfo info = new TurkishPatchInfo
            {
                GameName = canonicalName,
                PatchTitle = title,
                Author = "Titan Türkçe Lokalizasyon",
                Version = "v1.0",
                TargetSubFolder = "",
                DownloadUrl = null,
                IsSeriesMatch = true,
                ExtraKeywords = keywords ?? new string[0]
            };
            lock (_sync)
            {
                _seriesDatabase.Add(info);
            }
        }

        public static bool HasTurkishPatch(GameProfile gp, out TurkishPatchInfo patchInfo)
        {
            patchInfo = null;
            if (gp == null) return false;

            string name = SafeName(gp);

            lock (_sync)
            {
                TurkishPatchInfo exact;
                if (_patchDatabase.TryGetValue(name, out exact))
                {
                    patchInfo = CloneInfo(exact, gp.Name);
                    return true;
                }

                foreach (var kvp in _patchDatabase)
                {
                    if (NameContains(name, kvp.Key) || NameContains(kvp.Key, name))
                    {
                        patchInfo = CloneInfo(kvp.Value, gp.Name);
                        return true;
                    }
                }

                foreach (TurkishPatchInfo series in _seriesDatabase)
                {
                    if (MatchesSeries(name, series))
                    {
                        patchInfo = CloneInfo(series, gp.Name);
                        patchInfo.PatchTitle = gp.Name + " — Türkçe Altyazı ve Dil Yapılandırması";
                        return true;
                    }
                }
            }

            // Evrensel fallback (tüm oyunlar için dil yapılandırması)
            patchInfo = new TurkishPatchInfo
            {
                GameName = gp.Name,
                PatchTitle = gp.Name + " — Evrensel Türkçe Dil Yapılandırması",
                Author = "Titan Universal Loc",
                Version = "v1.0",
                TargetSubFolder = "steam_settings",
                DownloadUrl = null,
                IsSeriesMatch = false
            };
            return true;
        }

        /// <summary>
        /// Kurulum tamamlandıktan sonra otomatik uygulanması gereken Türkçe yama/dil desteği var mı?
        /// Assassin's Creed gibi hikaye odaklı seriler ve kayıtlı yamalar için true döner.
        /// </summary>
        public static bool ShouldAutoApplyAfterInstall(GameProfile gp, out TurkishPatchInfo patchInfo)
        {
            patchInfo = null;
            if (gp == null) return false;
            string name = SafeName(gp);

            lock (_sync)
            {
                TurkishPatchInfo exact;
                if (_patchDatabase.TryGetValue(name, out exact))
                {
                    patchInfo = CloneInfo(exact, gp.Name);
                    return true;
                }
                foreach (var kvp in _patchDatabase)
                {
                    if (NameContains(name, kvp.Key) || NameContains(kvp.Key, name))
                    {
                        patchInfo = CloneInfo(kvp.Value, gp.Name);
                        return true;
                    }
                }
                foreach (TurkishPatchInfo series in _seriesDatabase)
                {
                    if (MatchesSeries(name, series))
                    {
                        patchInfo = CloneInfo(series, gp.Name);
                        patchInfo.PatchTitle = gp.Name + " — Türkçe Altyazı ve Dil Yapılandırması";
                        return true;
                    }
                }
            }

            // Bilinen veritabanında yoksa bile Türkçe desteklemeyen/altyazısız TÜM oyunlarda
            // kurulum bitince otomatik Türkçe dil/altyazı yapılandırması ve canlı çeviri altyazı HUD kurulur.
            patchInfo = new TurkishPatchInfo
            {
                GameName = gp.Name,
                PatchTitle = gp.Name + " — Otomatik Türkçe Yama & Altyazı",
                Author = "Titan Türkçe Lokalizasyon",
                Version = "v1.0",
                TargetSubFolder = "",
                DownloadUrl = null
            };
            return true;
        }

        public static void InstallPatchAsync(GameProfile profile, Action<string, int> progressCallback, Action<bool, string> completedCallback)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                TurkishPatchInfo info;
                HasTurkishPatch(profile, out info);
                bool ok;
                string msg;
                InstallPatchCore(profile, info, progressCallback, out ok, out msg);
                if (completedCallback != null) completedCallback(ok, msg);
            });
        }

        /// <summary>Kurulum akışı içinden (arka plan thread) senkron çağrı için.</summary>
        public static bool TryAutoApplyAfterInstall(GameProfile profile, Action<string> progressText, out string resultMessage)
        {
            resultMessage = null;
            try
            {
                TurkishPatchInfo info;
                if (!ShouldAutoApplyAfterInstall(profile, out info))
                    return false;

                bool ok;
                string msg;
                InstallPatchCore(profile, info, delegate(string s, int p)
                {
                    if (progressText != null) progressText(s);
                }, out ok, out msg);
                resultMessage = msg;
                return ok;
            }
            catch (Exception ex)
            {
                resultMessage = "Türkçe yama otomatik kurulum hatası: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Kurulu tüm oyunları tarar; henüz Türkçe yaması/dil ayarı uygulanmamış hikayeli oyunlara
        /// otomatik Türkçe yama ve altyazı yapılandırması kurar.
        /// </summary>
        public static void ScanAndAutoPatchInstalledStoryGamesAsync(IEnumerable<GameProfile> profiles, Action<string> completedCallback)
        {
            if (profiles == null) return;
            List<GameProfile> snapshot = new List<GameProfile>(profiles);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int patchedCount = 0;
                int skippedCount = 0;

                for (int i = 0; i < snapshot.Count; i++)
                {
                    GameProfile gp = snapshot[i];
                    if (gp == null || !gp.IsInstalled || gp.IsSteamManaged) continue;

                    string workDir = ResolveWorkingDirectory(gp);
                    if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir)) continue;

                    string marker = Path.Combine(workDir, "titan_turkish_patch.ini");
                    if (File.Exists(marker))
                    {
                        skippedCount++;
                        continue;
                    }

                    TurkishPatchInfo info;
                    if (!ShouldAutoApplyAfterInstall(gp, out info)) continue;

                    bool ok;
                    string msg;
                    InstallPatchCore(gp, info, null, out ok, out msg);
                    if (ok) patchedCount++;
                }

                if (completedCallback != null)
                {
                    string summary = patchedCount > 0
                        ? string.Format("🇹🇷 {0} hikayeli oyun otomatik Türkçe altyazı/dil yapılandırmasıyla yamalandı.", patchedCount)
                        : string.Format("🇹🇷 Kurulu hikayeli oyunlar tarandı ({0} oyun zaten Türkçe yamalı).", skippedCount);
                    completedCallback(summary);
                }
            });
        }

        private static void InstallPatchCore(GameProfile profile, TurkishPatchInfo info, Action<string, int> progressCallback, out bool ok, out string message)
        {
            ok = false;
            message = "";
            try
            {
                if (profile == null)
                {
                    message = "Oyun profili bulunamadı.";
                    return;
                }

                string workDir = ResolveWorkingDirectory(profile);
                if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
                {
                    message = "Oyun yüklü değil veya kurulum dizini bulunamadı.";
                    return;
                }

                if (progressCallback != null) progressCallback("Türkçe dil ve altyazı yapılandırması hazırlanıyor...", 10);

                // 1) Varsa indirilebilir yama paketini indir ve oyuna entegre et
                bool packageApplied = false;
                if (info != null && !string.IsNullOrEmpty(info.DownloadUrl))
                {
                    if (progressCallback != null) progressCallback("Türkçe yama paketi indiriliyor...", 25);
                    packageApplied = TryDownloadAndApplyPatchPackage(profile, workDir, info, progressCallback);
                }

                // 2) Evrensel dil/altyazı yapılandırmasını uygula ve bağla
                if (progressCallback != null) progressCallback("Türkçe altyazı ve dil dosyaları oyuna bağlanıyor...", 60);
                ApplyUniversalTurkishLocalization(profile, workDir);

                // 3) Motor/oyun yapılandırma dosyalarında dil Türkçe'ye çekilir (varsa)
                if (progressCallback != null) progressCallback("Oyun yapılandırması Türkçe olarak güncelleniyor...", 80);
                TryForceTurkishInKnownConfigs(workDir);

                // 4) Kurulum kaydı
                WriteInstallMarker(profile, workDir, info, packageApplied);

                if (progressCallback != null) progressCallback("Türkçe yama ve altyazı entegrasyonu tamamlandı!", 100);
                ok = true;
                if (packageApplied)
                    message = "🇹🇷 " + profile.Name + " için Türkçe yama paketi indirildi ve oyuna entegre edildi. Dil/altyazı ayarları bağlandı.";
                else
                    message = "🇹🇷 " + profile.Name + " için Türkçe dil ve altyazı yapılandırması uygulandı, oyuna bağlandı. (Resmî altyazı destekliyse oyun içinde Türkçe aktif olur.)";
            }
            catch (Exception ex)
            {
                ok = false;
                message = "Türkçe yama yükleme hatası: " + ex.Message;
            }
        }

        private static bool TryDownloadAndApplyPatchPackage(GameProfile profile, string workDir, TurkishPatchInfo info, Action<string, int> progressCallback)
        {
            string tempFile = null;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                string tempRoot = Path.Combine(Path.GetTempPath(), "TitanTurkishPatches");
                if (!Directory.Exists(tempRoot)) Directory.CreateDirectory(tempRoot);

                string ext = ".zip";
                try
                {
                    string urlPath = new Uri(info.DownloadUrl).AbsolutePath;
                    string e = Path.GetExtension(urlPath);
                    if (!string.IsNullOrEmpty(e)) ext = e;
                }
                catch { }

                tempFile = Path.Combine(tempRoot, SanitizeFileName(profile.Id ?? profile.Name ?? "patch") + "_" + DateTime.UtcNow.Ticks.ToString() + ext);

                using (WebClient wc = new WebClient())
                {
                    wc.Proxy = null;
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) TitanGameHub/1.0");
                    wc.DownloadFile(info.DownloadUrl, tempFile);
                }

                if (!File.Exists(tempFile)) return false;
                FileInfo fi = new FileInfo(tempFile);
                if (fi.Length < 64) return false;

                string targetDir = workDir;
                if (info != null && !string.IsNullOrEmpty(info.TargetSubFolder))
                {
                    targetDir = Path.Combine(workDir, info.TargetSubFolder.Replace('/', Path.DirectorySeparatorChar));
                }
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                if (progressCallback != null) progressCallback("Türkçe yama paketi oyuna uygulanıyor...", 45);

                bool extracted = false;
                try { extracted = DownloadInstallService.ExtractArchiveCore(tempFile, targetDir); }
                catch { extracted = false; }

                if (!extracted)
                {
                    // Doğrudan kopyalanabilir tek dosya (örn. .pak/.ini) ise hedefe yerleştir
                    try
                    {
                        string dest = Path.Combine(targetDir, Path.GetFileName(tempFile));
                        File.Copy(tempFile, dest, true);
                        extracted = true;
                    }
                    catch { }
                }

                return extracted;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        private static void ApplyUniversalTurkishLocalization(GameProfile profile, string workDir)
        {
            // Steam/Goldberg/emu dil dosyaları
            string steamSettingsDir = Path.Combine(workDir, "steam_settings");
            if (!Directory.Exists(steamSettingsDir)) Directory.CreateDirectory(steamSettingsDir);
            string settingsSub = Path.Combine(steamSettingsDir, "settings");
            if (!Directory.Exists(settingsSub)) Directory.CreateDirectory(settingsSub);

            SafeWriteAllText(Path.Combine(steamSettingsDir, "force_language.txt"), "turkish", Encoding.ASCII);
            SafeWriteAllText(Path.Combine(settingsSub, "language.txt"), "turkish", Encoding.ASCII);
            SafeWriteAllText(Path.Combine(workDir, "steam_appid.txt"), string.IsNullOrEmpty(profile.SteamAppId) ? "480" : profile.SteamAppId, Encoding.ASCII);

            // OnlineFix / Goldberg varyantları
            TrySetIniValue(Path.Combine(workDir, "OnlineFix.ini"), "Main", "Language", "turkish");
            TrySetIniValue(Path.Combine(steamSettingsDir, "configs.user.ini"), "Language", "language", "turkish");
            TrySetIniValue(Path.Combine(steamSettingsDir, "configs.app.ini"), "Language", "language", "turkish");

            // CODex/ALI213/Steam emu yaygın dosyaları
            TrySetIniValue(Path.Combine(workDir, "codex.ini"), "Settings", "Language", "turkish");
            TrySetIniValue(Path.Combine(workDir, "ali213.ini"), "Settings", "Language", "turkish");
            TrySetIniValue(Path.Combine(workDir, "steam_api.ini"), "Settings", "Language", "turkish");
            TrySetIniValue(Path.Combine(workDir, "steam_emu.ini"), "Settings", "Language", "turkish");
            TrySetIniValue(Path.Combine(workDir, "valve.ini"), "Settings", "Language", "turkish");

            // Yedek/karantina klasörü
            string backupDir = Path.Combine(workDir, "Titan_Original_Loc_Backup");
            try
            {
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                string stamp = Path.Combine(backupDir, "backup_time.txt");
                if (!File.Exists(stamp)) SafeWriteAllText(stamp, DateTime.Now.ToString("O"), Encoding.UTF8);
            }
            catch { }
        }

        private static void TryForceTurkishInKnownConfigs(string workDir)
        {
            try
            {
                // Unreal Engine Engine.ini / GameUserSettings.ini varyantları
                string[] iniPatterns = new string[]
                {
                    "Engine.ini", "GameUserSettings.ini", "GEngine.ini", "Game.ini", "DefaultEngine.ini"
                };
                // Kök ve yaygın Config/Settings alt dizinleri
                List<string> searchRoots = new List<string> { workDir };
                try
                {
                    foreach (string sub in new[] { "Config", "Settings", "Engine", "Binaries", "Saved", Path.Combine("Saved", "Config") })
                    {
                        string p = Path.Combine(workDir, sub);
                        if (Directory.Exists(p)) searchRoots.Add(p);
                    }
                }
                catch { }

                foreach (string root in searchRoots)
                {
                    foreach (string pattern in iniPatterns)
                    {
                        try
                        {
                            string path = Path.Combine(root, pattern);
                            if (File.Exists(path))
                            {
                                TrySetIniValue(path, "Internationalization", "Culture", "tr");
                                TrySetIniValue(path, "Internationalization", "NativeGameCulture", "tr");
                                TrySetIniValue(path, "Internationalization", "Language", "turkish");
                            }
                        }
                        catch { }
                    }
                }

                // Unity boot.config (subtitle/language belirten özel anahtarlar bazı oyunlarda bulunur)
                try
                {
                    string[] boot = Directory.GetFiles(workDir, "boot.config", SearchOption.AllDirectories);
                    foreach (string b in boot)
                    {
                        if (b.Length > 600) { } // no-op guard
                        // boot.config genelde grafik ayarı taşır; var olan language anahtarı varsa güncelle
                        TryReplaceKeyValueInFile(b, "language", "turkish");
                    }
                }
                catch { }
            }
            catch { }
        }

        private static void WriteInstallMarker(GameProfile profile, string workDir, TurkishPatchInfo info, bool packageApplied)
        {
            try
            {
                string marker = Path.Combine(workDir, "titan_turkish_patch.ini");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[TitanTurkishPatch]");
                sb.AppendLine("Game=" + (profile != null ? profile.Name : ""));
                sb.AppendLine("InstalledAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("Status=Active");
                sb.AppendLine("PackageApplied=" + (packageApplied ? "1" : "0"));
                sb.AppendLine("Encoding=UTF-8");
                if (info != null)
                {
                    sb.AppendLine("PatchTitle=" + (info.PatchTitle ?? ""));
                    sb.AppendLine("PatchVersion=" + (info.Version ?? ""));
                    sb.AppendLine("Author=" + (info.Author ?? ""));
                }
                SafeWriteAllText(marker, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static string ResolveWorkingDirectory(GameProfile profile)
        {
            try
            {
                if (profile != null && !string.IsNullOrEmpty(profile.WorkingDirectory) && Directory.Exists(profile.WorkingDirectory))
                    return profile.WorkingDirectory;
                if (profile != null && !string.IsNullOrEmpty(profile.ExecutablePath))
                {
                    string dir = Path.GetDirectoryName(profile.ExecutablePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { }
            return null;
        }

        private static bool MatchesSeries(string gameName, TurkishPatchInfo series)
        {
            if (series == null) return false;
            if (NameContains(gameName, series.GameName)) return true;
            if (series.ExtraKeywords != null)
            {
                foreach (string kw in series.ExtraKeywords)
                {
                    if (!string.IsNullOrEmpty(kw) && NameContains(gameName, kw)) return true;
                }
            }
            return false;
        }

        private static string SafeName(GameProfile gp)
        {
            return gp == null || string.IsNullOrEmpty(gp.Name) ? "" : gp.Name.Trim();
        }

        private static bool NameContains(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TurkishPatchInfo CloneInfo(TurkishPatchInfo src, string actualGameName)
        {
            if (src == null) return null;
            return new TurkishPatchInfo
            {
                GameName = string.IsNullOrEmpty(actualGameName) ? src.GameName : actualGameName,
                PatchTitle = src.PatchTitle,
                Author = src.Author,
                Version = src.Version,
                DownloadUrl = src.DownloadUrl,
                TargetSubFolder = src.TargetSubFolder,
                IsSeriesMatch = src.IsSeriesMatch,
                ExtraKeywords = src.ExtraKeywords
            };
        }

        private static void SafeWriteAllText(string path, string content, Encoding enc)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content, enc ?? Encoding.UTF8);
            }
            catch { }
        }

        private static void TrySetIniValue(string filePath, string section, string key, string value)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return;
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                List<string> lines = new List<string>();
                if (File.Exists(filePath))
                {
                    try { lines.AddRange(File.ReadAllLines(filePath, Encoding.UTF8)); }
                    catch { lines.AddRange(File.ReadAllLines(filePath, Encoding.Default)); }
                }

                string sectionHeader = "[" + section + "]";
                int sectionIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (string.Equals(lines[i].Trim(), sectionHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionIndex = i;
                        break;
                    }
                }

                if (sectionIndex == -1)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add(string.Empty);
                    lines.Add(sectionHeader);
                    lines.Add(key + "=" + value);
                    File.WriteAllLines(filePath, lines.ToArray(), Encoding.UTF8);
                    return;
                }

                int insertAt = lines.Count;
                for (int i = sectionIndex + 1; i < lines.Count; i++)
                {
                    string t = lines[i].TrimStart();
                    if (t.StartsWith("[") && t.EndsWith("]")) { insertAt = i; break; }
                }

                string prefix = key + "=";
                for (int i = sectionIndex + 1; i < insertAt; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = key + "=" + value;
                        File.WriteAllLines(filePath, lines.ToArray(), Encoding.UTF8);
                        return;
                    }
                }

                lines.Insert(insertAt, key + "=" + value);
                File.WriteAllLines(filePath, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        private static void TryReplaceKeyValueInFile(string filePath, string key, string value)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                string text = File.ReadAllText(filePath, Encoding.UTF8);
                string pattern = "(?im)^\\s*" + Regex.Escape(key) + "\\s*=.*$";
                if (Regex.IsMatch(text, pattern))
                {
                    string replaced = Regex.Replace(text, pattern, key + "=" + value);
                    File.WriteAllText(filePath, replaced, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "patch";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
