using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Horimiya.Config;
using Horimiya.Utils;

namespace Horimiya.Modules
{
    public class MacroEv
    {
        public bool Down;
        public long Ms;
        public int Button; // 1 = Left, 2 = Right

        public MacroEv(bool down, long ms, int button)
        {
            Down = down;
            Ms = ms;
            Button = button;
        }
    }

    [Injectable(true)]
    public class Recorder
    {
        private List<MacroEv> _events = new List<MacroEv>();
        private Thread _recThread;
        private Thread _playThread;
        private Stopwatch _stopwatch = new Stopwatch();

        public volatile bool IsRecording = false;
        public volatile bool IsPlaying = false;
        public double PlaybackSpeed = 1.0;
        public bool LoopPlayback = false;

        public event Action<string> StatusChanged;

        public int EventCount
        {
            get
            {
                lock (_events)
                {
                    return _events.Count;
                }
            }
        }

        public void StartRecord()
        {
            if (IsRecording || IsPlaying) return;

            lock (_events)
            {
                _events.Clear();
            }

            IsRecording = true;
            _stopwatch.Restart();
            TriggerStatus("Status: Recording...");

            _recThread = new Thread(RecordLoop) { IsBackground = true };
            _recThread.Start();
        }

        public void Stop()
        {
            IsRecording = false;
            IsPlaying = false;
            TriggerStatus("Status: Idle | Events: " + EventCount);
        }

        public void StartPlay()
        {
            if (IsRecording || IsPlaying) return;

            List<MacroEv> snap;
            lock (_events)
            {
                snap = new List<MacroEv>(_events);
            }

            if (snap.Count == 0) return;

            IsPlaying = true;
            TriggerStatus("Status: Playing | Events: " + snap.Count);

            _playThread = new Thread(() => PlayLoop(snap)) { IsBackground = true };
            _playThread.Start();
        }

        private void RecordLoop()
        {
            bool wasLmbDown = false;
            bool wasRmbDown = false;

            while (IsRecording)
            {
                bool isLmbDown = Win32.IsLeftDown;
                bool isRmbDown = Win32.IsRightDown;

                long ms = _stopwatch.ElapsedMilliseconds;

                if (isLmbDown != wasLmbDown)
                {
                    lock (_events)
                    {
                        _events.Add(new MacroEv(isLmbDown, ms, 1));
                    }
                    wasLmbDown = isLmbDown;
                }

                if (isRmbDown != wasRmbDown)
                {
                    lock (_events)
                    {
                        _events.Add(new MacroEv(isRmbDown, ms, 2));
                    }
                    wasRmbDown = isRmbDown;
                }

                Thread.Sleep(2);
            }
        }

        private void PlayLoop(List<MacroEv> eventsList)
        {
            do
            {
                var playSw = Stopwatch.StartNew();
                foreach (var ev in eventsList)
                {
                    if (!IsPlaying) break;

                    long targetMs = (long)(ev.Ms / PlaybackSpeed);
                    while (playSw.ElapsedMilliseconds < targetMs)
                    {
                        Thread.Sleep(1);
                    }

                    // Perform mouse event based on recorded button
                    uint flags = 0;
                    if (ev.Button == 1) // LMB
                    {
                        flags = ev.Down ? 2u : 4u; // LD = 2, LU = 4
                    }
                    else if (ev.Button == 2) // RMB
                    {
                        flags = ev.Down ? 8u : 16u; // RD = 8, RU = 16
                    }

                    if (flags != 0)
                    {
                        Win32.mouse_event(flags, 0, 0, 0, 0);
                    }
                }
            } while (LoopPlayback && IsPlaying);

            IsPlaying = false;
            TriggerStatus("Status: Idle | Events: " + EventCount);
        }

        public void SaveMacro(string name)
        {
            try
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, name + "_macro.txt");

                List<MacroEv> snap;
                lock (_events)
                {
                    snap = new List<MacroEv>(_events);
                }

                using (var writer = new StreamWriter(path))
                {
                    foreach (var ev in snap)
                    {
                        writer.WriteLine(string.Format("{0},{1},{2}", ev.Down ? "1" : "0", ev.Ms, ev.Button));
                    }
                }
                TriggerStatus("Saved macro '" + name + "'");
            }
            catch (Exception ex)
            {
                TriggerStatus("Error saving macro: " + ex.Message);
            }
        }

        public void LoadMacro(string name)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs", name + "_macro.txt");
                if (!File.Exists(path))
                {
                    TriggerStatus("Macro not found.");
                    return;
                }

                var loaded = new List<MacroEv>();
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split(',');
                    if (parts.Length < 2) continue;

                    bool down = parts[0] == "1";
                    long ms;
                    if (!long.TryParse(parts[1], out ms)) continue;

                    int button = 1;
                    if (parts.Length >= 3)
                    {
                        int.TryParse(parts[2], out button);
                    }

                    loaded.Add(new MacroEv(down, ms, button));
                }

                lock (_events)
                {
                    _events = loaded;
                }
                TriggerStatus("Loaded macro '" + name + "' (" + loaded.Count + " events)");
            }
            catch (Exception ex)
            {
                TriggerStatus("Error loading macro: " + ex.Message);
            }
        }

        private void TriggerStatus(string status)
        {
            if (StatusChanged != null)
            {
                StatusChanged(status);
            }
        }
    }
}
