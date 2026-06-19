using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace lospoderosos_lite.Config
{
    public class PresetConfig
    {
        public string Name      = "";
        public string Server    = "";
        public double Cps = 15.0;
        public int    RandMode  = 2;    // 0=Jitter, 1=Butterfly, 2=NoDelay, 3=Custom
        public bool   IsBuiltIn = false;
        public double[] CustomCpsWeights = null; // Only used when RandMode == 3

        public string RandModeName()
        {
            if (RandMode == 0) return "jitter";
            if (RandMode == 1) return "butterfly";
            if (RandMode == 2) return "nodelay";
            return "custom";
        }
    }

    public class AppConfig
    {
        // Singleton instance
        public static AppConfig Instance { get; private set; }

        // Configuration fields
        public double AverageCps = 15.0;
        public int Mode = 0; // 0=Hold, 1=Toggle, 2=Always
        public int BBMode = 1; // 0=Off, 1=Full, 2=Sneak
        public bool OnlyInGame = true;
        public bool RmbLock = true; // Bloquear LMB cuando RMB está presionado para evitar BlockHit automático
        public bool WorkInMenus = true;

        public bool DiscordRpc = true;
        public string DiscordAppId = "1234567890123456789";
        public bool DetectCheatBreaker = true;
        public string Sound = "None";
        public int ClickBind = 0;
        public int HideBind = 0;
        public int RandMode = 0; // 0=Jitter, 1=Butterfly, 2=NoDelay, 3=Manual
        public bool ForceExactCps = false; // When true, clicker uses exact AverageCps without jitter

        public double RightAverageCps = 15.0;
        public int RightMode = 0;
        public int RightBind = 0;
        public int RightRandMode = 0;
        
        public string CloudPresetsUrl = "";
        public MqttSettings Mqtt = new MqttSettings(); // MQTT configuration
        public double[] CustomCpsWeights = new double[25]; // CPS 1-25 weights
        public int ColorAccent = Color.FromArgb(0, 180, 255).ToArgb();
        public bool ParticleEnabled = true;
        public bool RefillMode = false;
        public int NotificationPosition = 0; // 0=Bottom Left, 1=Bottom Right, 2=Top Left, 3=Top Right


        public bool FlushDns = false;
        public bool HideTaskbar = false;
        public bool StreamerMode = false;
        public int DestructBind = 0;
        public bool WTapEnabled = false;
        public double PingMs = 0.0; // Latency compensation (0-200ms)
        public List<PresetConfig> Presets = new List<PresetConfig>();

        public AppConfig()
        {
            Instance = this;
            // Default built-in presets
            Presets.Add(new PresetConfig { Name = "elevatemc.com / cavepvp.com",  Server = "elevatemc.com / cavepvp.com",  Cps = 16.7, RandMode = 2, IsBuiltIn = true });
            Presets.Add(new PresetConfig { Name = "minemen.club",   Server = "minemen.club",   Cps = 20.0, RandMode = 2, IsBuiltIn = true });
        }

        public void Save()
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "default.json"), this.ToJson());
            } catch { }
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine(string.Format("  \"AverageCps\": {0} ,", AverageCps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)));
            sb.AppendLine(string.Format("  \"Mode\": {0},", Mode));
            sb.AppendLine(string.Format("  \"BBMode\": {0},", BBMode));
            sb.AppendLine(string.Format("  \"OnlyInGame\": {0},", OnlyInGame ? "true" : "false"));
            sb.AppendLine(string.Format("  \"RmbLock\": {0},", RmbLock ? "true" : "false"));
            sb.AppendLine(string.Format("  \"WorkInMenus\": {0},", WorkInMenus ? "true" : "false"));
            sb.AppendLine(string.Format("  \"DiscordRpc\": {0},", DiscordRpc ? "true" : "false"));
            sb.AppendLine(string.Format("  \"DiscordAppId\": \"{0}\",", DiscordAppId));

            sb.AppendLine(string.Format("  \"Sound\": \"{0}\",", Sound));
            sb.AppendLine(string.Format("  \"ClickBind\": {0},", ClickBind));
            sb.AppendLine(string.Format("  \"HideBind\": {0},", HideBind));
            sb.AppendLine(string.Format("  \"RandMode\": {0},", RandMode));
            
            sb.AppendLine(string.Format("  \"RightAverageCps\": {0} ,", RightAverageCps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)));
            sb.AppendLine(string.Format("  \"RightMode\": {0},", RightMode));
            sb.AppendLine(string.Format("  \"RightBind\": {0},", RightBind));
            sb.AppendLine(string.Format("  \"RightRandMode\": {0},", RightRandMode));
            sb.AppendLine(string.Format("  \"CloudPresetsUrl\": \"{0}\",", CloudPresetsUrl));
            sb.AppendLine(string.Format("  \"Mqtt\": {{\"Host\": \"{0}\", \"Port\": {1}, \"Username\": \"{2}\", \"Password\": \"{3}\", \"PublishTopic\": \"{4}\", \"SubscribeTopic\": \"{5}\", \"UseTls\": {6}, \"QoS\": {7}}},", Mqtt.Host, Mqtt.Port, Mqtt.Username, Mqtt.Password, Mqtt.PublishTopic, Mqtt.SubscribeTopic, Mqtt.UseTls ? "true" : "false", Mqtt.QoS));
            sb.AppendLine(string.Format("  \"ColorAccent\": {0},", ColorAccent));
            sb.AppendLine(string.Format("  \"ParticleEnabled\": {0},", ParticleEnabled ? "true" : "false"));
            sb.AppendLine(string.Format("  \"RefillMode\": {0},", RefillMode ? "true" : "false"));
            sb.AppendLine(string.Format("  \"NotificationPosition\": {0},", NotificationPosition));


            sb.AppendLine(string.Format("  \"FlushDns\": {0},", FlushDns ? "true" : "false"));
            sb.AppendLine(string.Format("  \"HideTaskbar\": {0},", HideTaskbar ? "true" : "false"));
            sb.AppendLine(string.Format("  \"StreamerMode\": {0},", StreamerMode ? "true" : "false"));
            sb.AppendLine(string.Format("  \"DestructBind\": {0},", DestructBind));
            sb.AppendLine(string.Format("  \"WTapEnabled\": {0},", WTapEnabled ? "true" : "false"));
            sb.AppendLine(string.Format("  \"PingMs\": {0},", PingMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)));

            sb.Append("  \"CustomCpsWeights\": [");
            for (int i = 0; i < CustomCpsWeights.Length; i++)
            {
                sb.Append(CustomCpsWeights[i].ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                if (i < CustomCpsWeights.Length - 1) sb.Append(", ");
            }
            sb.AppendLine("],");

            sb.AppendLine("  \"UserPresets\": [");
            bool first = true;
            foreach (var pr in Presets)
            {
                if (pr.IsBuiltIn) continue;
                sb.Append(string.Format("    {{\"Name\":\"{0}\",\"Server\":\"{1}\",\"Cps\":{2},\"RandMode\":{3}", pr.Name.Replace("\"", "\\\""), pr.Server.Replace("\"", "\\\""), pr.Cps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), pr.RandMode));
                if (pr.CustomCpsWeights != null && pr.CustomCpsWeights.Length > 0)
                {
                    sb.Append(",\"CustomCpsWeights\":[");
                    for (int i = 0; i < pr.CustomCpsWeights.Length; i++)
                    {
                        sb.Append(pr.CustomCpsWeights[i].ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                        if (i < pr.CustomCpsWeights.Length - 1) sb.Append(",");
                    }
                    sb.Append("]");
                }
                sb.Append("}");
                first = false;
            }
            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static AppConfig FromJson(string json)
        {
            var cfg = new AppConfig();
            cfg.AverageCps = GetDouble(json, "AverageCps", cfg.AverageCps);
            cfg.Mode = GetInt(json, "Mode", cfg.Mode);
            cfg.BBMode = GetInt(json, "BBMode", cfg.BBMode);
            cfg.OnlyInGame = GetBool(json, "OnlyInGame", cfg.OnlyInGame);
            cfg.RmbLock = GetBool(json, "RmbLock", cfg.RmbLock);
            cfg.WorkInMenus = GetBool(json, "WorkInMenus", cfg.WorkInMenus);
            cfg.DiscordRpc = GetBool(json, "DiscordRpc", cfg.DiscordRpc);
            cfg.DiscordAppId = GetString(json, "DiscordAppId", cfg.DiscordAppId);

            cfg.Sound = GetString(json, "Sound", cfg.Sound);
            cfg.ClickBind = GetInt(json, "ClickBind", cfg.ClickBind);
            cfg.HideBind = GetInt(json, "HideBind", cfg.HideBind);
            cfg.RandMode = GetInt(json, "RandMode", cfg.RandMode);
            
            cfg.RightAverageCps = GetDouble(json, "RightAverageCps", cfg.RightAverageCps);
            cfg.RightMode = GetInt(json, "RightMode", cfg.RightMode);
            cfg.RightBind = GetInt(json, "RightBind", cfg.RightBind);
            cfg.RightRandMode = GetInt(json, "RightRandMode", cfg.RightRandMode);
            cfg.CloudPresetsUrl = GetString(json, "CloudPresetsUrl", cfg.CloudPresetsUrl);
            cfg.Mqtt = GetMqttSettings(json);

            cfg.ColorAccent = GetInt(json, "ColorAccent", cfg.ColorAccent);
            cfg.ParticleEnabled = GetBool(json, "ParticleEnabled", cfg.ParticleEnabled);
            cfg.RefillMode = GetBool(json, "RefillMode", cfg.RefillMode);
            cfg.NotificationPosition = GetInt(json, "NotificationPosition", cfg.NotificationPosition);


            cfg.FlushDns = GetBool(json, "FlushDns", cfg.FlushDns);
            cfg.HideTaskbar = GetBool(json, "HideTaskbar", cfg.HideTaskbar);
            cfg.StreamerMode = GetBool(json, "StreamerMode", cfg.StreamerMode);
            cfg.DestructBind = GetInt(json, "DestructBind", cfg.DestructBind);
            cfg.WTapEnabled = GetBool(json, "WTapEnabled", cfg.WTapEnabled);
            cfg.PingMs = Math.Max(0, Math.Min(200, GetDouble(json, "PingMs", cfg.PingMs)));

            double[] loadedWeights = GetDoubleArray(json, "CustomCpsWeights");
            if (loadedWeights != null && loadedWeights.Length == 25)
                cfg.CustomCpsWeights = loadedWeights;
            else if (loadedWeights != null)
                for (int i = 0; i < Math.Min(25, loadedWeights.Length); i++)
                    cfg.CustomCpsWeights[i] = loadedWeights[i];

            ParseUserPresets(json, cfg.Presets);
            return cfg;
        }

        private static double GetDouble(string json, string key, double def)
        {
            string s = "\"" + key + "\":";
            int idx = json.IndexOf(s);
            if (idx < 0) return def;
            int start = idx + s.Length;
            int end = FindValueEnd(json, start);
            double val;
            string raw = json.Substring(start, end - start).Trim();
            return double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val) ? val : def;
        }

        private static int GetInt(string json, string key, int def)
        {
            string s = "\"" + key + "\":";
            int idx = json.IndexOf(s);
            if (idx < 0) return def;
            int start = idx + s.Length;
            int end = FindValueEnd(json, start);
            int val;
            string raw = json.Substring(start, end - start).Trim();
            return int.TryParse(raw, out val) ? val : def;
        }

        private static bool GetBool(string json, string key, bool def)
        {
            string s = "\"" + key + "\":";
            int idx = json.IndexOf(s);
            if (idx < 0) return def;
            int start = idx + s.Length;
            int end = FindValueEnd(json, start);
            string raw = json.Substring(start, end - start).Trim();
            return raw.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetString(string json, string key, string def)
        {
            string s = "\"" + key + "\":\"";
            int idx = json.IndexOf(s);
            if (idx < 0) return def;
            int start = idx + s.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return def;
            return json.Substring(start, end - start);
        }

        private static double[] GetDoubleArray(string json, string key)
        {
            string s = "\"" + key + "\":[";
            int idx = json.IndexOf(s);
            if (idx < 0) return null;
            int start = idx + s.Length;
            int end = json.IndexOf(']', start);
            if (end < 0) return null;
            var parts = json.Substring(start, end - start).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var arr = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out arr[i]);
            return arr;
        }

        private static int FindValueEnd(string json, int start)
        {
            int idxComma = json.IndexOf(',', start);
            int idxBrace = json.IndexOf('}', start);
            if (idxComma < 0) return idxBrace;
            if (idxBrace < 0) return idxComma;
            return Math.Min(idxComma, idxBrace);
        }

        private static MqttSettings GetMqttSettings(string json)
        {
            var settings = new MqttSettings();
            settings.Host = GetString(json, "Mqtt.Host", "test.mosquitto.org");
            settings.Port = GetInt(json, "Mqtt.Port", 1883);
            settings.Username = GetString(json, "Mqtt.Username", "");
            settings.Password = GetString(json, "Mqtt.Password", "");
            settings.PublishTopic = GetString(json, "Mqtt.PublishTopic", "lospoderosos/commands");
            settings.SubscribeTopic = GetString(json, "Mqtt.SubscribeTopic", "lospoderosos/status");
            settings.UseTls = GetBool(json, "Mqtt.UseTls", false);
            settings.QoS = GetInt(json, "Mqtt.QoS", 0);
            return settings;
        }

        private static void ParseUserPresets(string json, List<PresetConfig> list)
        {
            string marker = "\"UserPresets\":";
            int idx = json.IndexOf(marker);
            if (idx < 0) return;
            int arrStart = json.IndexOf('[', idx);
            if (arrStart < 0) return;
            int arrEnd = json.IndexOf(']', arrStart);
            if (arrEnd < 0) return;
            string arr = json.Substring(arrStart + 1, arrEnd - arrStart - 1).Trim();
            if (arr.Length == 0) return;
            int pos = 0;
            while (pos < arr.Length)
            {
                int ob = arr.IndexOf('{', pos);
                if (ob < 0) break;
                int cb = arr.IndexOf('}', ob);
                if (cb < 0) break;
                string obj = arr.Substring(ob, cb - ob + 1);
                string name = GetStringFromObj(obj, "Name");
                string server = GetStringFromObj(obj, "Server");
                double cps = GetDoubleFromObj(obj, "Cps", 13.0);
                // UI initialization moved to a dedicated method. Existing stray code removed.
                int rand = GetIntFromObj(obj, "RandMode", 2);
                double[] customWeights = GetDoubleArrayFromObj(obj, "CustomCpsWeights");
                if (!string.IsNullOrEmpty(name))
                    list.Add(new PresetConfig { Name = name, Server = server, Cps = cps, RandMode = rand, IsBuiltIn = false, CustomCpsWeights = customWeights });
                pos = cb + 1;
            }
        }

        private static string GetStringFromObj(string obj, string key)
        {
            string s = "\"" + key + "\":\"";
            int idx = obj.IndexOf(s);
            if (idx < 0) return "";
            int start = idx + s.Length;
            int end = obj.IndexOf('"', start);
            if (end < 0) return "";
            return obj.Substring(start, end - start);
        }

        private static double GetDoubleFromObj(string obj, string key, double def)
        {
            string s = "\"" + key + "\":";
            int idx = obj.IndexOf(s);
            if (idx < 0) return def;
            int start = idx + s.Length;
            int end = obj.IndexOf(',', start);
            if (end < 0) end = obj.IndexOf('}', start);
            if (end < 0) return def;
            double val;
            string raw = obj.Substring(start, end - start).Trim();
            return double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val) ? val : def;
        }

        private static int GetIntFromObj(string obj, string key, int def)
        {
            string s = "\"" + key + "\":";
            int idx = obj.IndexOf(s);
            if (idx < 0) return def;
            int start = idx + s.Length;
            int end = obj.IndexOf(',', start);
            if (end < 0) end = obj.IndexOf('}', start);
            if (end < 0) return def;
            int val;
            string raw = obj.Substring(start, end - start).Trim();
            return int.TryParse(raw, out val) ? val : def;
        }

        private static double[] GetDoubleArrayFromObj(string obj, string key)
        {
            string s = "\"" + key + "\":[";
            int idx = obj.IndexOf(s);
            if (idx < 0) return null;
            int start = idx + s.Length;
            int end = obj.IndexOf(']', start);
            if (end < 0) return null;
            var parts = obj.Substring(start, end - start).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var arr = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out arr[i]);
            return arr;
        }
    }
}
