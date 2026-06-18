using System;
using System.IO;
using System.Windows.Forms;
using lospoderosos_lite.Config;
using lospoderosos_lite.Modules;
using lospoderosos_lite.UI;
using System.Drawing;

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

                // Initialize Modules
                var clicker = new Clicker(cfg);
                var recorder = new Recorder();
                var misc = new Misc(cfg, clicker);
                
                // Start Modules
                clicker.Start();
                misc.Start();

                // Run main form
                var mainForm = new MainForm(cfg, clicker, recorder, misc);
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
