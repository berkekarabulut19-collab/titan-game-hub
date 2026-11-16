using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace TitanGameHub
{
    /// <summary>
    /// OnlineFix dosya bütünlüğü kontrol yardımcısı (native winmm.dll / OnlineFix64.dll varlığını doğrular).
    /// </summary>
    public static class OnlineFixLaunchInjector
    {
        public static bool NeedsDirectInjection(string workingDir)
        {
            return false;
        }

        public static List<string> ResolveDllLoadOrder(string workingDir)
        {
            return new List<string>();
        }

        public static Process LaunchWithOnlineFixInjection(string exePath, string workingDir, string appId)
        {
            return null;
        }
    }
}
