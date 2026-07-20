using System;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using Horimiya.Auth;
using Horimiya.Config;
using Horimiya.Modules;
using Horimiya.UI;
using Horimiya.Utils;

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

            // ── Authentication ────────────────────────────────────────────────
            var cfg = AppConfig.Load("default");
            bool autoAuthed = false;

            // Try reading license key from dedicated file (next to the exe)
            string licenseFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "license.key");
            if (File.Exists(licenseFilePath))
            {
                string savedKey = File.ReadAllText(licenseFilePath).Trim();
                if (!string.IsNullOrEmpty(savedKey))
                {
                    cfg.LicenseKey = savedKey; // sync into config
                }
            }

            if (!string.IsNullOrEmpty(cfg.LicenseKey))
            {
                var result = Horimiya.Auth.AuthManager.Authenticate(cfg.LicenseKey);
                if (result.Success)
                {
                    autoAuthed = true;
                }
            }

            if (!autoAuthed)
            {
                using (var loginForm = new LoginForm())
                {
                    var loginResult = loginForm.ShowDialog();
                    if (loginResult != DialogResult.OK)
                    {
                        // User closed the window or auth failed — do not launch
                        return;
                    }
                }
                // Reload config so the LicenseKey saved by LoginForm is now in memory
                cfg = AppConfig.Load("default");
                // Also sync from license.key file if it was just created
                if (File.Exists(licenseFilePath))
                {
                    string savedKey = File.ReadAllText(licenseFilePath).Trim();
                    if (!string.IsNullOrEmpty(savedKey)) cfg.LicenseKey = savedKey;
                }
            }
            // ─────────────────────────────────────────────────────────────────

            // Setup Dependency Injection Container
            var container = new DependencyContainer();

            container.RegisterSingleton<AppConfig>(cfg);

            // Initialize Modules using DI Container
            var clicker = container.Resolve<Clicker>();
            container.RegisterSingleton<Clicker>(clicker);

            var rightClicker = container.Resolve<RightClicker>();
            container.RegisterSingleton<RightClicker>(rightClicker);

            var recorder = container.Resolve<Recorder>();
            container.RegisterSingleton<Recorder>(recorder);

            var misc = container.Resolve<Misc>();
            container.RegisterSingleton<Misc>(misc);

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
            
            Console.Title = "Horimiya - Authenticating";
            Console.CursorVisible = false;
            
            // Render ASCII
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine("");
            Console.WriteLine(@"    _   _            _           _              ");
            Console.WriteLine(@"   | | | | ___  _ __(_)_ __ ___ (_)_   _  __ _   ");
            Console.WriteLine(@"   | |_| |/ _ \| '__| | '_ ` _ \| | | | |/ _` |  ");
            Console.WriteLine(@"   |  _  | (_) | |  | | | | | | | | |_| | (_| |  ");
            Console.WriteLine(@"   |_| |_|\___/|_|  |_|_| |_| |_|_|\__, |\__,_|  ");
            Console.WriteLine(@"                                   |___/         ");
            
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("                                           1.0.1\n\n");
            
            Console.WriteLine("Authenticating...");
            Console.WriteLine("");

            // Check for updates automatically
            Updater.CheckForUpdates("1.0.1");
            Console.WriteLine("");
            
            int totalBlocks = 30;
            Console.Write(" ");
            for (int i = 0; i < totalBlocks; i++)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
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

            // Run ImGui form via DI
            container.RegisterTransient<ImGuiForm>();
            var form = container.Resolve<ImGuiForm>();
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
