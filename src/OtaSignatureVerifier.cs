using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TitanGameHub
{
    /// <summary>
    /// Fail-closed OTA package authentication.
    ///
    /// The update channel is a public ntfy topic: anyone who learns the topic name can publish.
    /// Therefore a message is only accepted when it carries an RSA-2048 / SHA-256 signature over
    /// "TITAN-OTA/1|&lt;timestamp&gt;|&lt;sha256-of-zip&gt;", produced with the private key that never ships.
    /// Unsigned, malformed or hash-mismatching packages are rejected and deleted.
    /// </summary>
    public static class OtaSignatureVerifier
    {
        // Public half of the release signing key (private half lives in tools\ota_private_key.xml
        // and bin\ota_signing_key.xml on the publisher machine, excluded from distributed packages).
        private const string EmbeddedPublicKeyXml =
            "<RSAKeyValue><Modulus>w7kI2EnOJrUt7Vmkb4LlcJXtL9//Wt5NtzibgnrM5ZykomZq2qI8KlAXnLfgrrh2w5D48GODnAv/tT8+yJ98oir8M4eoTGcqgkfajR0ooh8lOlZxEvi98TXSgmDtTv6DhcjGSLpBAtoPVWUfVzpBxC9+CheI+/vWBcrsuWtjXhiuJnJgk8FfZD+1oN3dByf1F/4g8eoNYnGRuoBbCVDt+lqM+HtfgQ06mtSLV8kL7bevChQvkANUmZqFwINuNgfeOk1qF3a6gwHrro8s/69hAbyfrV/FEMF2WdYd0zLjb8beFSFyfDbK5aO67w0dJbzpUQsHwY96DZw82GOkaILlcQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public const string ManifestPrefix = "BIN_MANIFEST#";
        private const string PayloadScheme = "TITAN-OTA/1";

        public static string BuildPayload(long timestamp, string sha256Hex)
        {
            return PayloadScheme + "|" + timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (sha256Hex ?? "").ToLowerInvariant();
        }

        public static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static bool VerifySignature(long timestamp, string sha256Hex, string signatureBase64, out string status)
        {
            status = "";
            if (string.IsNullOrEmpty(signatureBase64))
            {
                status = "OTA paketi imzasız (reddedildi).";
                return false;
            }
            if (string.IsNullOrEmpty(sha256Hex) || sha256Hex.Length != 64)
            {
                status = "OTA manifestinde geçerli SHA-256 yok (reddedildi).";
                return false;
            }

            try
            {
                byte[] sig = Convert.FromBase64String(signatureBase64);
                byte[] data = Encoding.UTF8.GetBytes(BuildPayload(timestamp, sha256Hex));

                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(EmbeddedPublicKeyXml);

                    bool ok = rsa.VerifyData(data, new SHA256CryptoServiceProvider(), sig);
                    status = ok ? "OTA imzası geçerli." : "OTA imzası geçersiz (yayıncı anahtarı uyuşmuyor).";
                    return ok;
                }
            }
            catch (Exception ex)
            {
                status = "OTA imza doğrulama hatası: " + ex.Message;
                return false;
            }
        }

        /// <summary>Verifies the downloaded archive against the signed hash.</summary>
        public static bool VerifyPackageHash(string filePath, string expectedSha256Hex, out string status)
        {
            status = "";
            try
            {
                if (!File.Exists(filePath))
                {
                    status = "İndirilen OTA paketi bulunamadı.";
                    return false;
                }
                string actual = Sha256File(filePath);
                if (!string.Equals(actual, (expectedSha256Hex ?? "").ToLowerInvariant(), StringComparison.Ordinal))
                {
                    status = "OTA paket bütünlüğü bozuk (SHA-256 uyuşmuyor).";
                    return false;
                }
                status = "OTA paket bütünlüğü doğrulandı.";
                return true;
            }
            catch (Exception ex)
            {
                status = "OTA hash hatası: " + ex.Message;
                return false;
            }
        }

        /// <summary>Private key lookup used only by the publisher side. Returns null on end-user machines.</summary>
        public static string FindPrivateKeyPath()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("TITAN_OTA_KEY");
                if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new string[]
                {
                    Path.Combine(baseDir, "ota_signing_key.xml"),
                    Path.Combine(baseDir, "..", "tools", "ota_private_key.xml"),
                    Path.Combine(baseDir, "tools", "ota_private_key.xml")
                };
                foreach (string c in candidates)
                {
                    try { if (File.Exists(c)) return Path.GetFullPath(c); }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Publisher side only: produces the manifest tail that must be published with the package.</summary>
        public static bool TrySignPackage(string zipPath, long timestamp, out string manifestMessage, out string error)
        {
            manifestMessage = "";
            error = "";

            string keyPath = FindPrivateKeyPath();
            if (string.IsNullOrEmpty(keyPath))
            {
                error = "İmza anahtarı bulunamadı (tools\\ota_private_key.xml). Yayın reddedildi: imzasız paket dağıtılmaz.";
                return false;
            }

            try
            {
                string sha = Sha256File(zipPath);
                byte[] data = Encoding.UTF8.GetBytes(BuildPayload(timestamp, sha));

                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(File.ReadAllText(keyPath, Encoding.ASCII));

                    byte[] sig = rsa.SignData(data, new SHA256CryptoServiceProvider());
                    manifestMessage = ManifestPrefix + timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                      "#" + sha + "#" + Convert.ToBase64String(sig);
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "İmzalama hatası: " + ex.Message;
                return false;
            }
        }

        /// <summary>Parses "BIN_MANIFEST#ts#sha256#signature" into its parts.</summary>
        public static bool TryParseManifest(string message, out long timestamp, out string sha256Hex, out string signatureBase64)
        {
            timestamp = 0;
            sha256Hex = null;
            signatureBase64 = null;

            if (string.IsNullOrEmpty(message)) return false;
            if (!message.StartsWith(ManifestPrefix, StringComparison.Ordinal)) return false;

            string[] parts = message.Split('#');
            if (parts.Length < 4) return false;

            if (!long.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out timestamp))
                return false;

            sha256Hex = parts[2].Trim().ToLowerInvariant();
            signatureBase64 = parts[3].Trim();
            return sha256Hex.Length == 64 && signatureBase64.Length > 0;
        }
    }
}
