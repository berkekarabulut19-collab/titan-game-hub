using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    public class GameSignatureProfile
    {
        public string GameId { get; set; }
        public string EngineTag { get; set; }
        public string SpeedPattern { get; set; }
        public string StaminaPattern { get; set; }
        public string CoordPattern { get; set; }
        public int[] PlayerCoordOffsets { get; set; }

        public IntPtr ResolvedSpeedAddr { get; set; }
        public IntPtr ResolvedStaminaAddr { get; set; }
        public IntPtr ResolvedCoordAddr { get; set; }
    }

    public static class SignatureDatabaseService
    {
        private static readonly string SigDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signatures_v7.cfg");
        private static Dictionary<string, GameSignatureProfile> _db = new Dictionary<string, GameSignatureProfile>(StringComparer.OrdinalIgnoreCase);

        static SignatureDatabaseService()
        {
            LoadDatabase();
        }

        public static void LoadDatabase()
        {
            _db.Clear();
            RegisterDefaults();

            if (!File.Exists(SigDbPath))
            {
                SaveDatabase();
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(SigDbPath, Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] p = line.Split('|');
                    if (p.Length >= 5)
                    {
                        GameSignatureProfile prof = new GameSignatureProfile
                        {
                            GameId = p[0].Trim(),
                            EngineTag = p[1].Trim(),
                            SpeedPattern = p[2].Trim(),
                            StaminaPattern = p[3].Trim(),
                            CoordPattern = p[4].Trim(),
                            PlayerCoordOffsets = new int[] { 0x18, 0x30, 0x90 }
                        };
                        _db[prof.GameId] = prof;
                    }
                }
            }
            catch { }
        }

        public static void SaveDatabase()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# TitanGameHub v7.0 Cloud AOB Signature Database");
                foreach (KeyValuePair<string, GameSignatureProfile> kv in _db)
                {
                    GameSignatureProfile g = kv.Value;
                    sb.AppendLine(string.Format("{0}|{1}|{2}|{3}|{4}",
                        g.GameId, g.EngineTag, g.SpeedPattern, g.StaminaPattern, g.CoordPattern));
                }
                File.WriteAllText(SigDbPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static void RegisterDefaults()
        {
            AddDefault("repo", "UnityMono",
                "F3 0F 10 47 ? 0F 28 C8 F3 0F 59 0D ? ? ? ?",
                "F3 0F 11 47 ? 48 8B 47 ? F3 0F 10 40 ?",
                "F3 0F 10 40 ? F3 0F 11 45 ? F3 0F 10 40 ?");

            AddDefault("lethal", "UnityMono",
                "F3 0F 10 83 ? ? ? ? F3 0F 59 05 ? ? ? ?",
                "F3 0F 11 83 ? ? ? ? 0F 28 74 24 ?",
                "48 8B 83 ? ? ? ? F3 0F 10 40 ? F3 0F 10 48 ?");

            AddDefault("palworld", "Unreal5",
                "48 8B 05 ? ? ? ? 48 3B C6 0F 84 ? ? ? ?",
                "F3 0F 11 43 ? 48 8B 5C 24 ? 48 83 C4 20",
                "0F 10 81 ? ? ? ? 0F 11 02 0F 10 89 ? ? ? ?");

            AddDefault("sotf", "UnityIL2CPP",
                "F3 0F 10 05 ? ? ? ? F3 0F 59 81 ? ? ? ? C3",
                "F3 0F 11 73 ? 48 8B 5C 24 ? C3",
                "F3 0F 10 43 ? F3 0F 10 4B ? F3 0F 10 53 ?");

            AddDefault("phasmophobia", "UnityIL2CPP",
                "F3 0F 10 05 ? ? ? ? F3 0F 59 81 ? ? ? ? C3",
                "F3 0F 11 47 ? 48 85 C0 74 ?",
                "F3 0F 10 40 ? F3 0F 10 48 ? F3 0F 10 50 ?");

            AddDefault("schedule1", "UnityIL2CPP",
                "F3 0F 10 05 ? ? ? ? F3 0F 59 81 ? ? ? ? C3",
                "F3 0F 11 43 ? 48 8B 43 ? 48 85 C0",
                "F3 0F 10 43 ? F3 0F 11 45 ? F3 0F 10 4B ?");

            AddDefault("chained", "Unreal5",
                "48 8B 1D ? ? ? ? 48 85 DB 74 ? 41 B0 01",
                "F3 0F 11 43 ? F3 0F 10 43 ?",
                "0F 10 81 ? ? ? ? 0F 11 02");
        }

        private static void AddDefault(string id, string engine, string speedSig, string stamSig, string coordSig)
        {
            _db[id] = new GameSignatureProfile
            {
                GameId = id,
                EngineTag = engine,
                SpeedPattern = speedSig,
                StaminaPattern = stamSig,
                CoordPattern = coordSig,
                PlayerCoordOffsets = new int[] { 0x18, 0x30, 0x90 }
            };
        }

        public static GameSignatureProfile GetSignatureProfile(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;
            GameSignatureProfile prof;
            if (_db.TryGetValue(gameId, out prof))
                return prof;
            return null;
        }

        public static void BindSignaturesToEngine(TelemetryEngine engine, GameProfile gp)
        {
            if (engine == null || !engine.IsAttached || gp == null) return;

            GameSignatureProfile sig = GetSignatureProfile(gp.Id);
            if (sig == null)
            {
                engine.Log("[SIGDB] '" + gp.Name + "' icin genel motor imzasi kullaniliyor.");
                EngineInspectorService.RunRuntimeSdkScan(engine, gp);
                return;
            }

            engine.Log(string.Format("[SIGDB] '{0}' ({1}) imzalari bellek sayfalarinda araniyor...", gp.Name, sig.EngineTag));

            if (!string.IsNullOrEmpty(sig.SpeedPattern))
                sig.ResolvedSpeedAddr = engine.FindPattern(sig.SpeedPattern);

            if (!string.IsNullOrEmpty(sig.StaminaPattern))
                sig.ResolvedStaminaAddr = engine.FindPattern(sig.StaminaPattern);

            if (!string.IsNullOrEmpty(sig.CoordPattern))
                sig.ResolvedCoordAddr = engine.FindPattern(sig.CoordPattern);

            engine.Log(string.Format("[SIGDB HAZIR] Speed: 0x{0:X} | Resource: 0x{1:X} | Coord: 0x{2:X}",
                sig.ResolvedSpeedAddr.ToInt64(), sig.ResolvedStaminaAddr.ToInt64(), sig.ResolvedCoordAddr.ToInt64()));
        }
    }
}
