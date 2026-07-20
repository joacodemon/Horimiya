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

        /// <summary>True if the license never expires.</summary>
        public bool IsLifetime => string.Equals(LicenseType, "lifetime", StringComparison.OrdinalIgnoreCase);

        /// <summary>Returns a formatted expiry label for display in the UI.</summary>
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

        /// <summary>Returns a friendly license type label.</summary>
        public string LicenseTypeDisplay
        {
            get
            {
                switch (LicenseType?.ToLowerInvariant())
                {
                    case "trial":     return "Trial (3 days)";
                    case "monthly":   return "Monthly";
                    case "quarterly": return "Quarterly";
                    case "biannual":  return "6 Months";
                    case "yearly":    return "Yearly";
                    case "lifetime":  return "Lifetime ∞";
                    default:          return LicenseType ?? "Unknown";
                }
            }
        }
    }
}
