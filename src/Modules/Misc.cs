using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using lospoderosos_lite.Config;
using lospoderosos_lite.Utils;

namespace lospoderosos_lite.Modules
{
    [Injectable(true)]
    public class Misc
    {
        private readonly AppConfig _cfg;
        private readonly Clicker _clicker;

        // Discord RPC state
        private Thread _rpcThread;
        private volatile bool _rpcRun = false;
        private volatile bool _rpcOk = false;
        private readonly DateTime _t0 = DateTime.UtcNow;

        // Hotkey polling state
        private Thread _hotkeyThread;
        private volatile bool _hotkeyRun = false;

        // Discord App ID
        public string AppId
        {
            get { return _cfg.DiscordAppId; }
        }

        public event Action<bool> RpcStatusChanged;
        public event Action ClickBindTriggered;
        public event Action RightClickBindTriggered;
        public event Action HideBindTriggered;
        public event Action DestructBindTriggered;
        public event Action<string> ProfileSwitchTriggered;

        public Misc(AppConfig cfg, Clicker clicker)
        {
            _cfg = cfg;
            _clicker = clicker;
        }

        public void Start()
        {
            if (_cfg.DiscordRpc)
            {
                StartRpc();
            }

            StartHotkeyListener();
        }

        public void OptimizePC()
        {
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c color a & echo Optimizando sistema para Los Poderosisimos... & powercfg -s 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c & ipconfig /flushdns & netsh int tcp set global autotuninglevel=normal & netsh int tcp set global congestionprovider=ctcp & echo. & echo ================================= & echo Optimizacion P+ Completada al 100%%! & echo ================================= & pause",
                    CreateNoWindow = false,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        public void Stop()
        {
            StopRpc();
            StopHotkeyListener();
        }



        // ── Hotkey Polling ───────────────────────────────────────────────────
        private void StartHotkeyListener()
        {
            if (_hotkeyRun) return;
            _hotkeyRun = true;
            _hotkeyThread = new Thread(HotkeyLoop) { IsBackground = true };
            _hotkeyThread.Start();
        }

        private void StopHotkeyListener()
        {
            _hotkeyRun = false;
        }

        private void HotkeyLoop()
        {
            bool clickKeyWasDown = false;
            bool rightClickKeyWasDown = false;
            bool hideKeyWasDown = false;
            bool destructKeyWasDown = false;
            bool switchKeyWasDown = false;

            // Built-in profiles for the switcher
            string[] profileNames    = { "Aggressive", "Legit" };
            double[] profileCps      = { 20.0, 12.0 };
            int[]    profileRandMode = { 2, 0 }; // NoDelay, Jitter

            while (_hotkeyRun)
            {
                // Check Click Bind
                int clickBind = _cfg.ClickBind;
                if (clickBind > 0)
                {
                    bool isDown = (Win32.GetAsyncKeyState(clickBind) & 0x8000) != 0;
                    if (isDown && !clickKeyWasDown)
                    {
                        if (ClickBindTriggered != null)
                        {
                            ClickBindTriggered();
                        }
                    }
                    clickKeyWasDown = isDown;
                }
                else
                {
                    clickKeyWasDown = false;
                }

                // Check Right Click Bind
                int rightClickBind = _cfg.RightBind;
                if (rightClickBind > 0)
                {
                    bool isDown = (Win32.GetAsyncKeyState(rightClickBind) & 0x8000) != 0;
                    if (isDown && !rightClickKeyWasDown)
                    {
                        if (RightClickBindTriggered != null)
                        {
                            RightClickBindTriggered();
                        }
                    }
                    rightClickKeyWasDown = isDown;
                }
                else
                {
                    rightClickKeyWasDown = false;
                }

                // Check Hide Bind
                int hideBind = _cfg.HideBind;
                if (hideBind > 0)
                {
                    bool isDown = (Win32.GetAsyncKeyState(hideBind) & 0x8000) != 0;
                    if (isDown && !hideKeyWasDown)
                    {
                        if (HideBindTriggered != null)
                        {
                            HideBindTriggered();
                        }
                    }
                    hideKeyWasDown = isDown;
                }
                else
                {
                    hideKeyWasDown = false;
                }

                // Check Destruct Bind
                int destructBind = _cfg.DestructBind;
                if (destructBind > 0)
                {
                    bool isDown = (Win32.GetAsyncKeyState(destructBind) & 0x8000) != 0;
                    if (isDown && !destructKeyWasDown)
                    {
                        if (DestructBindTriggered != null)
                        {
                            DestructBindTriggered();
                        }
                    }
                    destructKeyWasDown = isDown;
                }
                else
                {
                    destructKeyWasDown = false;
                }

                // Check Profile Switch Bind
                int switchBind = _cfg.ProfileSwitchBind;
                if (_cfg.AutoSwitchProfiles && switchBind > 0)
                {
                    bool isDown = (Win32.GetAsyncKeyState(switchBind) & 0x8000) != 0;
                    if (isDown && !switchKeyWasDown)
                    {
                        _cfg.CurrentProfileIndex = (_cfg.CurrentProfileIndex + 1) % profileNames.Length;
                        int idx = _cfg.CurrentProfileIndex;
                        _cfg.AverageCps  = profileCps[idx];
                        _cfg.RandMode    = profileRandMode[idx];
                        _cfg.Save();
                        string notifMsg = "Profile: " + profileNames[idx] + " (" + profileCps[idx].ToString("F0") + " CPS)";
                        lospoderosos_lite.UI.NotificationOverlay.Show(
                            "lospoderosisimos", notifMsg,
                            lospoderosos_lite.UI.NotificationOverlay.NotificationType.Success,
                            _cfg.NotificationPosition);
                        if (ProfileSwitchTriggered != null) ProfileSwitchTriggered(notifMsg);
                    }
                    switchKeyWasDown = isDown;
                }
                else
                {
                    switchKeyWasDown = false;
                }

                Thread.Sleep(15); // Poll frequency
            }
        }

        // ── Discord RPC (Named Pipes) ─────────────────────────────────────────
        public void StartRpc()
        {
            if (_rpcRun) return;
            _rpcRun = true;
            _rpcThread = new Thread(RpcLoop) { IsBackground = true };
            _rpcThread.Start();
        }

        public void StopRpc()
        {
            _rpcRun = false;
            _rpcOk = false;
            TriggerRpcStatus(false);
        }

        private void RpcLoop()
        {
            while (_rpcRun)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", "discord-ipc-0", PipeDirection.InOut))
                    {
                        pipe.Connect(3000);
                        _rpcOk = true;
                        TriggerRpcStatus(true);

                        // Handshake
                        SendFrame(pipe, 0, "{\"v\":1,\"client_id\":\"" + AppId + "\"}");
                        ReadFrame(pipe);

                        SendActivity(pipe);

                        var activityTimer = Stopwatch.StartNew();
                        while (_rpcRun && pipe.IsConnected)
                        {
                            if (activityTimer.ElapsedMilliseconds > 8000)
                            {
                                SendActivity(pipe);
                                activityTimer.Restart();
                            }
                            Thread.Sleep(500);
                        }
                    }
                }
                catch
                {
                    // Fail silently and retry connection
                }

                _rpcOk = false;
                TriggerRpcStatus(false);
                if (_rpcRun)
                {
                    Thread.Sleep(5000);
                }
            }
        }

        private void SendActivity(NamedPipeClientStream pipe)
        {
            string clickState = _clicker.Clicking
                ? (_cfg.RandMode == 0 ? "Jitter Mode" : _cfg.RandMode == 1 ? "Butterfly Mode" : "NoDelay Mode")
                : "Idle";

            long uptimeSeconds = (long)(_t0 - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

            string json =
                "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + Process.GetCurrentProcess().Id +
                ",\"activity\":{" +
                "\"details\":\"Los poderosisimos v3.0.0\"," +
                "\"state\":\"no te compares, somos los poderosisimos.\"," +
                "\"timestamps\":{\"start\":" + uptimeSeconds + "}," +
                "\"assets\":{" +
                    "\"large_image\":\"logo\"," +
                    "\"large_text\":\"Status: " + clickState + "\"" +
                "}" +
                "}},\"nonce\":\"lp_rpc\"}";

            try
            {
                SendFrame(pipe, 1, json);
                ReadFrame(pipe);
            }
            catch
            {
                // Connection broken
            }
        }

        private void SendFrame(NamedPipeClientStream pipe, int op, string data)
        {
            byte[] payload = Encoding.UTF8.GetBytes(data);
            byte[] header = new byte[8];

            // OP Code
            header[0] = (byte)(op & 0xFF);
            header[1] = (byte)((op >> 8) & 0xFF);
            header[2] = (byte)((op >> 16) & 0xFF);
            header[3] = (byte)((op >> 24) & 0xFF);

            // Payload Length
            int len = payload.Length;
            header[4] = (byte)(len & 0xFF);
            header[5] = (byte)((len >> 8) & 0xFF);
            header[6] = (byte)((len >> 16) & 0xFF);
            header[7] = (byte)((len >> 24) & 0xFF);

            pipe.Write(header, 0, 8);
            pipe.Write(payload, 0, payload.Length);
            pipe.Flush();
        }

        private void ReadFrame(NamedPipeClientStream pipe)
        {
            byte[] header = new byte[8];
            int read = 0;
            while (read < 8)
            {
                read += pipe.Read(header, read, 8 - read);
            }

            int len = header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24);
            if (len > 0 && len < 65536)
            {
                byte[] buffer = new byte[len];
                read = 0;
                while (read < len)
                {
                    read += pipe.Read(buffer, read, len - read);
                }
            }
        }

        private void TriggerRpcStatus(bool ok)
        {
            if (RpcStatusChanged != null)
            {
                RpcStatusChanged(ok);
            }
        }


    }
}
