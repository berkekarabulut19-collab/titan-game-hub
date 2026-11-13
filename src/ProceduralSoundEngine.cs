using System;
using System.IO;
using System.Media;
using System.Threading;

namespace TitanGameHub
{
    public static class ProceduralSoundEngine
    {
        private static bool _isMuted = false;
        public static bool IsMuted
        {
            get { return _isMuted; }
            set { _isMuted = value; }
        }

        private static byte[] _navHoverWav;
        private static byte[] _navSelectWav;
        private static byte[] _gameLaunchWav;
        private static byte[] _downloadCompleteWav;
        private static byte[] _achievementWav;

        static ProceduralSoundEngine()
        {
            try
            {
                _navHoverWav = GenerateHoverChirp();
                _navSelectWav = GenerateSelectTick();
                _gameLaunchWav = GenerateLaunchSwell();
                _downloadCompleteWav = GenerateDownloadCompleteArpeggio();
                _achievementWav = GenerateAchievementFanfare();
            }
            catch { }
        }

        private static readonly object _playLock = new object();
        private static long _lastPlayTicks = 0;

        public static void PlayHover()
        {
            long now = DateTime.UtcNow.Ticks;
            if ((now - Interlocked.Read(ref _lastPlayTicks)) < TimeSpan.FromMilliseconds(110).Ticks) return;
            PlayMemoryWav(_navHoverWav, true);
        }

        public static void PlaySelect()
        {
            PlayMemoryWav(_navSelectWav, false);
        }

        public static void PlayLaunch()
        {
            PlayMemoryWav(_gameLaunchWav, false);
        }

        public static void PlayDownloadComplete()
        {
            PlayMemoryWav(_downloadCompleteWav, false);
        }

        public static void PlayAchievement()
        {
            PlayMemoryWav(_achievementWav, false);
        }

        private static void PlayMemoryWav(byte[] wav, bool skipIfBusy)
        {
            if (_isMuted || wav == null || wav.Length == 0) return;
            Interlocked.Exchange(ref _lastPlayTicks, DateTime.UtcNow.Ticks);

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool locked = false;
                try
                {
                    locked = skipIfBusy ? Monitor.TryEnter(_playLock) : Monitor.TryEnter(_playLock, 300);
                    if (!locked) return;

                    using (MemoryStream ms = new MemoryStream(wav, false))
                    using (SoundPlayer player = new SoundPlayer(ms))
                    {
                        player.PlaySync();
                    }
                }
                catch { }
                finally
                {
                    if (locked) { try { Monitor.Exit(_playLock); } catch { } }
                }
            });
        }

        private static byte[] BuildWav(short[] samples, int sampleRate)
        {
            int dataSize = samples.Length * 2;
            byte[] wav = new byte[44 + dataSize];
            using (MemoryStream ms = new MemoryStream(wav))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataSize);
                bw.Write(new char[] { 'W', 'A', 'V', 'E' });
                bw.Write(new char[] { 'f', 'm', 't', ' ' });
                bw.Write(16); // subchunk1 size (16 for PCM)
                bw.Write((short)1); // AudioFormat = 1 (PCM)
                bw.Write((short)1); // NumChannels = 1 (Mono)
                bw.Write(sampleRate);
                bw.Write(sampleRate * 2); // ByteRate = SampleRate * NumChannels * BitsPerSample/8
                bw.Write((short)2); // BlockAlign = NumChannels * BitsPerSample/8
                bw.Write((short)16); // BitsPerSample = 16
                bw.Write(new char[] { 'd', 'a', 't', 'a' });
                bw.Write(dataSize);
                for (int i = 0; i < samples.Length; i++)
                {
                    bw.Write(samples[i]);
                }
            }
            return wav;
        }

        // 1. Yumuşak, kulak yormayan hover tıkı (24ms, 520Hz -> 440Hz, fade-in/out)
        private static byte[] GenerateHoverChirp()
        {
            int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * 0.024);
            short[] samples = new short[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double attack = Math.Min(1.0, t / 0.004);
                double freq = 520.0 - (80.0 * (t / 0.024));
                double env = attack * Math.Exp(-t * 150.0);
                double s = Math.Sin(2.0 * Math.PI * freq * t) * env;
                samples[i] = (short)(s * 2400.0);
            }
            return BuildWav(samples, sampleRate);
        }

        // 2. Yumuşak modern seçim sesi (55ms, 523Hz + 659Hz, fade-in/out)
        private static byte[] GenerateSelectTick()
        {
            int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * 0.055);
            short[] samples = new short[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double attack = Math.Min(1.0, t / 0.005);
                double env = attack * Math.Exp(-t * 65.0);
                double s1 = Math.Sin(2.0 * Math.PI * 523.25 * t);
                double s2 = 0.35 * Math.Sin(2.0 * Math.PI * 659.25 * t);
                double combined = (s1 + s2) * env;
                samples[i] = (short)(combined * 4200.0);
            }
            return BuildWav(samples, sampleRate);
        }

        // 3. Triumphant Game Launch Swell (320ms, 220Hz up to 587Hz with warm harmonics)
        private static byte[] GenerateLaunchSwell()
        {
            int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * 0.32); // 320ms
            short[] samples = new short[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double progress = t / 0.32;
                double freq = 220.0 + (367.0 * Math.Pow(progress, 1.8));

                // Envelope: 40ms attack, smooth decay
                double attack = Math.Min(1.0, t / 0.04);
                double decay = Math.Exp(-Math.Max(0.0, t - 0.08) * 9.0);
                double env = attack * decay;

                double s1 = Math.Sin(2.0 * Math.PI * freq * t);
                double s2 = 0.35 * Math.Sin(2.0 * Math.PI * (freq * 2.0) * t);
                double combined = (s1 + s2) * env;
                samples[i] = (short)(combined * 11500.0);
            }
            return BuildWav(samples, sampleRate);
        }

        // 4. Download Complete Ascending Arpeggio (380ms, C5-E5-G5-C6)
        private static byte[] GenerateDownloadCompleteArpeggio()
        {
            int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * 0.38);
            short[] samples = new short[totalSamples];

            double[] notes = new double[] { 523.25, 659.25, 783.99, 1046.50 };
            double noteDuration = 0.095; // 95ms per note

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                int noteIdx = Math.Min(notes.Length - 1, (int)(t / noteDuration));
                double noteTime = t - (noteIdx * noteDuration);
                double freq = notes[noteIdx];

                double attack = Math.Min(1.0, noteTime / 0.008);
                double decay = Math.Exp(-noteTime * 22.0);
                double env = attack * decay;

                double s1 = Math.Sin(2.0 * Math.PI * freq * t);
                double s2 = 0.25 * Math.Sin(2.0 * Math.PI * (freq * 2.0) * t);
                double combined = (s1 + s2) * env;
                samples[i] = (short)(combined * 9500.0);
            }
            return BuildWav(samples, sampleRate);
        }

        // 5. Celebratory Steam Achievement Fanfare (460ms, G-major triad with brass warmth)
        private static byte[] GenerateAchievementFanfare()
        {
            int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * 0.46);
            short[] samples = new short[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double attack = Math.Min(1.0, t / 0.02);
                double decay = Math.Exp(-Math.Max(0.0, t - 0.06) * 6.5);
                double env = attack * decay;

                // G Major Triad: G4 (392Hz), B4 (493.88Hz), D5 (587.33Hz), G5 (783.99Hz)
                double s1 = Math.Sin(2.0 * Math.PI * 392.00 * t);
                double s2 = Math.Sin(2.0 * Math.PI * 493.88 * t);
                double s3 = Math.Sin(2.0 * Math.PI * 587.33 * t);
                double s4 = 0.7 * Math.Sin(2.0 * Math.PI * 783.99 * t);

                double combined = ((s1 + s2 + s3 + s4) / 3.7) * env;
                samples[i] = (short)(combined * 12500.0);
            }
            return BuildWav(samples, sampleRate);
        }
    }
}
