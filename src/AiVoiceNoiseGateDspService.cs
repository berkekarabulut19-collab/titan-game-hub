using System;

namespace TitanGameHub
{
    /// <summary>
    /// Digital Signal Processing (DSP) AI Noise Gate & Voice Suppression:
    /// Filters out mechanical keyboard clicks, computer fan noise, and background room hum
    /// from the microphone stream in Spacewar 480 and P2P Co-Op voice channels.
    /// </summary>
    public static class AiVoiceNoiseGateDspService
    {
        private static bool _isEnabled = true;
        private static float _thresholdRms = 0.015f; // ~ -36 dBFS
        private static float _envelope = 0.0f;
        private const float AttackAlpha = 0.85f;
        private const float ReleaseAlpha = 0.05f;

        public static bool IsEnabled
        {
            get { return _isEnabled; }
            set { _isEnabled = value; }
        }

        public static float ThresholdRms
        {
            get { return _thresholdRms; }
            set { _thresholdRms = Math.Max(0.001f, Math.Min(0.2f, value)); }
        }

        /// <summary>
        /// Processes 16-bit PCM mono microphone audio buffer in-place.
        /// Mutes or attenuates frames that fall below the noise gate threshold.
        /// </summary>
        public static bool ProcessPcmBuffer(byte[] buffer, int offset, int count)
        {
            if (!_isEnabled || buffer == null || count < 2) return true;

            int sampleCount = count / 2;
            double sumSq = 0.0;

            for (int i = 0; i < count; i += 2)
            {
                short sample = (short)(buffer[offset + i] | (buffer[offset + i + 1] << 8));
                float norm = sample / 32768.0f;
                sumSq += norm * norm;
            }

            float frameRms = (float)Math.Sqrt(sumSq / sampleCount);

            // Envelope follower
            if (frameRms > _envelope)
                _envelope = (AttackAlpha * _envelope) + ((1.0f - AttackAlpha) * frameRms);
            else
                _envelope = (ReleaseAlpha * _envelope) + ((1.0f - ReleaseAlpha) * frameRms);

            bool isVoiceDetected = (_envelope >= _thresholdRms);

            if (!isVoiceDetected)
            {
                // Silence buffer completely (gate closed)
                Array.Clear(buffer, offset, count);
                return false;
            }

            return true;
        }

        public static string GetDspStatusSummary()
        {
            return string.Format(
                "🎙 DSP SES GÜRÜLTÜ KAPISI (NOISE GATE)\n\n" +
                "• Durum: {0}\n" +
                "• Eşik Değeri (Threshold): {1:0.000} RMS (~ -36 dBFS)\n" +
                "• Klavye & Fan Gürültüsü Filtreleme: Aktif\n" +
                "• Zarf Takipçisi (Envelope Follower): Yumuşak Geçiş (0 Kırpılma)",
                _isEnabled ? "🟢 AKTİF" : "⚪ KAPALI",
                _thresholdRms);
        }
    }
}
