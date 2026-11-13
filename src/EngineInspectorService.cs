using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TitanGameHub
{
    public enum GameEngineType
    {
        Unknown,
        UnityMono,
        UnityIL2CPP,
        UnrealEngine4,
        UnrealEngine5,
        SourceEngine,
        NativeCustom
    }

    public class EngineAnalysisResult
    {
        public GameEngineType EngineType { get; set; }
        public string EngineDisplayName { get; set; }
        public string CoreModuleName { get; set; }
        public bool SupportsBepInEx { get; set; }
        public string Architecture { get; set; }
        public IntPtr ResolvedGWorld { get; set; }
        public IntPtr ResolvedMonoDomain { get; set; }
    }

    public static class EngineInspectorService
    {
        // Standard Unreal Engine 4/5 GWorld & LocalPlayer AOB Signatures
        public const string UE4_GWORLD_SIG = "48 8B 1D ? ? ? ? 48 85 DB 74 ? 41 B0 01";
        public const string UE5_GWORLD_SIG = "48 8B 05 ? ? ? ? 48 3B C6 0F 84 ? ? ? ?";
        public const string UNITY_TIMESCALE_SIG = "F3 0F 10 05 ? ? ? ? F3 0F 59 81 ? ? ? ? C3";

        public static EngineAnalysisResult InspectGameDirectory(GameProfile gp)
        {
            EngineAnalysisResult res = new EngineAnalysisResult
            {
                EngineType = GameEngineType.Unknown,
                EngineDisplayName = "Native x64 Engine",
                CoreModuleName = "",
                SupportsBepInEx = false,
                Architecture = "x64"
            };

            if (gp == null || string.IsNullOrEmpty(gp.WorkingDirectory) || !Directory.Exists(gp.WorkingDirectory))
                return res;

            try
            {
                string root = gp.WorkingDirectory;
                string[] allFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories);

                bool hasGameAssembly = false;
                bool hasMonoBdwgc = false;
                bool hasUnityPlayer = false;
                bool hasUnrealShipping = false;
                bool hasSourceEngine = false;

                foreach (string f in allFiles)
                {
                    string fn = (Path.GetFileName(f) ?? "").ToLowerInvariant();
                    if (fn == "gameassembly.dll") hasGameAssembly = true;
                    else if (fn == "mono-2.0-bdwgc.dll" || fn == "mono.dll") hasMonoBdwgc = true;
                    else if (fn == "unityplayer.dll") hasUnityPlayer = true;
                    else if (fn.EndsWith("-win64-shipping.exe")) hasUnrealShipping = true;
                    else if (fn == "engine.dll" && f.ToLowerInvariant().Contains("\\bin\\")) hasSourceEngine = true;
                }

                if (hasGameAssembly)
                {
                    res.EngineType = GameEngineType.UnityIL2CPP;
                    res.EngineDisplayName = "Unity Engine (IL2CPP Native)";
                    res.CoreModuleName = "GameAssembly.dll";
                    res.SupportsBepInEx = true;
                }
                else if (hasMonoBdwgc || hasUnityPlayer)
                {
                    res.EngineType = GameEngineType.UnityMono;
                    res.EngineDisplayName = "Unity Engine (Mono JIT)";
                    res.CoreModuleName = "mono-2.0-bdwgc.dll";
                    res.SupportsBepInEx = true;
                }
                else if (hasUnrealShipping || Directory.Exists(Path.Combine(root, "Engine")))
                {
                    res.EngineType = GameEngineType.UnrealEngine5;
                    res.EngineDisplayName = "Unreal Engine 4/5 (Shipping)";
                    res.CoreModuleName = Path.GetFileName(gp.ExecutablePath ?? "");
                    res.SupportsBepInEx = false;
                }
                else if (hasSourceEngine)
                {
                    res.EngineType = GameEngineType.SourceEngine;
                    res.EngineDisplayName = "Valve Source Engine";
                    res.CoreModuleName = "client.dll";
                    res.SupportsBepInEx = false;
                }
                else
                {
                    res.EngineType = GameEngineType.NativeCustom;
                    res.EngineDisplayName = "Native C++ / DirectX Engine";
                    res.CoreModuleName = Path.GetFileName(gp.ExecutablePath ?? "");
                }
            }
            catch { }

            return res;
        }

        public static IntPtr ResolveRelativeRipAddress(TelemetryEngine telemetry, IntPtr instructionAddr, int offsetIndex, int instructionLength)
        {
            if (telemetry == null || !telemetry.IsAttached || instructionAddr == IntPtr.Zero)
                return IntPtr.Zero;

            try
            {
                int relOffset = telemetry.ReadInt32((IntPtr)(instructionAddr.ToInt64() + offsetIndex));
                if (relOffset == 0) return IntPtr.Zero;
                return (IntPtr)(instructionAddr.ToInt64() + instructionLength + relOffset);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public static void RunRuntimeSdkScan(TelemetryEngine telemetry, GameProfile gp)
        {
            if (telemetry == null || !telemetry.IsAttached) return;

            EngineAnalysisResult info = InspectGameDirectory(gp);
            telemetry.Log(string.Format("[SDK ANALIZ] Motor Tespiti: {0} (Cekirdek: {1})",
                info.EngineDisplayName, string.IsNullOrEmpty(info.CoreModuleName) ? "MainModule" : info.CoreModuleName));

            if (info.EngineType == GameEngineType.UnrealEngine4 || info.EngineType == GameEngineType.UnrealEngine5)
            {
                IntPtr gworldInsn = telemetry.FindPattern(UE4_GWORLD_SIG);
                if (gworldInsn == IntPtr.Zero)
                    gworldInsn = telemetry.FindPattern(UE5_GWORLD_SIG);

                if (gworldInsn != IntPtr.Zero)
                {
                    IntPtr gworldPtr = ResolveRelativeRipAddress(telemetry, gworldInsn, 3, 7);
                    telemetry.Log(string.Format("[UNREAL SDK] GWorld RIP Adresi Cozumlendi: 0x{0:X}", gworldPtr.ToInt64()));
                }
            }
            else if (info.EngineType == GameEngineType.UnityMono || info.EngineType == GameEngineType.UnityIL2CPP)
            {
                IntPtr timeInsn = telemetry.FindPattern(UNITY_TIMESCALE_SIG);
                if (timeInsn != IntPtr.Zero)
                {
                    IntPtr timeScalePtr = ResolveRelativeRipAddress(telemetry, timeInsn, 4, 8);
                    telemetry.Log(string.Format("[UNITY SDK] TimeScale Vektor Adresi Cozumlendi: 0x{0:X}", timeScalePtr.ToInt64()));
                }
            }
        }
    }
}
