using System;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Horimiya.Auth
{
    /// <summary>
    /// Handles communication with the Horimiya auth API.
    /// Sends HWID + license key and parses the response.
    /// </summary>
    public static class AuthManager
    {
        // ── CONFIGURE THIS: Replace with your actual server URL ──────────────
        public static string ApiUrl = "http://horimiya.free.nf/api/auth.php";
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The authenticated session. Null until a successful auth.</summary>
        public static AuthResult Current { get; private set; }

        /// <summary>Whether the current session is authenticated.</summary>
        public static bool IsAuthenticated => Current != null && Current.Success;

        private static readonly HttpClient _client;

        static AuthManager()
        {
            // Force TLS 1.2/1.3 (required by most modern servers)
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36");
            _client.Timeout = TimeSpan.FromSeconds(15);
        }

        /// <summary>
        /// Authenticates using the provided license key and the local machine HWID.
        /// On success, sets <see cref="Current"/> and returns the result.
        /// </summary>
        public static AuthResult Authenticate(string licenseKey)
        {
            try
            {
                string hwid = HwidGenerator.GetHwid();
                string cleanKey = licenseKey?.Trim().ToUpperInvariant() ?? "";

                // ── Hardcoded dev key (works offline / before server deployment) ──
                if (cleanKey == "HMRYA-K7W3N-R9X4P-M2VQT-J6HYB")
                {
                    var devResult = new AuthResult
                    {
                        Success     = true,
                        Username    = "joacodemon",
                        LicenseType = "lifetime",
                        ExpiresAt   = "",
                        Message     = "Authenticated (owner key).",
                        HwidBound   = false
                    };
                    Current = devResult;
                    return devResult;
                }

                // ── License for Los Poderosos ──
                if (cleanKey == "HMRYA-PODEROSOS-VIP")
                {
                    var devResult = new AuthResult
                    {
                        Success     = true,
                        Username    = "lospoderosos",
                        LicenseType = "lifetime",
                        ExpiresAt   = "",
                        Message     = "Authenticated (Los Poderosos VIP).",
                        HwidBound   = false
                    };
                    Current = devResult;
                    return devResult;
                }
                // ─────────────────────────────────────────────────────────────────

                // Build JSON body manually (no external JSON libs)
                string json = $"{{\"hwid\":\"{EscapeJson(hwid)}\",\"license_key\":\"{EscapeJson(cleanKey)}\"}}";
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = _client.PostAsync(ApiUrl, content).GetAwaiter().GetResult();
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                var result = ParseResponse(body);

                if (result.Success)
                    Current = result;

                return result;
            }
            catch (TaskCanceledException)
            {
                return new AuthResult { Success = false, Message = "Connection timed out. Check your internet connection." };
            }
            catch (HttpRequestException ex)
            {
                return new AuthResult { Success = false, Message = "Cannot reach auth server: " + ex.Message };
            }
            catch (Exception ex)
            {
                return new AuthResult { Success = false, Message = "Error: " + ex.Message };
            }
        }

        /// <summary>Clears the current authenticated session.</summary>
        public static void Logout()
        {
            Current = null;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static AuthResult ParseResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new AuthResult { Success = false, Message = "Empty response from server." };

            var r = new AuthResult
            {
                Success      = GetBool(json, "success"),
                Username     = GetString(json, "username"),
                LicenseType  = GetString(json, "license_type"),
                ExpiresAt    = GetString(json, "expires_at"),
                Message      = GetString(json, "message"),
                HwidBound    = GetBool(json, "hwid_bound")
            };

            return r;
        }

        private static string GetString(string json, string key)
        {
            string search = "\"" + key + "\":\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return "";
            int start = idx + search.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return "";
            return UnescapeJson(json.Substring(start, end - start));
        }

        private static bool GetBool(string json, string key)
        {
            string search = "\"" + key + "\":";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return false;
            int start = idx + search.Length;
            while (start < json.Length && json[start] == ' ') start++;
            if (start + 4 <= json.Length && json.Substring(start, 4) == "true") return true;
            return false;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string UnescapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\\"", "\"").Replace("\\\\", "\\")
                    .Replace("\\n", "\n").Replace("\\r", "\r");
        }
    }
}
