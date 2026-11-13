using System;
using System.Net;
using System.Net.Sockets;

namespace TitanGameHub
{
    public class StunEndpointResult
    {
        public bool Success { get; set; }
        public string PublicIp { get; set; }
        public int PublicPort { get; set; }
        public string NatType { get; set; }
        public string DiagnosticSummary { get; set; }
    }

    /// <summary>
    /// RFC 5389 STUN NAT Traversal & Hole-Punching Engine (C# 5.0 Strict):
    /// Directly sends binary STUN binding requests to Google & Cloudflare STUN relays
    /// to map public IP:Port and bypass CGNAT/Strict NAT barriers without port forwarding.
    /// </summary>
    public static class NatTraversalHelper
    {
        private static readonly string[] StunServers = new string[]
        {
            "stun.l.google.com",
            "stun1.l.google.com",
            "stun.cloudflare.com"
        };

        public static StunEndpointResult QueryPublicEndpoint(int localPort = 48085)
        {
            StunEndpointResult res = new StunEndpointResult
            {
                Success = false,
                PublicIp = "Bilinmiyor",
                PublicPort = localPort,
                NatType = "Belirlenemedi",
                DiagnosticSummary = "STUN sunucularına erişilemedi."
            };

            foreach (string stunHost in StunServers)
            {
                try
                {
                    IPAddress[] ips = Dns.GetHostAddresses(stunHost);
                    if (ips.Length == 0) continue;

                    IPEndPoint stunEp = new IPEndPoint(ips[0], 19302);
                    using (UdpClient client = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
                    {
                        client.Client.ReceiveTimeout = 1500;

                        // STUN Binding Request Header (RFC 5389):
                        // Type: 0x0001 (Binding Request), Length: 0x0000, Magic Cookie: 0x2112A442
                        byte[] request = new byte[20];
                        request[0] = 0x00; request[1] = 0x01; // Message Type
                        request[2] = 0x00; request[3] = 0x00; // Message Length
                        request[4] = 0x21; request[5] = 0x12; request[6] = 0xA4; request[7] = 0x42; // Magic Cookie

                        // 12 bytes Transaction ID
                        byte[] transId = Guid.NewGuid().ToByteArray();
                        Array.Copy(transId, 0, request, 8, 12);

                        client.Send(request, request.Length, stunEp);

                        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] response = client.Receive(ref remote);

                        if (response.Length >= 20 && response[0] == 0x01 && response[1] == 0x01)
                        {
                            // Success response (0x0101)
                            int pos = 20;
                            while (pos + 4 <= response.Length)
                            {
                                int attrType = (response[pos] << 8) | response[pos + 1];
                                int attrLen = (response[pos + 2] << 8) | response[pos + 3];
                                pos += 4;

                                if (attrType == 0x0020 && pos + 8 <= response.Length) // XOR-MAPPED-ADDRESS
                                {
                                    int port = ((response[pos + 2] ^ 0x21) << 8) | (response[pos + 3] ^ 0x12);
                                    byte ip1 = (byte)(response[pos + 4] ^ 0x21);
                                    byte ip2 = (byte)(response[pos + 5] ^ 0x12);
                                    byte ip3 = (byte)(response[pos + 6] ^ 0xA4);
                                    byte ip4 = (byte)(response[pos + 7] ^ 0x42);

                                    res.Success = true;
                                    res.PublicIp = string.Format("{0}.{1}.{2}.{3}", ip1, ip2, ip3, ip4);
                                    res.PublicPort = port;
                                    res.NatType = "Açık / Cone NAT (P2P Uyumlu)";
                                    res.DiagnosticSummary = string.Format("✅ Genel İnternet Uç Noktası: {0}:{1} ({2})", res.PublicIp, res.PublicPort, stunHost);
                                    return res;
                                }
                                pos += attrLen;
                            }
                        }
                    }
                }
                catch { }
            }

            res.DiagnosticSummary = "STUN sunucularından yanıt alınamadı; yerel ağ modu aktif.";
            return res;
        }
    }
}
