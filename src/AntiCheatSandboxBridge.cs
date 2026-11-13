using System;

namespace TitanGameHub
{
    /// <summary>
    /// Anti-cheat compatibility notice.
    ///
    /// Previous builds opened a named pipe called "EasyAntiCheat" and answered heartbeat
    /// packets with a forged "clean" status. That cannot make a kernel anti-cheat trust the
    /// process, and impersonating an AC service only adds a detection surface (and may be
    /// flagged by the AC vendor). The spoof is therefore removed.
    ///
    /// The API is kept intact so the launch pipeline and the UI need no changes.
    /// Co-op sessions must run the official, unmodified anti-cheat.
    /// </summary>
    public static class AntiCheatSandboxBridge
    {
        public static bool IsRunning { get { return false; } }

        public static void StartAntiCheatSandbox(GameProfile profile)
        {
            // Intentionally a no-op: no AC service impersonation, no forged integrity replies.
        }

        public static void StopAntiCheatSandbox()
        {
            // Intentionally a no-op.
        }

        public static string GetCompatibilityNotice()
        {
            return "Anti-cheat köprüsü devre dışı: anti-cheat servisi taklit edilmiyor. " +
                   "Çok oyunculu oturumlar resmi anti-cheat ile çalıştırılmalıdır.";
        }
    }
}
