using System;
using System.IO;
using System.Windows.Forms;
using lospoderosos_lite.Config;
using lospoderosos_lite.Modules;
using lospoderosos_lite.UI;
using System.Drawing;
using lospoderosos_lite.Utils;

namespace lospoderosos_lite
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Setup Dependency Injection Container
                var container = new DependencyContainer();

                // Initialize Configuration
                var cfg = new AppConfig();

                // Login bypassed for the new version – no password required
                // (Login screen removed)

                // Show Splash Screen
                var splash = new SplashForm();
                Application.Run(splash);

                // Load default configuration if it exists
                string defaultCfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configs", "default.json");
                if (File.Exists(defaultCfgPath))
                {
                    try
                    {
                        string json = File.ReadAllText(defaultCfgPath);
                        cfg = AppConfig.FromJson(json);
                    }
                    catch
                    {
                        // Ignore load errors and use defaults
                    }
                }

                container.RegisterSingleton<AppConfig>(cfg);

                // Auto Register all [Injectable] classes
                container.AutoRegister(System.Reflection.Assembly.GetExecutingAssembly());

                // Initialize Modules using DI Container
                var clicker = container.Resolve<Clicker>();
                var recorder = container.Resolve<Recorder>();
                var misc = container.Resolve<Misc>();
                
                // Start Modules
                clicker.Start();
                misc.Start();

                // Run main form via DI
                container.RegisterTransient<MainForm>();
                var mainForm = container.Resolve<MainForm>();
                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                // Write crash dump
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.WriteAllText(logPath, ex.ToString());
            }
        }
    }
}
