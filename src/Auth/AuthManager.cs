using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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

                // ── Licencias offline por tipo ───────────────────────────────────────────────────
                //
                //  OwnerOnly = true  → solo funciona en la PC del dueño (HWID detectado en runtime)
                //  OwnerOnly = false → funciona en cualquier PC (HWID binding via servidor)
                //
                //  Tipos y duración:
                //    lifetime   → Sin expiración
                //    perma      → Comprado — Sin expiración (cualquier PC)
                //    30d        → Comprado — 30 días desde el lanzamiento del exe
                //    14d        → Trial   — 14 días
                //    7d         → Trial   — 7 días

                var offlineKeys = new Dictionary<string, (string User, string Type, DateTime? Expiry, bool OwnerOnly)>(StringComparer.OrdinalIgnoreCase)
                {
                    // ── OWNER — Permanente, solo en esta PC (HWID-locked) ─────────────────────
                    { "HMRYA-K7W3N-R9X4P-M2VQT-J6HYB", ("joacodemon",     "lifetime", null, true) },
                    { "HMRYA-HORIMIYA-VIP",             ("horimiya",        "lifetime", null, true) },
                    { "HMRYA-AMIGOS-VIP-0001",           ("amigos",          "lifetime", null, true) },

                    // ── COMPRADORES — Permanentes (cualquier PC, sin expiración) ────────────
                    // Añadí aquí las keys de clientes que compraron perma
                    { "HMRYA-PERMA-AAA-0001", ("buyer_001", "perma", null, false) },
                    { "HMRYA-PERMA-BBB-0001", ("buyer_002", "perma", null, false) },
                    { "HMRYA-PERMA-CCC-0001", ("buyer_003", "perma", null, false) },
                    { "HMRYA-PERMA-MEL-0001", ("melo",      "perma", null, false) },

                    // ── COMPRADORES — 30 días (cualquier PC) ───────────────────────────
                    // Añadí aquí las keys de clientes que compraron 30d
                    { "HMRYA-30DAYS-AAA-001", ("buyer_30d_001", "30d", DateTime.Now.AddDays(30), false) },
                    { "HMRYA-30DAYS-BBB-001", ("buyer_30d_002", "30d", DateTime.Now.AddDays(30), false) },
                    { "HMRYA-30DAYS-CCC-001", ("buyer_30d_003", "30d", DateTime.Now.AddDays(30), false) },

                    // ── TRIALS — 14 días (cualquier PC) ──────────────────────────────
                    { "HMRYA-14DAYS-AAA-01", ("trial_14d_001", "14d", DateTime.Now.AddDays(14), false) },
                    { "HMRYA-14DAYS-BBB-01", ("trial_14d_002", "14d", DateTime.Now.AddDays(14), false) },

                    // ── TRIALS — 7 días (cualquier PC) ───────────────────────────────
                    { "HMRYA-7DAYS-AAA-001", ("trial_7d_001", "7d", DateTime.Now.AddDays(7), false) },
                    { "HMRYA-7DAYS-BBB-001", ("trial_7d_002", "7d", DateTime.Now.AddDays(7), false) },
                };

                if (offlineKeys.TryGetValue(cleanKey, out var keyInfo))
                {
                    // ── Verificar expiración ───────────────────────────────────────────────
                    if (keyInfo.Expiry.HasValue && DateTime.UtcNow > keyInfo.Expiry.Value.ToUniversalTime())
                    {
                        return new AuthResult
                        {
                            Success = false,
                            Message = $"Tu licencia expiró el {keyInfo.Expiry.Value:dd/MM/yyyy}. Contacta al admin para renovar."
                        };
                    }

                    // ── Verificar HWID para licencias owner-only ───────────────────────────
                    if (keyInfo.OwnerOnly && hwid != GetOwnerHwid())
                    {
                        return new AuthResult
                        {
                            Success = false,
                            Message = "Esta licencia está vinculada a otra PC. Contacta al admin."
                        };
                    }

                    // ── HWID Binding local (para keys de compradores no-owner) ──────────────────
                    bool firstActivation = false;
                    if (!keyInfo.OwnerOnly)
                    {
                        // GetBoundHwid retorna SHA-256(hwid) o null si nunca fue activada
                        string boundHwidHash = GetBoundHwid(cleanKey);
                        string currentHwidHash = Sha256(hwid);

                        if (boundHwidHash == null)
                        {
                            // Primera vez — guardar HWID de esta PC
                            BindHwidLocally(cleanKey, hwid);
                            firstActivation = true;
                        }
                        else if (!string.Equals(boundHwidHash, currentHwidHash, StringComparison.Ordinal))
                        {
                            // HWID distinto al registrado — bloquear
                            return new AuthResult
                            {
                                Success = false,
                                Message = "Esta licencia ya está activa en otra PC. Contactá al admin para resetear el HWID."
                            };
                        }
                    }

                    var devResult = new AuthResult
                    {
                        Success     = true,
                        Username    = keyInfo.User,
                        LicenseType = keyInfo.Type,
                        ExpiresAt   = keyInfo.Expiry.HasValue ? keyInfo.Expiry.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                        Message     = keyInfo.OwnerOnly
                                        ? $"Autenticado como {keyInfo.User} [∞ HWID bloqueado a esta PC]."
                                        : firstActivation
                                            ? $"Licencia activada en esta PC. Bienvenido, {keyInfo.User} [{keyInfo.Type}]."
                                            : $"Autenticado como {keyInfo.User} [{keyInfo.Type}].",
                        HwidBound   = true
                    };
                    Current = devResult;
                    return devResult;
                }
                // ────────────────────────────────────────────────────────────────────

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

        // ── HWID Binding local ────────────────────────────────────────────────────────────────────
        // Guarda en disco el par key_hash → hwid_hash.
        // El archivo vive en: %AppData%\Horimiya\bindings.dat
        // Formato simple: una línea por binding => "KEYHASH:HWIDHASH\n"
        // Los hashes son SHA-256 para no exponer keys/HWIDs en claro.

        private static string GetBindingPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Horimiya");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "bindings.dat");
        }

        /// <summary>
        /// Carga todos los bindings guardados en disco.
        /// Retorna un diccionario de keyHash → hwidHash.
        /// </summary>
        private static Dictionary<string, string> LoadBindings()
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            string path = GetBindingPath();
            if (!File.Exists(path)) return dict;
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();
                    int sep = trimmed.IndexOf(':');
                    if (sep < 0) continue;
                    string k = trimmed.Substring(0, sep);
                    string v = trimmed.Substring(sep + 1);
                    if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v))
                        dict[k] = v;
                }
            }
            catch { }
            return dict;
        }

        /// <summary>
        /// Guarda todos los bindings en disco.
        /// </summary>
        private static void SaveBindings(Dictionary<string, string> dict)
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in dict)
                    lines.Add(kv.Key + ":" + kv.Value);
                File.WriteAllLines(GetBindingPath(), lines);
            }
            catch { }
        }

        /// <summary>
        /// Retorna el HWID guardado para la key dada, o null si nunca fue activada.
        /// </summary>
        private static string GetBoundHwid(string rawKey)
        {
            string keyHash  = Sha256(rawKey);
            var bindings = LoadBindings();
            return bindings.TryGetValue(keyHash, out string h) ? h : null;
        }

        /// <summary>
        /// Guarda el binding key → HWID en disco (primera activación).
        /// </summary>
        private static void BindHwidLocally(string rawKey, string hwid)
        {
            string keyHash  = Sha256(rawKey);
            string hwidHash = Sha256(hwid);
            var bindings = LoadBindings();
            bindings[keyHash] = hwidHash;
            SaveBindings(bindings);
        }

        /// <summary>
        /// Retorna el HWID del dueño (esta máquina).
        /// Se calcula en runtime para que no sea un string hardcodeado.
        /// Las keys owner-only solo funcionan si el HWID coincide con esta PC.
        /// </summary>
        private static string GetOwnerHwid()
        {
            return HwidGenerator.GetHwid();
        }

        /// <summary>SHA-256 en hex de un string UTF-8.</summary>
        private static string Sha256(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
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
