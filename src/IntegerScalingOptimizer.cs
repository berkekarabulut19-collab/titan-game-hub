using System;
using System.Drawing;
using System.Windows.Forms;

namespace TitanGameHub
{
    public class IntegerScalingInfo
    {
        public int MonitorWidth { get; set; }
        public int MonitorHeight { get; set; }
        public int GameWidth { get; set; }
        public int GameHeight { get; set; }
        public int ScaleFactor { get; set; }
        public bool IsExactIntegerMatch { get; set; }
        public string DiagnosticMessage { get; set; }
    }

    public static class IntegerScalingOptimizer
    {
        public static IntegerScalingInfo CalculateIntegerScale(int gameWidth, int gameHeight)
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            int monW = bounds.Width;
            int monH = bounds.Height;

            int gW = (gameWidth > 0) ? gameWidth : 1280;
            int gH = (gameHeight > 0) ? gameHeight : 720;

            int scaleX = monW / gW;
            int scaleY = monH / gH;
            int factor = Math.Max(1, Math.Min(scaleX, scaleY));

            bool exactMatch = (monW % gW == 0) && (monH % gH == 0);

            string msg = exactMatch
                ? string.Format("Kusursuz {0}x Tamsayı Ölçekleme (Integer Scaling). Pikseller %100 keskin, sıfır bulanıklık.", factor)
                : string.Format("{0}x Ölçekleme (Pencere: {1}x{2} -> Monitör: {3}x{4}). Siyah çubuklarla (Letterbox) pikseller net kalır.",
                    factor, gW * factor, gH * factor, monW, monH);

            return new IntegerScalingInfo
            {
                MonitorWidth = monW,
                MonitorHeight = monH,
                GameWidth = gW,
                GameHeight = gH,
                ScaleFactor = factor,
                IsExactIntegerMatch = exactMatch,
                DiagnosticMessage = msg
            };
        }
    }
}
