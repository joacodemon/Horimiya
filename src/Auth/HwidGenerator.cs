using System;
using System.Management;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace Horimiya.Auth
{
    /// <summary>
    /// Generates a unique hardware fingerprint for this machine.
    /// Uses CPU ID, disk serial, MAC address, and motherboard serial,
    /// then SHA-256 hashes the result into a formatted HWID string.
    /// </summary>
    public static class HwidGenerator
    {
        private static string _cached = null;

        /// <summary>
        /// Returns the HWID for this machine in the format XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX.
        /// Result is cached after the first call.
        /// </summary>
        public static string GetHwid()
        {
            if (_cached != null) return _cached;

            var sb = new StringBuilder();

            // 1. CPU Processor ID
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string val = obj["ProcessorId"]?.ToString() ?? "";
                        sb.Append(val.Trim());
                        break;
                    }
                }
            }
            catch { }

            // 2. Primary disk serial number
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string val = obj["SerialNumber"]?.ToString() ?? "";
                        sb.Append(val.Trim());
                        break;
                    }
                }
            }
            catch { }

            // 3. First active physical MAC address
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        nic.OperationalStatus == OperationalStatus.Up &&
                        nic.GetPhysicalAddress().ToString().Length > 0)
                    {
                        sb.Append(nic.GetPhysicalAddress().ToString());
                        break;
                    }
                }
            }
            catch { }

            // 4. Motherboard serial
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string val = obj["SerialNumber"]?.ToString() ?? "";
                        sb.Append(val.Trim());
                        break;
                    }
                }
            }
            catch { }

            // Fallback in case WMI fails entirely
            string raw = sb.ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = Environment.MachineName + Environment.UserName + Environment.OSVersion.ToString();
            }

            // SHA-256 hash → format as XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                string hex = BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                _cached = $"{hex.Substring(0, 8)}-{hex.Substring(8, 8)}-{hex.Substring(16, 8)}-{hex.Substring(24, 8)}";
            }

            return _cached;
        }
    }
}
