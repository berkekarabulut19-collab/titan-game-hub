using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace TitanGameHub
{
    public class SwarmSaveSnapshot
    {
        public string GameId { get; set; }
        public string SenderNick { get; set; }
        public string Hash { get; set; }
        public DateTime Timestamp { get; set; }
        public string LocalStoragePath { get; set; }
        public long FileSizeBytes { get; set; }
    }

    /// <summary>
    /// P2P Co-Op Save Swarm Mesh Service:
    /// Automatically mirrors the host's world save across all squad members in real time over P2P sockets.
    /// If the host disconnects or crashes, any squad member can instantly resume hosting the server
    /// with zero lost progress or player inventory rollback.
    /// </summary>
    public static class P2pSaveSwarmMeshService
    {
        private const int MeshPort = 48092;
        private static TcpListener _meshServer;
        private static Thread _meshThread;
        private static bool _isRunning = false;
        private static readonly object _sync = new object();
        private static readonly List<SwarmSaveSnapshot> _cachedSnapshots = new List<SwarmSaveSnapshot>();

        public static bool IsRunning
        {
            get { return _isRunning; }
        }

        public static List<SwarmSaveSnapshot> Snapshots
        {
            get
            {
                lock (_sync)
                {
                    return new List<SwarmSaveSnapshot>(_cachedSnapshots);
                }
            }
        }

        public static void StartMeshListener()
        {
            lock (_sync)
            {
                if (_isRunning) return;
                try
                {
                    _meshServer = new TcpListener(IPAddress.Any, MeshPort);
                    _meshServer.Start();
                    _meshThread = new Thread(ListenForSwarmSaves);
                    _meshThread.IsBackground = true;
                    _meshThread.Start();
                    _isRunning = true;
                }
                catch { }
            }
        }

        public static void StopMeshListener()
        {
            lock (_sync)
            {
                _isRunning = false;
                try { if (_meshServer != null) _meshServer.Stop(); } catch { }
            }
        }

        private static void ListenForSwarmSaves()
        {
            while (_isRunning)
            {
                try
                {
                    TcpClient client = _meshServer.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate(object st)
                    {
                        ReceiveSavePackage((TcpClient)st);
                    }, client);
                }
                catch
                {
                    if (!_isRunning) break;
                }
            }
        }

        private static void ReceiveSavePackage(TcpClient client)
        {
            using (client)
            using (NetworkStream ns = client.GetStream())
            using (BinaryReader br = new BinaryReader(ns))
            using (BinaryWriter bw = new BinaryWriter(ns))
            {
                try
                {
                    string gameId = br.ReadString();
                    string senderNick = br.ReadString();
                    string hash = br.ReadString();
                    int dataLen = br.ReadInt32();

                    if (dataLen <= 0 || dataLen > 100 * 1024 * 1024) // 100MB limit
                    {
                        bw.Write(false);
                        return;
                    }

                    byte[] zipData = br.ReadBytes(dataLen);
                    string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SaveBackups", "SwarmMesh", gameId);
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                    string saveFile = Path.Combine(targetDir, string.Format("swarm_{0}_{1:yyyyMMdd_HHmmss}.zip", senderNick, DateTime.Now));
                    File.WriteAllBytes(saveFile, zipData);

                    lock (_sync)
                    {
                        _cachedSnapshots.Add(new SwarmSaveSnapshot
                        {
                            GameId = gameId,
                            SenderNick = senderNick,
                            Hash = hash,
                            Timestamp = DateTime.Now,
                            LocalStoragePath = saveFile,
                            FileSizeBytes = zipData.Length
                        });
                    }

                    bw.Write(true);
                }
                catch { }
            }
        }

        /// <summary>
        /// Broadcasts current active save to all peers in the room IP list.
        /// </summary>
        public static void BroadcastSaveToPeersAsync(GameProfile profile, List<string> peerIps, Action<string> onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (profile == null) return;
                List<string> savList = GvasSaveEditorService.LocateSaveFilesForGame(profile);
                if (savList.Count == 0)
                {
                    if (onComplete != null) onComplete("Yedeklenecek yerel save dosyası bulunamadı.");
                    return;
                }

                try
                {
                    // Create in-memory zip of found save files
                    byte[] zipBytes;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (ZipArchive archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        {
                            for (int i = 0; i < savList.Count; i++)
                            {
                                string sPath = savList[i];
                                string entryName = Path.GetFileName(sPath);
                                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                                using (Stream es = entry.Open())
                                using (FileStream fs = new FileStream(sPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    fs.CopyTo(es);
                                }
                            }
                        }
                        zipBytes = ms.ToArray();
                    }

                    string hash;
                    using (SHA256 sha = SHA256.Create())
                    {
                        hash = BitConverter.ToString(sha.ComputeHash(zipBytes)).Replace("-", "").Substring(0, 16);
                    }

                    int sentCount = 0;
                    if (peerIps != null)
                    {
                        for (int p = 0; p < peerIps.Count; p++)
                        {
                            string ip = peerIps[p];
                            try
                            {
                                using (TcpClient client = new TcpClient())
                                {
                                    IAsyncResult ar = client.BeginConnect(ip, MeshPort, null, null);
                                    if (ar.AsyncWaitHandle.WaitOne(1200))
                                    {
                                        client.EndConnect(ar);
                                        using (NetworkStream ns = client.GetStream())
                                        using (BinaryWriter bw = new BinaryWriter(ns))
                                        using (BinaryReader br = new BinaryReader(ns))
                                        {
                                            bw.Write(profile.Id);
                                            bw.Write(Environment.UserName);
                                            bw.Write(hash);
                                            bw.Write(zipBytes.Length);
                                            bw.Write(zipBytes);

                                            if (br.ReadBoolean()) sentCount++;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    string res = string.Format("💾 P2P Save Swarm: Save paketi ({0:0.0} KB) {1} takım arkadaşına anında eşitlendi!", zipBytes.Length / 1024.0, sentCount);
                    if (onComplete != null) onComplete(res);
                }
                catch (Exception ex)
                {
                    if (onComplete != null) onComplete("Save eşitleme hatası: " + ex.Message);
                }
            });
        }
    }
}
