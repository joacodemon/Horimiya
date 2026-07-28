using System;

namespace Horimiya.Auth
{
    /// <summary>
    /// Represents the result returned by the authentication API.
    /// </summary>
    public class AuthResult
    {
        /// <summary>Whether authentication was successful.</summary>
        public bool Success { get; set; }

        /// <summary>Display name of the authenticated user.</summary>
        public string Username { get; set; } = "";

        /// <summary>License type: monthly, quarterly, biannual, yearly, lifetime, trial.</summary>
        public string LicenseType { get; set; } = "";

        /// <summary>ISO date string of expiry, e.g. "2026-12-31". Empty if lifetime.</summary>
        public string ExpiresAt { get; set; } = "";

        /// <summary>Human-readable status message (shown in LoginForm).</summary>
        public string Message { get; set; } = "";

        /// <summary>True if this was the first auth and the HWID was just bound.</summary>
        public bool HwidBound { get; set; }

        /// <summary>True si la licencia nunca expira (lifetime o perma).</summary>
        public bool IsLifetime => string.Equals(LicenseType, "lifetime", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(LicenseType, "perma",    StringComparison.OrdinalIgnoreCase);

        /// <summary>Retorna una etiqueta formateada de expiración para mostrar en la UI.</summary>
        public string ExpiryDisplay
        {
            get
            {
                if (IsLifetime) return "Lifetime";
                if (string.IsNullOrEmpty(ExpiresAt)) return "Unknown";
                if (DateTime.TryParse(ExpiresAt, out var dt))
                    return dt.ToString("MMM dd, yyyy");
                return ExpiresAt;
            }
        }

        /// <summary>Retorna una etiqueta amigable del tipo de licencia.</summary>
        public string LicenseTypeDisplay
        {
            get
            {
                switch (LicenseType?.ToLowerInvariant())
                {
                    case "lifetime":  return "∞ Lifetime";
                    case "perma":     return "∞ Permanente";
                    case "30d":       return "30 Días";
                    case "14d":       return "14 Días";
                    case "7d":        return "7 Días";
                    case "trial":     return "Trial";
                    case "monthly":   return "Mensual (30d)";
                    case "quarterly": return "Trimestral (90d)";
                    case "biannual":  return "6 Meses";
                    case "yearly":    return "Anual";
                    default:          return LicenseType ?? "Desconocido";
                }
            }
        }
    }
}
