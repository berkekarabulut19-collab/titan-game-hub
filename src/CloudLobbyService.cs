using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TitanGameHub
{
    public class FriendPresence
    {
        public string UserName { get; set; }
        public string FriendCode { get; set; }
        public string ActiveGame { get; set; }
        public string Status { get; set; }
        public string GameVersion { get; set; }
        public string AppVersion { get; set; }
        public string InviteUrl { get; set; }
        public string RoomCode { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsOnline { get; set; }
        public bool IsSavedFriend { get; set; }
        public string LastKnownIp { get; set; }
    }

    public class SavedFriend
    {
        public string Nick { get; set; }
        public string FriendCode { get; set; }
        public DateTime AddedAt { get; set; }
        public string Status { get; set; } // "Aktif", "İstek Gönderildi", "Beklemede"
    }

    /// <summary>
    /// Online arkadaş, presence ve davet servisi (ntfy tabanlı hafif senkronizasyon).
    /// </summary>
    public class CloudLobbyService : IDisposable
    {
        private const string BaseUrl = "https://ntfy.sh/";
        private const string TopicPrefix = "titan_v5_lobby_";
        private const int HeartbeatSeconds = 30;
        private const int PollIntervalMs = 3500;
        private const int FriendMailboxPollSeconds = 20;
        private const int PresenceTtlMinutes = 3;
        private const int RequestTimeoutMs = 6000;
        private const int MaxProcessedIds = 2500;

        public string UserName { get; set; }
        public string RoomCode { get; set; }
        public string MyFriendCode { get; set; }
        public string CurrentGame { get; set; }
        public string CurrentGameVersion { get; set; }
        public string CurrentAppVersion { get; set; }
        public string CurrentStatus { get; set; }
        public bool IsHost { get; set; }
        public bool IsRunning { get { return _running; } }
        public DateTime LastHeartbeatUtc { get; private set; }

        public event Action<List<FriendPresence>> OnFriendsUpdated;
        public event Action<string, string> OnPeerAppVersionDifferent; // peerNick, peerVersion
        public event Action<string, string> OnChatMessageReceived;
        public event Action<string, string, string> OnInviteReceived;
        public event Action<string, string, string, string> OnFriendRequestReceived; // fromNick, fromCode, game, room
        public event Action<string, string> OnFriendAccepted; // fromNick, fromCode
        public event Action<string, string, string, string, string, string> OnDirectGameInvite; // fromNick, fromCode, game, room, hostIp, virtIp

        private Thread _pollThread;
        private volatile bool _running;
        private volatile bool _forcePollRequested;
        private readonly object _processedLock = new object();
        private readonly HashSet<string> _processedMsgIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _processedOrder = new Queue<string>();
        private readonly object _savedFriendsLock = new object();
        private readonly List<SavedFriend> _savedFriends = new List<SavedFriend>();
        private readonly object _livePresenceLock = new object();
        private readonly Dictionary<string, FriendPresence> _livePresence = new Dictionary<string, FriendPresence>(StringComparer.OrdinalIgnoreCase);
        private string _lastPublishedSignature = string.Empty;
        private string _lastPeerAppVersionNoticeKey = string.Empty;
        private DateTime _lastPeerAppVersionNoticeAt = DateTime.MinValue;

        private static readonly string FriendsConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "data", "friends.cfg");
        private static readonly string IdentityConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "data", "identity.cfg");

        public CloudLobbyService()
        {
            LoadIdentity();
            UserName = string.IsNullOrWhiteSpace(UserName) ? Environment.UserName : UserName.Trim();
            RoomCode = "TITAN-TR";
            CurrentGame = "Titan Hub";
            CurrentStatus = "Çevrimiçi";
            CurrentAppVersion = AutoUpdateService.CurrentAppVersion;
            IsHost = true;
            LoadSavedFriends();
        }

        private void LoadIdentity()
        {
            try
            {
                if (File.Exists(IdentityConfigPath))
                {
                    string[] lines = File.ReadAllLines(IdentityConfigPath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();
                        if (key.Equals("FRIEND_CODE", StringComparison.OrdinalIgnoreCase)) MyFriendCode = val;
                        else if (key.Equals("NICK", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val)) UserName = val;
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(MyFriendCode))
            {
                MyFriendCode = GenerateFriendCode();
                SaveIdentity();
            }
        }

        private static string GenerateFriendCode()
        {
            Random rnd = new Random(Guid.NewGuid().GetHashCode());
            const string chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
            char[] codeChars = new char[6];
            for (int i = 0; i < 6; i++) codeChars[i] = chars[rnd.Next(chars.Length)];
            return "TITAN-" + new string(codeChars);
        }

        public void SaveIdentity()
        {
            try
            {
                string dir = Path.GetDirectoryName(IdentityConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Titan Game Hub Identity Configuration");
                sb.AppendLine("FRIEND_CODE=" + (MyFriendCode ?? ""));
                sb.AppendLine("NICK=" + (UserName ?? Environment.UserName));
                string tmpPath = IdentityConfigPath + ".tmp";
                File.WriteAllText(tmpPath, sb.ToString(), Encoding.UTF8);
                if (File.Exists(IdentityConfigPath)) File.Delete(IdentityConfigPath);
                File.Move(tmpPath, IdentityConfigPath);
            }
            catch { }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _forcePollRequested = true;
            _pollThread = new Thread(WorkerLoop) { IsBackground = true, Name = "TitanCloudLobbyPoll" };
            _pollThread.Start();
        }

        public void Stop()
        {
            _running = false;
        }

        private void LoadSavedFriends()
        {
            lock (_savedFriendsLock)
            {
                _savedFriends.Clear();
                try
                {
                    if (File.Exists(FriendsConfigPath))
                    {
                        string[] lines = File.ReadAllLines(FriendsConfigPath, Encoding.UTF8);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i];
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                            string[] parts = line.Split('|');
                            if (parts.Length >= 1)
                            {
                                SavedFriend sf = new SavedFriend();
                                sf.Nick = parts[0].Trim();
                                sf.FriendCode = parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : sf.Nick;
                                sf.Status = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : "Aktif";
                                sf.AddedAt = DateTime.Now;
                                if (!string.IsNullOrWhiteSpace(sf.Nick)) _savedFriends.Add(sf);
                            }
                        }
                    }
                }
                catch { }

                EnsureDefaultFriend();
            }
        }

        private void EnsureDefaultFriend()
        {
            bool hasBerke = false;
            for (int i = 0; i < _savedFriends.Count; i++)
            {
                if (string.Equals(_savedFriends[i].Nick, "Berke", StringComparison.OrdinalIgnoreCase))
                {
                    hasBerke = true;
                    break;
                }
            }
            if (!hasBerke)
            {
                _savedFriends.Add(new SavedFriend
                {
                    Nick = "Berke",
                    FriendCode = "TITAN-BERKE",
                    AddedAt = DateTime.Now,
                    Status = "Aktif"
                });
            }
        }

        public void SaveFriends()
        {
            lock (_savedFriendsLock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(FriendsConfigPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("# Titan Hub - Saved Friends");
                    for (int i = 0; i < _savedFriends.Count; i++)
                    {
                        SavedFriend f = _savedFriends[i];
                        sb.AppendLine((f.Nick ?? "") + "|" + (f.FriendCode ?? f.Nick ?? "") + "|" + (f.Status ?? "Aktif"));
                    }
                    File.WriteAllText(FriendsConfigPath, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        public void ClearFriends()
        {
            lock (_savedFriendsLock)
            {
                _savedFriends.Clear();
                EnsureDefaultFriend();
            }
            SaveFriends();
            NotifyFriendsUpdated(true);
        }

        public bool AddFriend(string nicknameOrCode, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(nicknameOrCode))
            {
                message = "Arkadaş adı veya Titan kodu boş olamaz.";
                return false;
            }

            string clean = nicknameOrCode.Trim();
            bool looksLikeCode = clean.StartsWith("TITAN-", StringComparison.OrdinalIgnoreCase);

            lock (_savedFriendsLock)
            {
                if (string.Equals(clean, UserName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, MyFriendCode, StringComparison.OrdinalIgnoreCase))
                {
                    message = "Kendinizi arkadaş olarak ekleyemezsiniz.";
                    return false;
                }

                for (int i = 0; i < _savedFriends.Count; i++)
                {
                    SavedFriend f = _savedFriends[i];
                    if (string.Equals(f.Nick, clean, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.FriendCode, clean, StringComparison.OrdinalIgnoreCase))
                    {
                        message = clean + " zaten arkadaş listenizde kayıtlı.";
                        return false;
                    }
                }

                _savedFriends.Add(new SavedFriend
                {
                    Nick = looksLikeCode ? clean : clean,
                    FriendCode = clean,
                    AddedAt = DateTime.Now,
                    Status = "İstek Gönderildi"
                });
            }
            SaveFriends();

            SendRemoteFriendRequestPacket(clean);
            message = "➕ '" + clean + "' hedefine arkadaşlık isteği gönderildi. Karşı taraf kabul edince eşleşme tamamlanır.";
            RequestImmediatePoll();
            NotifyFriendsUpdated(true);
            return true;
        }

        public void AcceptRemoteFriendRequest(string fromNick, string fromCode)
        {
            if (string.IsNullOrWhiteSpace(fromNick) && string.IsNullOrWhiteSpace(fromCode)) return;
            string cleanNick = (fromNick ?? "").Trim();
            string cleanCode = string.IsNullOrWhiteSpace(fromCode) ? cleanNick : fromCode.Trim();
            if (string.IsNullOrEmpty(cleanNick)) cleanNick = cleanCode;

            bool shouldAck = false;
            lock (_savedFriendsLock)
            {
                SavedFriend existing = null;
                for (int i = 0; i < _savedFriends.Count; i++)
                {
                    SavedFriend f = _savedFriends[i];
                    if (string.Equals(f.Nick, cleanNick, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.FriendCode, cleanCode, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(cleanNick) && string.Equals(f.FriendCode, cleanNick, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing = f;
                        break;
                    }
                }

                if (existing != null)
                {
                    if (!string.Equals(existing.Status, "Aktif", StringComparison.OrdinalIgnoreCase)) shouldAck = true;
                    existing.Nick = cleanNick;
                    existing.FriendCode = cleanCode;
                    existing.Status = "Aktif";
                }
                else
                {
                    _savedFriends.Add(new SavedFriend
                    {
                        Nick = cleanNick,
                        FriendCode = cleanCode,
                        AddedAt = DateTime.Now,
                        Status = "Aktif"
                    });
                    shouldAck = true;
                }
            }
            SaveFriends();

            if (shouldAck || !string.IsNullOrWhiteSpace(cleanCode) || !string.IsNullOrWhiteSpace(cleanNick))
                SendRemoteFriendAcceptPacket(!string.IsNullOrWhiteSpace(cleanCode) ? cleanCode : cleanNick);

            RequestImmediatePoll();
            NotifyFriendsUpdated(true);
        }

        public bool RemoveFriend(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname)) return false;
            string clean = nickname.Trim();
            bool removed = false;
            lock (_savedFriendsLock)
            {
                removed = _savedFriends.RemoveAll(f =>
                    string.Equals(f.Nick, clean, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.FriendCode, clean, StringComparison.OrdinalIgnoreCase)) > 0;
            }
            if (removed)
            {
                SaveFriends();
                NotifyFriendsUpdated(true);
            }
            return removed;
        }

        public void RequestImmediatePoll()
        {
            _forcePollRequested = true;
        }

        public void PollRoom()
        {
            if (!_running)
            {
                // tek seferlik hızlı deneme
                ThreadPool.QueueUserWorkItem(delegate { PollRoomOnceSafe(); NotifyFriendsUpdated(false); });
                return;
            }
            RequestImmediatePoll();
        }

        public void SendChatMessage(string message)
        {
            SendPacket(new Dictionary<string, string>
            {
                { "type", "chat" },
                { "user", UserName },
                { "msg", message ?? "" }
            });
        }

        public void SendLobbyInvite(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    Dictionary<string, string> payload = new Dictionary<string, string>();
                    payload["type"] = "invite";
                    payload["user"] = UserName ?? "Titan";
                    payload["code"] = MyFriendCode ?? "";
                    payload["game"] = gameName;
                    payload["room"] = RoomCode ?? "TITAN-TR";
                    payload["invite"] = "steam://run/480//";
                    PostJson(GetRoomTopicUrl(), ser.Serialize(payload));
                }
                catch { }
            });
        }

        private static string _cachedPublicIp = string.Empty;
        private static DateTime _cachedPublicIpAt = DateTime.MinValue;
        private static readonly object _publicIpLock = new object();

        private static string GetPublicIpCached()
        {
            try
            {
                lock (_publicIpLock)
                {
                    if (!string.IsNullOrEmpty(_cachedPublicIp) && (DateTime.UtcNow - _cachedPublicIpAt).TotalMinutes < 10)
                        return _cachedPublicIp;
                }

                string ip = string.Empty;
                try { ip = (HttpGet("https://api.ipify.org", 4000) ?? "").Trim(); } catch { ip = string.Empty; }

                lock (_publicIpLock)
                {
                    if (!string.IsNullOrEmpty(ip))
                    {
                        _cachedPublicIp = ip;
                        _cachedPublicIpAt = DateTime.UtcNow;
                    }
                    return _cachedPublicIp ?? string.Empty;
                }
            }
            catch { return string.Empty; }
        }

        public void SendDirectInvite(string targetFriendCodeOrNick, string gameName)
        {
            if (string.IsNullOrWhiteSpace(targetFriendCodeOrNick)) return;
            string targetTopic = GetUserTopicUrl(targetFriendCodeOrNick);
            string gName = string.IsNullOrWhiteSpace(gameName) ? (CurrentGame ?? "Titan Hub") : gameName;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string hostIp = GetPublicIpCached();
                    string virtIp = string.Empty;

                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    Dictionary<string, string> payload = new Dictionary<string, string>();
                    payload["type"] = "direct_invite";
                    payload["from_nick"] = UserName ?? "Titan";
                    payload["from_code"] = MyFriendCode ?? "";
                    payload["game"] = gName;
                    payload["room"] = RoomCode ?? "TITAN-TR";
                    payload["host_ip"] = hostIp ?? "";
                    payload["virt_ip"] = virtIp ?? "";
                    PostJson(targetTopic, ser.Serialize(payload));
                }
                catch { }
            });
        }

        private void SendRemoteFriendRequestPacket(string targetCodeOrNick)
        {
            string targetTopic = GetUserTopicUrl(targetCodeOrNick);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    Dictionary<string, string> payload = new Dictionary<string, string>();
                    payload["type"] = "friend_req";
                    payload["from_nick"] = UserName ?? Environment.UserName;
                    payload["from_code"] = MyFriendCode ?? "";
                    payload["game"] = CurrentGame ?? "Titan Hub";
                    payload["room"] = RoomCode ?? "TITAN-TR";
                    PostJson(targetTopic, ser.Serialize(payload));
                }
                catch { }
            });
        }

        private void SendRemoteFriendAcceptPacket(string targetCodeOrNick)
        {
            if (string.IsNullOrWhiteSpace(targetCodeOrNick)) return;
            string targetTopic = GetUserTopicUrl(targetCodeOrNick);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    Dictionary<string, string> payload = new Dictionary<string, string>();
                    payload["type"] = "friend_ack";
                    payload["from_nick"] = UserName ?? Environment.UserName;
                    payload["from_code"] = MyFriendCode ?? "";
                    payload["game"] = CurrentGame ?? "Titan Hub";
                    payload["room"] = RoomCode ?? "TITAN-TR";
                    PostJson(targetTopic, ser.Serialize(payload));
                }
                catch { }
            });
        }

        private void WorkerLoop()
        {
            DateTime lastHeartbeat = DateTime.MinValue;
            DateTime lastFriendPresencePoll = DateTime.MinValue;

            while (_running)
            {
                bool doImmediate = false;
                try
                {
                    doImmediate = _forcePollRequested;
                    _forcePollRequested = false;

                    if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= HeartbeatSeconds)
                    {
                        SendHeartbeatInternal();
                        lastHeartbeat = DateTime.UtcNow;
                    }

                    PollRoomOnceSafe();

                    if ((DateTime.UtcNow - lastFriendPresencePoll).TotalSeconds >= FriendMailboxPollSeconds || doImmediate)
                    {
                        PollFriendMailboxesSafe();
                        lastFriendPresencePoll = DateTime.UtcNow;
                    }

                    NotifyFriendsUpdated(false);
                }
                catch { }

                SleepInterruptible(doImmediate ? 400 : PollIntervalMs);
            }
        }

        private void SleepInterruptible(int ms)
        {
            int waited = 0;
            while (_running && waited < ms)
            {
                if (_forcePollRequested) break;
                Thread.Sleep(150);
                waited += 150;
            }
        }

        public void SendHeartbeat()
        {
            ThreadPool.QueueUserWorkItem(delegate { SendHeartbeatInternal(); });
        }

        private void SendHeartbeatInternal()
        {
            try
            {
                string inviteUrl = "steam://run/480//+connect_lobby " + (RoomCode ?? "TITAN-TR");
                SendPacket(new Dictionary<string, string>
                {
                    { "type", "heartbeat" },
                    { "user", UserName ?? Environment.UserName },
                    { "code", MyFriendCode ?? "" },
                    { "game", CurrentGame ?? "Titan Hub" },
                    { "status", CurrentStatus ?? "Çevrimiçi" },
                    { "gamever", CurrentGameVersion ?? "" },
                    { "appver", CurrentAppVersion ?? AutoUpdateService.CurrentAppVersion ?? "" },
                    { "invite", inviteUrl },
                    { "room", RoomCode ?? "TITAN-TR" },
                    { "stun", ZeroVpnTunnelService.GetBestShareableEndpoint() ?? "" }
                });
                LastHeartbeatUtc = DateTime.UtcNow;
            }
            catch { }
        }

        private void SendPacket(Dictionary<string, string> data)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    PostJson(GetRoomTopicUrl(), ser.Serialize(data));
                }
                catch { }
            });
        }

        private void PollRoomOnceSafe()
        {
            try
            {
                ProcessInboxMessages(GetInboxMessages(GetRoomTopicUrl(true)), false);
                ProcessInboxMessages(GetInboxMessages(GetUserTopicUrl(MyFriendCode)), true);
            }
            catch { }
        }

        private void PollFriendMailboxesSafe()
        {
            List<string> targets = new List<string>();
            lock (_savedFriendsLock)
            {
                for (int i = 0; i < _savedFriends.Count; i++)
                {
                    SavedFriend sf = _savedFriends[i];
                    if (sf == null) continue;
                    if (string.Equals(sf.Nick, UserName, StringComparison.OrdinalIgnoreCase)) continue;
                    string t = !string.IsNullOrWhiteSpace(sf.FriendCode) ? sf.FriendCode : sf.Nick;
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    if (!targets.Contains(t)) targets.Add(t);
                }
            }

            // Sınırlı ardışık polling: bağlantı fırtınasını ve gereksiz thread sayısını azaltır.
            for (int i = 0; i < targets.Count; i++)
            {
                if (!_running) break;
                try { ProcessInboxMessages(GetInboxMessages(GetUserTopicUrl(targets[i])), true); }
                catch { }
                if (i < targets.Count - 1) Thread.Sleep(60);
            }
        }

        private string[] GetInboxMessages(string fullUrl)
        {
            try
            {
                string raw = HttpGet(fullUrl, RequestTimeoutMs);
                if (string.IsNullOrEmpty(raw)) return new string[0];
                return raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch { }
            return new string[0];
        }

        private void ProcessInboxMessages(string[] lines, bool isPrivateMailbox)
        {
            if (lines == null || lines.Length == 0) return;
            JavaScriptSerializer ser = new JavaScriptSerializer();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    Dictionary<string, object> env = ser.Deserialize<Dictionary<string, object>>(line);
                    if (env == null) continue;
                    if (!env.ContainsKey("id") || !env.ContainsKey("message")) continue;

                    string id = env["id"] != null ? env["id"].ToString() : null;
                    if (string.IsNullOrEmpty(id) || IsDuplicateMessage(id)) continue;

                    string msgJson = env["message"].ToString();
                    if (string.IsNullOrWhiteSpace(msgJson)) continue;

                    Dictionary<string, object> data;
                    try { data = ser.Deserialize<Dictionary<string, object>>(msgJson); }
                    catch { continue; }
                    if (data == null || !data.ContainsKey("type")) continue;

                    string type = SafeStr(data, "type");
                    string sender = SafeStr(data, "user");
                    if (string.IsNullOrEmpty(sender)) sender = SafeStr(data, "from_nick");

                    if (string.Equals(sender, UserName, StringComparison.OrdinalIgnoreCase) && !string.Equals(type, "friend_ack", StringComparison.OrdinalIgnoreCase))
                    {
                        // kendi paketlerimizi (heartbeat/chat vb.) işlemeyelim
                        if (!string.Equals(type, "friend_req", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (string.Equals(type, "heartbeat", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleHeartbeat(sender, data);
                    }
                    else if (string.Equals(type, "chat", StringComparison.OrdinalIgnoreCase))
                    {
                        string msg = SafeStr(data, "msg");
                        if (!string.IsNullOrEmpty(sender) && !string.Equals(sender, UserName, StringComparison.OrdinalIgnoreCase))
                            SafeRaise(OnChatMessageReceived, sender, msg);
                    }
                    else if (string.Equals(type, "invite", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.Equals(sender, UserName, StringComparison.OrdinalIgnoreCase))
                        {
                            string gName = SafeStr(data, "game");
                            string inv = SafeStr(data, "invite");
                            string room = SafeStr(data, "room");
                            SafeRaiseInvite(OnInviteReceived, sender, gName, string.IsNullOrEmpty(inv) ? room : inv);
                        }
                    }
                    else if (string.Equals(type, "friend_req", StringComparison.OrdinalIgnoreCase) && isPrivateMailbox)
                    {
                        string fromNick = SafeStr(data, "from_nick");
                        string fromCode = SafeStr(data, "from_code");
                        string game = SafeStr(data, "game");
                        string room = SafeStr(data, "room");
                        if (string.IsNullOrEmpty(fromNick)) fromNick = sender;
                        if (!string.IsNullOrEmpty(fromNick) && !string.Equals(fromNick, UserName, StringComparison.OrdinalIgnoreCase))
                            SafeRaiseFriendReq(OnFriendRequestReceived, fromNick, fromCode, game, room);
                    }
                    else if (string.Equals(type, "friend_ack", StringComparison.OrdinalIgnoreCase))
                    {
                        string fromNick = SafeStr(data, "from_nick");
                        string fromCode = SafeStr(data, "from_code");
                        if (string.IsNullOrEmpty(fromNick)) fromNick = sender;
                        if (!string.IsNullOrEmpty(fromNick) && !string.Equals(fromNick, UserName, StringComparison.OrdinalIgnoreCase))
                        {
                            UpdateFriendStatus(fromNick, fromCode, "Aktif");
                            SafeRaise(OnFriendAccepted, fromNick, fromCode);
                        }
                    }
                    else if (string.Equals(type, "direct_invite", StringComparison.OrdinalIgnoreCase) && isPrivateMailbox)
                    {
                        string fromNick = SafeStr(data, "from_nick");
                        string fromCode = SafeStr(data, "from_code");
                        string game = SafeStr(data, "game");
                        string room = SafeStr(data, "room");
                        string hostIp = SafeStr(data, "host_ip");
                        string virtIp = SafeStr(data, "virt_ip");
                        if (string.IsNullOrEmpty(fromNick)) fromNick = sender;
                        if (!string.IsNullOrEmpty(fromNick) && !string.Equals(fromNick, UserName, StringComparison.OrdinalIgnoreCase))
                            SafeRaiseDirectInvite(OnDirectGameInvite, fromNick, fromCode, game, room, hostIp, virtIp);
                    }
                }
                catch { }
            }
        }

        private void HandleHeartbeat(string sender, Dictionary<string, object> data)
        {
            if (string.IsNullOrWhiteSpace(sender)) return;
            string code = SafeStr(data, "code");
            string game = SafeStr(data, "game");
            string status = SafeStr(data, "status");
            string gamever = SafeStr(data, "gamever");
            string appver = SafeStr(data, "appver");
            string invite = SafeStr(data, "invite");
            string room = SafeStr(data, "room");
            string stun = SafeStr(data, "stun");

            bool isSaved = false;
            lock (_savedFriendsLock)
            {
                for (int i = 0; i < _savedFriends.Count; i++)
                {
                    SavedFriend sf = _savedFriends[i];
                    if (string.Equals(sf.Nick, sender, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(code) && string.Equals(sf.FriendCode, code, StringComparison.OrdinalIgnoreCase)))
                    {
                        isSaved = true;
                        if (!string.IsNullOrEmpty(code)) sf.FriendCode = code;
                        if (!string.Equals(sf.Status, "Aktif", StringComparison.OrdinalIgnoreCase)) sf.Status = "Aktif";
                        if (!string.IsNullOrEmpty(sender)) sf.Nick = sender;
                        break;
                    }
                }
            }

            lock (_livePresenceLock)
            {
                FriendPresence fp;
                if (!_livePresence.TryGetValue(sender, out fp) || fp == null)
                {
                    fp = new FriendPresence();
                    _livePresence[sender] = fp;
                }
                fp.UserName = sender;
                fp.FriendCode = string.IsNullOrEmpty(code) ? fp.FriendCode : code;
                fp.ActiveGame = string.IsNullOrEmpty(game) ? "Titan Hub" : game;
                fp.Status = string.IsNullOrEmpty(status) ? "Çevrimiçi" : status;
                fp.GameVersion = gamever ?? "";
                fp.AppVersion = appver ?? "";
                fp.InviteUrl = string.IsNullOrEmpty(invite) ? "steam://run/480//" : invite;
                fp.RoomCode = string.IsNullOrEmpty(room) ? "TITAN-TR" : room;
                if (!string.IsNullOrEmpty(stun)) fp.LastKnownIp = stun;
                fp.LastSeen = DateTime.Now;
                fp.IsOnline = true;
                fp.IsSavedFriend = isSaved;

                string localApp = CurrentAppVersion ?? AutoUpdateService.CurrentAppVersion ?? "";
                if (!string.IsNullOrWhiteSpace(localApp) &&
                    !string.IsNullOrWhiteSpace(fp.AppVersion) &&
                    !string.Equals(localApp, fp.AppVersion, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(sender, UserName, StringComparison.OrdinalIgnoreCase))
                {
                    MaybeNotifyPeerAppVersionDifferent(sender, fp.AppVersion);
                }
            }
        }

        private void UpdateFriendStatus(string nick, string code, string status)
        {
            bool changed = false;
            lock (_savedFriendsLock)
            {
                for (int i = 0; i < _savedFriends.Count; i++)
                {
                    SavedFriend sf = _savedFriends[i];
                    if (string.Equals(sf.Nick, nick, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(code) && string.Equals(sf.FriendCode, code, StringComparison.OrdinalIgnoreCase)))
                    {
                        changed = changed || !string.Equals(sf.Status, status, StringComparison.OrdinalIgnoreCase) ||
                                  !string.Equals(sf.Nick, nick, StringComparison.OrdinalIgnoreCase) ||
                                  (!string.IsNullOrEmpty(code) && !string.Equals(sf.FriendCode, code, StringComparison.OrdinalIgnoreCase));
                        sf.Nick = nick;
                        if (!string.IsNullOrEmpty(code)) sf.FriendCode = code;
                        sf.Status = status;
                    }
                }
            }
            if (changed) SaveFriends();
        }

        private void MaybeNotifyPeerAppVersionDifferent(string peerNick, string peerVersion)
        {
            try
            {
                string key = (peerNick ?? "") + "|" + (peerVersion ?? "");
                DateTime now = DateTime.UtcNow;
                lock (_livePresenceLock)
                {
                    if (string.Equals(_lastPeerAppVersionNoticeKey, key, StringComparison.OrdinalIgnoreCase) &&
                        (now - _lastPeerAppVersionNoticeAt).TotalSeconds < 75)
                        return;
                    _lastPeerAppVersionNoticeKey = key;
                    _lastPeerAppVersionNoticeAt = now;
                }

                Action<string, string> h = OnPeerAppVersionDifferent;
                if (h != null)
                {
                    try { h(peerNick ?? "", peerVersion ?? ""); } catch { }
                }
            }
            catch { }
        }

        private void NotifyFriendsUpdated(bool force)
        {
            List<FriendPresence> combinedList = new List<FriendPresence>();
            HashSet<string> addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            lock (_livePresenceLock)
            {
                List<string> stale = new List<string>();
                foreach (KeyValuePair<string, FriendPresence> kv in _livePresence)
                {
                    FriendPresence p = kv.Value;
                    if (p == null) { stale.Add(kv.Key); continue; }
                    if ((DateTime.Now - p.LastSeen).TotalMinutes > PresenceTtlMinutes)
                    {
                        stale.Add(kv.Key);
                        continue;
                    }
                    p.IsOnline = true;
                    combinedList.Add(ClonePresence(p));
                    if (!string.IsNullOrEmpty(p.UserName)) addedKeys.Add(p.UserName);
                    if (!string.IsNullOrEmpty(p.FriendCode)) addedKeys.Add(p.FriendCode);
                }
                for (int i = 0; i < stale.Count; i++) _livePresence.Remove(stale[i]);
            }

            lock (_savedFriendsLock)
            {
                for (int i = 0; i < _savedFriends.Count; i++)
                {
                    SavedFriend sf = _savedFriends[i];
                    if (sf == null) continue;
                    if (string.Equals(sf.Nick, UserName, StringComparison.OrdinalIgnoreCase)) continue;

                    if (addedKeys.Contains(sf.Nick ?? "") || addedKeys.Contains(sf.FriendCode ?? "")) continue;

                    FriendPresence fp = new FriendPresence();
                    fp.UserName = sf.Nick;
                    fp.FriendCode = sf.FriendCode;
                    fp.ActiveGame = "Titan Hub";
                    fp.Status = string.Equals(sf.Status, "İstek Gönderildi", StringComparison.OrdinalIgnoreCase) ? "İstek Beklemede" : "Çevrimdışı";
                    fp.InviteUrl = "steam://run/480//";
                    fp.LastSeen = sf.AddedAt;
                    fp.IsOnline = false;
                    fp.IsSavedFriend = true;
                    combinedList.Add(fp);
                    if (!string.IsNullOrEmpty(sf.Nick)) addedKeys.Add(sf.Nick);
                    if (!string.IsNullOrEmpty(sf.FriendCode)) addedKeys.Add(sf.FriendCode);
                }
            }

            combinedList.Sort(delegate(FriendPresence a, FriendPresence b)
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                int ao = a.IsOnline ? 0 : 1;
                int bo = b.IsOnline ? 0 : 1;
                if (ao != bo) return ao.CompareTo(bo);
                return string.Compare(a.UserName ?? "", b.UserName ?? "", StringComparison.OrdinalIgnoreCase);
            });

            string sig = BuildSignature(combinedList);
            if (!force && string.Equals(sig, _lastPublishedSignature, StringComparison.Ordinal)) return;
            _lastPublishedSignature = sig;

            Action<List<FriendPresence>> handler = OnFriendsUpdated;
            if (handler != null)
            {
                try { handler(combinedList); } catch { }
            }
        }

        private static FriendPresence ClonePresence(FriendPresence p)
        {
            return new FriendPresence
            {
                UserName = p.UserName,
                FriendCode = p.FriendCode,
                ActiveGame = p.ActiveGame,
                Status = p.Status,
                GameVersion = p.GameVersion,
                InviteUrl = p.InviteUrl,
                RoomCode = p.RoomCode,
                LastSeen = p.LastSeen,
                IsOnline = p.IsOnline,
                IsSavedFriend = p.IsSavedFriend,
                LastKnownIp = p.LastKnownIp
            };
        }

        private static string BuildSignature(List<FriendPresence> list)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                FriendPresence p = list[i];
                if (p == null) continue;
                sb.Append(p.UserName ?? "").Append('|')
                  .Append(p.FriendCode ?? "").Append('|')
                  .Append(p.IsOnline ? '1' : '0').Append('|')
                  .Append(p.Status ?? "").Append('|')
                  .Append(p.ActiveGame ?? "").Append('|')
                  .Append(p.RoomCode ?? "").Append('|')
                  .Append(p.GameVersion ?? "").Append(';');
            }
            return sb.ToString();
        }

        public List<FriendPresence> GetLiveFriendsList()
        {
            List<FriendPresence> list = new List<FriendPresence>();
            DateTime now = DateTime.Now;
            lock (_livePresenceLock)
            {
                foreach (KeyValuePair<string, FriendPresence> kv in _livePresence)
                {
                    FriendPresence p = kv.Value;
                    if (p == null) continue;
                    if ((now - p.LastSeen).TotalMinutes <= PresenceTtlMinutes)
                        list.Add(ClonePresence(p));
                }
            }
            return list;
        }

        private bool IsDuplicateMessage(string id)
        {
            lock (_processedLock)
            {
                if (_processedMsgIds.Contains(id)) return true;
                _processedMsgIds.Add(id);
                _processedOrder.Enqueue(id);
                while (_processedOrder.Count > MaxProcessedIds)
                {
                    string old = _processedOrder.Dequeue();
                    _processedMsgIds.Remove(old);
                }
                return false;
            }
        }

        private string GetRoomTopicUrl()
        {
            return GetRoomTopicUrl(false);
        }

        private string GetRoomTopicUrl(bool poll)
        {
            string room = SanitizeTopic(RoomCode ?? "TITAN-TR");
            return BaseUrl + TopicPrefix + room + "/json?poll=1&since=30s";
        }

        private string GetUserTopicUrl(string nickOrCode)
        {
            return BaseUrl + "titan_user_" + SanitizeTopic(nickOrCode) + "/json?poll=1&since=30s";
        }

        private static string SanitizeTopic(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "unknown";
            string clean = input.Trim();
            StringBuilder sb = new StringBuilder(clean.Length);
            for (int i = 0; i < clean.Length; i++)
            {
                char c = clean[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            string s = sb.ToString();
            return string.IsNullOrEmpty(s) ? "unknown" : s;
        }

        private static string SafeStr(Dictionary<string, object> data, string key)
        {
            try
            {
                if (data == null || !data.ContainsKey(key) || data[key] == null) return string.Empty;
                return data[key].ToString();
            }
            catch { return string.Empty; }
        }

        private static void PostJson(string url, string json)
        {
            if (string.IsNullOrEmpty(url)) return;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.Proxy = null;
            req.Timeout = RequestTimeoutMs;
            req.ReadWriteTimeout = RequestTimeoutMs;
            req.ContentType = "text/plain; charset=utf-8";
            byte[] bytes = Encoding.UTF8.GetBytes(json ?? "");
            req.ContentLength = bytes.Length;
            using (Stream s = req.GetRequestStream())
            {
                s.Write(bytes, 0, bytes.Length);
            }
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) { }
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Proxy = null;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static void SafeRaise(Action<string, string> evt, string a, string b)
        {
            try { if (evt != null) evt(a, b); } catch { }
        }

        private static void SafeRaiseInvite(Action<string, string, string> evt, string a, string b, string c)
        {
            try { if (evt != null) evt(a, b, c); } catch { }
        }

        private static void SafeRaiseFriendReq(Action<string, string, string, string> evt, string a, string b, string c, string d)
        {
            try { if (evt != null) evt(a, b, c, d); } catch { }
        }

        private static void SafeRaiseDirectInvite(Action<string, string, string, string, string, string> evt, string a, string b, string c, string d, string e, string f)
        {
            try { if (evt != null) evt(a, b, c, d, e, f); } catch { }
        }

        public void Dispose()
        {
            Stop();
            try { if (_pollThread != null) _pollThread.Join(1000); } catch { }
        }
    }
}
