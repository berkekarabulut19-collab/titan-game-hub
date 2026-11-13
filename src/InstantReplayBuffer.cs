using System;
using System.Collections.Generic;
using System.IO;

namespace TitanGameHub
{
    public class ReplayFrame
    {
        public DateTime Timestamp { get; set; }
        public byte[] FrameBytes { get; set; }
    }

    /// <summary>
    /// Instant Replay (Shadowplay) In-Memory Rolling Buffer (C# 5.0 Strict):
    /// Retains the last 30 seconds of gameplay screen captures in RAM.
    /// Flushes to MP4 / Motion JPEG clip on hotkey press without writing to SSD.
    /// </summary>
    public static class InstantReplayBuffer
    {
        private const int MaxBufferSeconds = 30;
        private static readonly Queue<ReplayFrame> _circularQueue = new Queue<ReplayFrame>();
        private static readonly object _syncLock = new object();
        private static volatile bool _isRecording = false;

        public static bool IsRecording { get { return _isRecording; } }

        public static void StartRecording()
        {
            _isRecording = true;
        }

        public static void StopRecording()
        {
            _isRecording = false;
            lock (_syncLock)
            {
                _circularQueue.Clear();
            }
        }

        public static void PushFrame(byte[] jpegBytes)
        {
            if (!_isRecording || jpegBytes == null) return;

            DateTime now = DateTime.Now;
            DateTime cutoff = now.AddSeconds(-MaxBufferSeconds);

            lock (_syncLock)
            {
                _circularQueue.Enqueue(new ReplayFrame { Timestamp = now, FrameBytes = jpegBytes });
                while (_circularQueue.Count > 0 && _circularQueue.Peek().Timestamp < cutoff)
                {
                    _circularQueue.Dequeue();
                }
            }
        }

        public static bool SaveReplayClip(string gameName, out string savedFilePath, out string statusMsg)
        {
            savedFilePath = null;
            statusMsg = "";

            try
            {
                string replayDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Replays");
                if (!Directory.Exists(replayDir)) Directory.CreateDirectory(replayDir);

                string safeName = string.IsNullOrEmpty(gameName) ? "Clip" : gameName.Replace(" ", "_");
                string file = Path.Combine(replayDir, string.Format("{0}_{1:yyyyMMdd_HHmmss}.mjpg", safeName, DateTime.Now));

                List<ReplayFrame> framesToSave;
                lock (_syncLock)
                {
                    framesToSave = new List<ReplayFrame>(_circularQueue);
                }

                if (framesToSave.Count == 0)
                {
                    statusMsg = "Tampon bellekte henüz kaydedilmiş kare yok.";
                    return false;
                }

                using (FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write))
                {
                    foreach (ReplayFrame f in framesToSave)
                    {
                        fs.Write(f.FrameBytes, 0, f.FrameBytes.Length);
                    }
                }

                savedFilePath = file;
                statusMsg = string.Format("🎬 Son {0} saniyelik klip kaydedildi: {1}", MaxBufferSeconds, Path.GetFileName(file));
                return true;
            }
            catch (Exception ex)
            {
                statusMsg = "Klip kaydetme hatası: " + ex.Message;
                return false;
            }
        }
    }
}
