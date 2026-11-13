using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    /// <summary>
    /// Telegram / Webhook Mobile Notification Dispatcher (C# 5.0 Strict):
    /// Sends real-time mobile push alerts when a squad lobby is created
    /// or when a game finishes background download.
    /// </summary>
    public static class TelegramLobbyWebhookService
    {
        private static string _savedWebhookUrl = null;

        public static string WebhookUrl
        {
            get { return _savedWebhookUrl; }
            set { _savedWebhookUrl = value; }
        }

        public static void SendLobbyInviteAlertAsync(string gameName, string hostNick, string roomCode, string customWebhook = null)
        {
            string url = string.IsNullOrEmpty(customWebhook) ? _savedWebhookUrl : customWebhook;
            if (string.IsNullOrEmpty(url))
            {
                // Default global topic broadcast
                url = "https://ntfy.sh/titan_squad_lobby_alerts";
            }

            string payload = string.Format("🎮 {0} Lobi Açtı! Oyun: {1} • Oda Kodu: {2} (Titan Game Hub Spacewar 480)",
                hostNick ?? "Bir Arkadaşın", gameName, roomCode ?? "TITAN-TR");

            PostNotificationAsync(url, payload, "Yeni Oyun Lobisi");
        }

        public static void SendDownloadFinishedAlertAsync(string gameName, string customWebhook = null)
        {
            string url = string.IsNullOrEmpty(customWebhook) ? _savedWebhookUrl : customWebhook;
            if (string.IsNullOrEmpty(url)) url = "https://ntfy.sh/titan_squad_lobby_alerts";

            string payload = string.Format("✅ {0} indirmesi tamamlandı ve oynanmaya hazır!", gameName);
            PostNotificationAsync(url, payload, "İndirme Tamamlandı");
        }

        private static void PostNotificationAsync(string endpointUrl, string message, string title)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(endpointUrl);
                    req.Method = "POST";
                    req.ContentType = "text/plain; charset=utf-8";
                    req.Headers.Add("Title", title);
                    req.Headers.Add("Priority", "high");
                    req.ContentLength = data.Length;
                    req.Timeout = 5000;

                    using (Stream s = req.GetRequestStream())
                    {
                        s.Write(data, 0, data.Length);
                    }

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        // Successfully dispatched
                    }
                }
                catch { }
            });
        }
    }
}
