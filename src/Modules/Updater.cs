using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace lospoderosos_lite.Modules
{
    public static class Updater
    {
        // IMPORTANTE: Este archivo debe existir en tu GitHub.
        // Ejemplo de contenido de version.txt: 2.3.0|https://tu-sitio.com/lospoderosos.exe
        public static string VersionUrl = "https://raw.githubusercontent.com/joacodemon/lospoderosos/main/version.txt";
        
        public static void CheckForUpdates(string currentVersion)
        {
            try
            {
                using (var client = new WebClient())
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Checking for updates...");
                    
                    var response = client.DownloadString(VersionUrl);
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        var parts = response.Trim().Split('|');
                        if (parts.Length >= 2)
                        {
                            string latestVersion = parts[0];
                            string downloadUrl = parts[1];

                            if (latestVersion != currentVersion)
                            {
                                // "te salte la nueva update" -> Mostramos un aviso de que hay update
                                MessageBox.Show($"Se ha encontrado una nueva versión ({latestVersion}).\nSe descargará y actualizará automáticamente.", 
                                                "Actualización Disponible", MessageBoxButtons.OK, MessageBoxIcon.Information);

                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"\nNew version found! [{currentVersion} -> {latestVersion}]");
                                Console.WriteLine("Downloading update, please wait...");
                                
                                DownloadAndApplyUpdate(downloadUrl);
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("You are running the latest version.");
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to check for updates (server unreachable or invalid URL).");
            }
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        private static void DownloadAndApplyUpdate(string url)
        {
            try
            {
                string currentExe = AppDomain.CurrentDomain.FriendlyName;
                if (!currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    currentExe += ".exe";

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string currentPath = Path.Combine(baseDir, currentExe);
                string newPath = currentPath + ".new";
                string oldPath = currentPath + ".old";

                using (var webClient = new WebClient())
                {
                    webClient.DownloadFile(url, newPath);
                }

                // Renombrar el actual a .old
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(currentPath, oldPath);

                // Mover el nuevo descargado al nombre original
                File.Move(newPath, currentPath);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Update successful! Restarting...\n");
                System.Threading.Thread.Sleep(1000);

                // Iniciar la nueva versión
                Process.Start(currentPath);

                // Matar la versión vieja actual
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al actualizar: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to apply update: " + ex.Message);
            }
        }
    }
}
