using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TitanGameHub
{
    public class UpnpMappingEntry
    {
        public int ExternalPort { get; set; }
        public int InternalPort { get; set; }
        public string Protocol { get; set; }
        public string InternalClient { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Hardware UPnP IGD (Internet Gateway Device) Port Forwarder:
    /// Discovers router gateway via SSDP M-SEARCH broadcast over UDP 239.255.255.250:1900.
    /// Sends SOAP XML AddPortMapping requests to automatically punch firewall holes in home routers
    /// without requiring friends or hosts to configure router web portals or run VPNs.
    /// </summary>
    public static class UpnpPortForwarderService
    {
        private static string _controlUrl = null;
        private static string _serviceType = null;
        private static string _localIp = null;
        private static string _externalIp = null;
        private static bool _isDiscovered = false;
        private static readonly object _syncLock = new object();
        private static readonly List<UpnpMappingEntry> _activeMappings = new List<UpnpMappingEntry>();

        public static bool IsDiscovered
        {
            get { return _isDiscovered; }
        }

        public static string ExternalIpAddress
        {
            get { return _externalIp ?? "Bilinmiyor"; }
        }

        public static string LocalIpAddress
        {
            get { return _localIp ?? GetLocalIPv4(); }
        }

        public static List<UpnpMappingEntry> ActiveMappings
        {
            get
            {
                lock (_syncLock)
                {
                    return new List<UpnpMappingEntry>(_activeMappings);
                }
            }
        }

        public static string GetLocalIPv4()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    if (endPoint != null)
                    {
                        _localIp = endPoint.Address.ToString();
                        return _localIp;
                    }
                }
            }
            catch { }

            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        _localIp = ip.ToString();
                        return _localIp;
                    }
                }
            }
            catch { }

            _localIp = "127.0.0.1";
            return _localIp;
        }

        /// <summary>
        /// Discovers router UPnP IGD endpoint via SSDP UDP broadcast.
        /// </summary>
        public static bool DiscoverRouter(int timeoutMs, out string logMessage)
        {
            lock (_syncLock)
            {
                if (_isDiscovered && !string.IsNullOrEmpty(_controlUrl))
                {
                    logMessage = string.Format("UPnP Router zaten bağlı: {0}", _controlUrl);
                    return true;
                }

                _localIp = GetLocalIPv4();
                string query = "M-SEARCH * HTTP/1.1\r\n" +
                               "HOST: 239.255.255.250:1900\r\n" +
                               "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
                               "MAN: \"ssdp:discover\"\r\n" +
                               "MX: 3\r\n\r\n";

                byte[] reqBytes = Encoding.ASCII.GetBytes(query);
                Socket client = null;

                try
                {
                    client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeoutMs);
                    client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

                    IPEndPoint ssdpEndpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                    client.SendTo(reqBytes, ssdpEndpoint);

                    byte[] buffer = new byte[4096];
                    DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);

                    while (DateTime.Now < deadline)
                    {
                        try
                        {
                            EndPoint senderEp = new IPEndPoint(IPAddress.Any, 0);
                            int received = client.ReceiveFrom(buffer, ref senderEp);
                            string response = Encoding.ASCII.GetString(buffer, 0, received);

                            string locationUrl = ExtractHeader(response, "LOCATION");
                            if (!string.IsNullOrEmpty(locationUrl))
                            {
                                if (ParseDeviceXml(locationUrl))
                                {
                                    _isDiscovered = true;
                                    QueryExternalIp();
                                    logMessage = string.Format("UPnP Router bulundu ({0})! Dış IP: {1}", locationUrl, ExternalIpAddress);
                                    return true;
                                }
                            }
                        }
                        catch (SocketException)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logMessage = "SSDP Keşif hatası: " + ex.Message;
                    return false;
                }
                finally
                {
                    if (client != null)
                    {
                        try { client.Close(); } catch { }
                    }
                }

                // Fallback attempt with second schema type
                try
                {
                    client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeoutMs);

                    string query2 = "M-SEARCH * HTTP/1.1\r\n" +
                                   "HOST: 239.255.255.250:1900\r\n" +
                                   "ST: urn:schemas-upnp-org:service:WANIPConnection:1\r\n" +
                                   "MAN: \"ssdp:discover\"\r\n" +
                                   "MX: 2\r\n\r\n";
                    byte[] q2Bytes = Encoding.ASCII.GetBytes(query2);
                    IPEndPoint ssdpEndpoint2 = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                    client.SendTo(q2Bytes, ssdpEndpoint2);

                    byte[] buffer2 = new byte[4096];
                    EndPoint senderEp2 = new IPEndPoint(IPAddress.Any, 0);
                    int r2 = client.ReceiveFrom(buffer2, ref senderEp2);
                    string resp2 = Encoding.ASCII.GetString(buffer2, 0, r2);
                    string loc2 = ExtractHeader(resp2, "LOCATION");
                    if (!string.IsNullOrEmpty(loc2) && ParseDeviceXml(loc2))
                    {
                        _isDiscovered = true;
                        QueryExternalIp();
                        logMessage = string.Format("UPnP WANIPConnection bulundu: {0}", loc2);
                        return true;
                    }
                }
                catch { }
                finally
                {
                    if (client != null)
                    {
                        try { client.Close(); } catch { }
                    }
                }

                logMessage = "Modem UPnP yanıt vermedi. Modem arayüzünden UPnP özelliğini aktif edin veya doğrudan P2P Modunu kullanın.";
                return false;
            }
        }

        private static string ExtractHeader(string rawHeaders, string headerName)
        {
            Match m = Regex.Match(rawHeaders, @"(?i)" + headerName + @":\s*(?<val>[^\r\n]+)");
            if (m.Success)
            {
                return m.Groups["val"].Value.Trim();
            }
            return null;
        }

        private static bool ParseDeviceXml(string locationUrl)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(locationUrl);
                req.Timeout = 4000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string xml = reader.ReadToEnd();
                    string[] targetServices = new string[]
                    {
                        "urn:schemas-upnp-org:service:WANIPConnection:1",
                        "urn:schemas-upnp-org:service:WANPPPConnection:1",
                        "urn:schemas-upnp-org:service:WANIPConnection:2"
                    };

                    foreach (string sType in targetServices)
                    {
                        if (xml.IndexOf(sType, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int serviceIdx = xml.IndexOf(sType, StringComparison.OrdinalIgnoreCase);
                            int controlIdx = xml.IndexOf("<controlURL>", serviceIdx, StringComparison.OrdinalIgnoreCase);
                            if (controlIdx >= 0)
                            {
                                int start = controlIdx + "<controlURL>".Length;
                                int end = xml.IndexOf("</controlURL>", start, StringComparison.OrdinalIgnoreCase);
                                if (end > start)
                                {
                                    string relUrl = xml.Substring(start, end - start).Trim();
                                    Uri baseUri = new Uri(locationUrl);
                                    Uri fullControlUri = new Uri(baseUri, relUrl);
                                    _controlUrl = fullControlUri.ToString();
                                    _serviceType = sType;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Adds a port forwarding rule on the router for TCP or UDP.
        /// </summary>
        public static bool ForwardPort(int port, string protocol, string description, out string message)
        {
            if (!_isDiscovered)
            {
                string discMsg;
                if (!DiscoverRouter(3500, out discMsg))
                {
                    message = discMsg;
                    return false;
                }
            }

            string clientIp = LocalIpAddress;
            string soapBody = string.Format(
                "<?xml version=\"1.0\"?>\r\n" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
                "<s:Body>\r\n" +
                "<u:AddPortMapping xmlns:u=\"{0}\">\r\n" +
                "  <NewRemoteHost></NewRemoteHost>\r\n" +
                "  <NewExternalPort>{1}</NewExternalPort>\r\n" +
                "  <NewProtocol>{2}</NewProtocol>\r\n" +
                "  <NewInternalPort>{3}</NewInternalPort>\r\n" +
                "  <NewInternalClient>{4}</NewInternalClient>\r\n" +
                "  <NewEnabled>1</NewEnabled>\r\n" +
                "  <NewPortMappingDescription>{5}</NewPortMappingDescription>\r\n" +
                "  <NewLeaseDuration>0</NewLeaseDuration>\r\n" +
                "</u:AddPortMapping>\r\n" +
                "</s:Body>\r\n" +
                "</s:Envelope>",
                _serviceType, port, protocol.ToUpperInvariant(), port, clientIp, description ?? "TitanGameHub"
            );

            try
            {
                string soapAction = string.Format("\"{0}#AddPortMapping\"", _serviceType);
                string response = SendSoapRequest(_controlUrl, soapAction, soapBody);

                if (response.IndexOf("AddPortMappingResponse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    response.IndexOf("200 OK", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lock (_syncLock)
                    {
                        _activeMappings.Add(new UpnpMappingEntry
                        {
                            ExternalPort = port,
                            InternalPort = port,
                            Protocol = protocol.ToUpperInvariant(),
                            InternalClient = clientIp,
                            Description = description,
                            IsActive = true
                        });
                    }
                    message = string.Format("Port {0} ({1}) başarıyla yönlendirildi! [Dış IP: {2}]", port, protocol, ExternalIpAddress);
                    return true;
                }
                else
                {
                    message = "Router SOAP Yanıtı: " + response;
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = "Port yönlendirme hatası: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Removes a port forwarding rule from the router.
        /// </summary>
        public static bool DeletePortMapping(int port, string protocol, out string message)
        {
            if (!_isDiscovered || string.IsNullOrEmpty(_controlUrl))
            {
                message = "UPnP Router bağlı değil.";
                return false;
            }

            string soapBody = string.Format(
                "<?xml version=\"1.0\"?>\r\n" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
                "<s:Body>\r\n" +
                "<u:DeletePortMapping xmlns:u=\"{0}\">\r\n" +
                "  <NewRemoteHost></NewRemoteHost>\r\n" +
                "  <NewExternalPort>{1}</NewExternalPort>\r\n" +
                "  <NewProtocol>{2}</NewProtocol>\r\n" +
                "</u:DeletePortMapping>\r\n" +
                "</s:Body>\r\n" +
                "</s:Envelope>",
                _serviceType, port, protocol.ToUpperInvariant()
            );

            try
            {
                string soapAction = string.Format("\"{0}#DeletePortMapping\"", _serviceType);
                string response = SendSoapRequest(_controlUrl, soapAction, soapBody);

                lock (_syncLock)
                {
                    _activeMappings.RemoveAll(delegate(UpnpMappingEntry m) {
                        return m.ExternalPort == port && string.Equals(m.Protocol, protocol, StringComparison.OrdinalIgnoreCase);
                    });
                }
                message = string.Format("Port {0} ({1}) yönlendirmesi kaldırıldı.", port, protocol);
                return true;
            }
            catch (Exception ex)
            {
                message = "Kaldırma hatası: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Queries the external public IP address of the router.
        /// </summary>
        public static string QueryExternalIp()
        {
            if (string.IsNullOrEmpty(_controlUrl) || string.IsNullOrEmpty(_serviceType))
            {
                return FetchExternalIpWebFallback();
            }

            string soapBody = string.Format(
                "<?xml version=\"1.0\"?>\r\n" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\r\n" +
                "<s:Body>\r\n" +
                "<u:GetExternalIPAddress xmlns:u=\"{0}\"></u:GetExternalIPAddress>\r\n" +
                "</s:Body>\r\n" +
                "</s:Envelope>",
                _serviceType
            );

            try
            {
                string soapAction = string.Format("\"{0}#GetExternalIPAddress\"", _serviceType);
                string response = SendSoapRequest(_controlUrl, soapAction, soapBody);

                Match m = Regex.Match(response, @"<NewExternalIPAddress>(?<ip>[0-9\.]+)</NewExternalIPAddress>");
                if (m.Success)
                {
                    _externalIp = m.Groups["ip"].Value;
                    return _externalIp;
                }
            }
            catch { }

            return FetchExternalIpWebFallback();
        }

        private static string FetchExternalIpWebFallback()
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers["User-Agent"] = "TitanGameHub/9.0";
                    string ip = wc.DownloadString("https://api.ipify.org").Trim();
                    if (!string.IsNullOrEmpty(ip))
                    {
                        _externalIp = ip;
                        return _externalIp;
                    }
                }
            }
            catch { }

            _externalIp = "127.0.0.1";
            return _externalIp;
        }

        private static string SendSoapRequest(string url, string soapAction, string soapBody)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(soapBody);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "text/xml; charset=\"utf-8\"";
            req.Headers.Add("SOAPAction", soapAction);
            req.ContentLength = bodyBytes.Length;
            req.Timeout = 5000;

            using (Stream reqStream = req.GetRequestStream())
            {
                reqStream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Auto-forwards standard Titan Co-op and Streaming ports in a single call.
        /// </summary>
        public static void AutoForwardTitanPortsAsync(Action<string> onComplete)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg;
                DiscoverRouter(3500, out msg);

                int[] titanPorts = new int[] { 48085, 48099, 47892, 7777 };
                int successCount = 0;

                for (int i = 0; i < titanPorts.Length; i++)
                {
                    int p = titanPorts[i];
                    string pMsg;
                    if (ForwardPort(p, "UDP", "Titan Hub P2P/Voice", out pMsg))
                        successCount++;
                    ForwardPort(p, "TCP", "Titan Hub Stream/Host", out pMsg);
                }

                string finalMsg = string.Format("UPnP Otomatik Yönlendirme Tamamlandı: {0} Port Açıldı. (Dış IP: {1})", successCount, ExternalIpAddress);
                if (onComplete != null)
                {
                    onComplete(finalMsg);
                }
            });
        }
    }
}
