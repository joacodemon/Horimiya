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
        public static string VersionUrl = "https://raw.githubusercontent.com/joacodemon/lospoderosos/master/version.txt";

        public static void CheckForUpdates(string currentVersion)
        {
            try
            {
                // Asegurarse de usar TLS 1.2 (Requerido por GitHub)
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Checking for updates...");

                    var noCacheUrl = VersionUrl + "?t=" + DateTime.Now.Ticks;
                    var response = client.DownloadString(noCacheUrl);
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        var parts = response.Trim().Split('|');
                        if (parts.Length >= 2)
                        {
                            string latestVersion = parts[0].Trim();
                            string downloadUrl   = parts[1].Trim();

                            // ── Comparación semántica: solo actualizar si remoto > actual ──
                            // Esto evita el bucle infinito donde el Release tiene una versión
                            // más vieja que el exe actual y el updater intenta "downgrade" en loop.
                            int cmp = CompareVersions(latestVersion, currentVersion);

                            if (cmp > 0)
                            {
                                // Remoto es mayor → actualizar
                                MessageBox.Show(
                                    $"Se ha encontrado una nueva versión ({latestVersion}).\nSe descargará y actualizará automáticamente.",
                                    "Actualización Disponible", MessageBoxButtons.OK, MessageBoxIcon.Information);

                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine($"\nNew version found! [{currentVersion} -> {latestVersion}]");
                                Console.WriteLine("Downloading update, please wait...");

                                DownloadAndApplyUpdate(downloadUrl);
                            }
                            else if (cmp < 0)
                            {
                                // Remoto es menor que el actual (Release desactualizado) → no hacer nada
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"Running ahead of release ({currentVersion}). No update needed.");
                            }
                            else
                            {
                                // Misma versión → todo OK
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("You are running the latest version.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to check for updates (server unreachable or invalid URL).");
                Console.WriteLine($"Error Details: {ex.Message}");
            }
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        // ── Comparación semántica de versiones (Major.Minor.Patch) ──
        // Retorna: >0 si a > b | 0 si a == b | <0 si a < b
        private static int CompareVersions(string a, string b)
        {
            try
            {
                var partsA = a.Split('.');
                var partsB = b.Split('.');
                int len = Math.Max(partsA.Length, partsB.Length);
                for (int i = 0; i < len; i++)
                {
                    int numA = i < partsA.Length ? int.Parse(partsA[i]) : 0;
                    int numB = i < partsB.Length ? int.Parse(partsB[i]) : 0;
                    if (numA != numB) return numA.CompareTo(numB);
                }
                return 0;
            }
            catch
            {
                // Si falla el parse, comparar como string (fallback seguro)
                return string.Compare(a, b, StringComparison.Ordinal);
            }
        }

        private static void DownloadAndApplyUpdate(string url)
        {
            try
            {
                string currentPath = Process.GetCurrentProcess().MainModule.FileName;
                string newPath  = currentPath + ".new";
                string oldPath  = currentPath + ".old";

                using (var webClient = new WebClient())
                {
                    webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    webClient.DownloadFile(url, newPath);
                }

                // Verificar que el archivo descargado tenga tamaño razonable (> 500 KB)
                // para evitar reemplazar el exe con una respuesta de error HTML
                var fi = new FileInfo(newPath);
                if (fi.Length < 500 * 1024)
                {
                    File.Delete(newPath);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Update aborted: downloaded file is too small (bad URL or server error).");
                    return;
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
                MessageBox.Show("Error al actualizar:\n\n" + ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to apply update: " + ex.Message);
            }
        }
    }
}
