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
    // ── Versión actual del ejecutable ─────────────────────────────────────
    public const string APP_VERSION = "1.0.3";
    // ─────────────────────────────────────────────────────────────────────

    [STAThread]
    static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            File.WriteAllText("crash.log", e.ExceptionObject.ToString());
        };
        Application.ThreadException += (s, e) => {
            File.WriteAllText("crash.log", e.Exception.ToString());
        };

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

            // ── Chequeo de actualizaciones (background, no bloquea el UI) ────
            new System.Threading.Thread(() =>
            {
                Updater.CheckForUpdates(APP_VERSION);
            }) { IsBackground = true }.Start();
            // ─────────────────────────────────────────────────────────────────

            // Setup Dependency Injection Container
            var container = new DependencyContainer();

            container.RegisterSingleton<AppConfig>(cfg);

            var clicker = container.Resolve<Clicker>();
            container.RegisterSingleton<Clicker>(clicker);

            var recorder = container.Resolve<Recorder>();
            container.RegisterSingleton<Recorder>(recorder);

            var misc = container.Resolve<Misc>();
            container.RegisterSingleton<Misc>(misc);

            // Start Modules
            Win32.timeBeginPeriod(1); // Set Windows timer resolution to 1ms to fix Thread.Sleep lag
            uint currentRes = 0;
            Win32.NtSetTimerResolution(5000, true, ref currentRes); // 0.5ms resolution via NTDLL
            
            clicker.Start();
            misc.Start();
            Win32.StartMouseHook();

            // Premium Console Boot Sequence
            try 
            {
                AllocConsole();
                var sw = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(sw);
                
                Console.Title = "Horimiya Client - Premium Build";
                
                Console.ForegroundColor = ConsoleColor.DarkMagenta;
                Console.WriteLine("\n");
                Console.WriteLine(@"   _  _         _       _         ");
                Console.WriteLine(@"  | || |___ _ _(_)_ __ (_)_  _ __ _ ");
                Console.WriteLine(@"  | __ / _ \ '_| | '  \| | || / _` |");
                Console.WriteLine(@"  |_||_\___/_| |_|_|_|_|_|\_, \__,_|");
                Console.WriteLine(@"                          |__/      ");
                Console.WriteLine("\n");
                
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("    [");
                Console.ForegroundColor = ConsoleColor.DarkMagenta;
                Console.Write("+");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("] Welcome, ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("Horimiya");

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("    [");
                Console.ForegroundColor = ConsoleColor.DarkMagenta;
                Console.Write("+");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("] Plan: ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("Lifetime / Developer\n");

                string[] bootSequence = new string[]
                {
                    "Validando licencia con el servidor...",
                    "Sincronizando perfil del usuario...",
                    "Optimizando procesos de red...",
                    "Cargando modulos y dependencias internas...",
                    "Cargando configuracion guardada...",
                    "Inicializando motor grafico (ImGui)...",
                    "Preparando la interfaz de Horimiya..."
                };

                var rand = new Random();
                foreach (var step in bootSequence)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("    [~] ");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write(step.PadRight(45));
                    Thread.Sleep(rand.Next(150, 400));
                    
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("OK");
                    Thread.Sleep(rand.Next(30, 100));
                }
                
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("    Injecting [");
                int totalBlocks = 35;
                for (int i = 0; i < totalBlocks; i++)
                {
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    Console.Write("=");
                    Thread.Sleep(rand.Next(10, 30)); 
                }
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("] 100%\n");
                
                Console.ForegroundColor = ConsoleColor.DarkMagenta;
                Console.Write("    > ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Initialization complete. Launching interface...");
                
                Thread.Sleep(800);
            } 
            catch { }
            finally 
            {
                FreeConsole();
            }

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
