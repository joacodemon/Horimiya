using System;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using lospoderosos_lite.Config;
using lospoderosos_lite.Modules;
using lospoderosos_lite.Utils;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Cleanup old updates
        try
        {
            string oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName + ".old");
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }
        catch { }

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Initialize Configuration
            var cfg = new AppConfig();

            // Load default configuration if it exists
            string defaultCfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs", "default.json");
            if (File.Exists(defaultCfgPath))
            {
                try
                {
                    string json = File.ReadAllText(defaultCfgPath);
                    cfg = AppConfig.FromJson(json);
                }
                catch { }
            }

            // Initialize Modules
            var clicker = new Clicker(cfg);
            var rightClicker = new RightClicker(cfg);
            var recorder = new Recorder();
            var misc = new Misc(cfg, clicker);

            // Start Modules
            Win32.timeBeginPeriod(1); // Set Windows timer resolution to 1ms to fix Thread.Sleep lag
            uint currentRes = 0;
            Win32.NtSetTimerResolution(5000, true, ref currentRes); // 0.5ms resolution via NTDLL
            
            clicker.Start();
            rightClicker.Start();
            misc.Start();
            Win32.StartMouseHook();

            // Console loading screen
            AllocConsole();
            IntPtr consoleHwnd = GetConsoleWindow();
            
            Console.Title = "Los Poderosos - Authenticating";
            Console.CursorVisible = false;
            
            // Render ASCII
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine("");
            Console.WriteLine(@"    __                               __                         ");
            Console.WriteLine(@"   / /___  _________  ____  ____  __/ /__  _________  _________  _____");
            Console.WriteLine(@"  / / __ \/ ___/ __ \/ __ \/ __ \/ __  / _ \/ ___/ __ \/ ___/ __ \/ ___/");
            Console.WriteLine(@" / / /_/ (__  ) /_/ / /_/ / /_/ / /_/ /  __/ /  / /_/ (__  ) /_/ (__  ) ");
            Console.WriteLine(@"/_/\____/____/ .___/\____/\____/\__,_/\___/_/   \____/____/\____/____/  ");
            Console.WriteLine(@"            /_/                                                 ");
            
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("                                           2.2.3\n\n");
            
            Console.WriteLine("Authenticating...");
            Console.WriteLine("");

            // Check for updates automatically
            Updater.CheckForUpdates("2.2.3");
            Console.WriteLine("");
            
            int totalBlocks = 30;
            Console.Write(" ");
            for (int i = 0; i < totalBlocks; i++)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write((char)9608); // Solid block
                Thread.Sleep(50); // Simula el tiempo de carga
            }
            Console.WriteLine("\n");
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Successfully authenticated.");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Starting the UI..._");
            Thread.Sleep(1000);
            
            FreeConsole();

            // Run ImGui form
            var form = new ImGuiForm(cfg, clicker, rightClicker, recorder, misc);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_imgui.log");
            File.WriteAllText(logPath, ex.ToString());
            MessageBox.Show("Crash: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
