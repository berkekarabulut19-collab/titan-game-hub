using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TitanGameHub
{
    public static class SteamScreenshotService
    {
        public static string LastScreenshotPath { get; private set; }

        public static string CaptureScreenshot(string gameName)
        {
            try
            {
                if (string.IsNullOrEmpty(gameName)) gameName = "SteamCaptures";
                string safeName = Regex.Replace(gameName, @"[^\w\s-]", "").Trim();
                if (string.IsNullOrEmpty(safeName)) safeName = "SteamCaptures";

                string shotDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots", safeName);
                if (!Directory.Exists(shotDir)) Directory.CreateDirectory(shotDir);

                string shotFile = Path.Combine(shotDir, string.Format("SteamShot_{0:yyyyMMdd_HHmmss}.png", DateTime.Now));
                int screenW = Screen.PrimaryScreen.Bounds.Width;
                int screenH = Screen.PrimaryScreen.Bounds.Height;

                using (Bitmap bmp = new Bitmap(screenW, screenH))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(0, 0, 0, 0, new Size(screenW, screenH));
                    }
                    bmp.Save(shotFile, ImageFormat.Png);
                }

                LastScreenshotPath = shotFile;
                Win32.PlayTechChirp(true);
                return shotFile;
            }
            catch
            {
                return null;
            }
        }
    }
}
