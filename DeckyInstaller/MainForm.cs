using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DeckyInstaller
{
    public partial class MainForm : Form
    {
        private bool _isInstalling = false;
        private const string DOWNLOAD_URL = "https://nightly.link/SteamDeckHomebrew/decky-loader/workflows/build-win/main/PluginLoader%20Win.zip";

        private readonly HttpClient _httpClient;

        public MainForm()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            txtOutput.AppendText("Ready to install Decky Loader...\r\n");
        }

        private async void btnInstall_Click(object sender, EventArgs e)
        {
            if (_isInstalling) return;
            await StartInstallation();
        }

        public async Task StartInstallation()
        {
            _isInstalling = true;
            btnInstall.Enabled = false;
            progressBar.Value = 0;
            progressBar.Maximum = 6;
            txtOutput.Clear();

            try
            {
                // Kill any existing PluginLoader processes
                KillExistingPluginLoaders();

                // Step 1: Create .cef-enable-remote-debugging file
                UpdateStatus("Setting up Steam CEF debugging...");
                if (!await SetupSteamDebug())
                {
                    throw new Exception("Failed to setup Steam CEF debugging");
                }
                progressBar.Value++;

                // Step 2: Create homebrew directories
                UpdateStatus("Creating homebrew directories...");
                if (!await CreateHomebrewDirectories())
                {
                    throw new Exception("Failed to create homebrew directories");
                }
                progressBar.Value++;

                // Step 3: Download and extract
                UpdateStatus("Downloading latest build...");
                string zipPath = Path.Combine(Path.GetTempPath(), "PluginLoader.zip");
                
                // Download the zip file
                var zipBytes = await _httpClient.GetByteArrayAsync(DOWNLOAD_URL);
                await File.WriteAllBytesAsync(zipPath, zipBytes);
                progressBar.Value++;

                // Step 4: Extract files
                UpdateStatus("Extracting files...");
                string servicesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "homebrew",
                    "services"
                );
                ZipFile.ExtractToDirectory(zipPath, servicesDir, true);
                progressBar.Value++;

                // Step 5: Create Steam shortcut
                UpdateStatus("Creating Steam shortcut...");
                if (!CreateSteamShortcut())
                {
                    throw new Exception("Failed to create Steam shortcut");
                }
                progressBar.Value++;

                // Step 6: Setup autostart
                UpdateStatus("Setting up autostart...");
                if (!SetupAutostart())
                {
                    throw new Exception("Failed to setup autostart");
                }
                progressBar.Value++;

                // Cleanup
                try { File.Delete(zipPath); } catch { }

                UpdateStatus("Installation complete!");
                MessageBox.Show(
                    "Decky Loader has been installed successfully!\n\n" +
                    "1. Close Steam if it's running\n" +
                    "2. Use the new Steam shortcut on your desktop to launch Steam\n" +
                    "3. In Big Picture Mode, press the STEAM button + A to access the Decky menu",
                    "Installation Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                MessageBox.Show(
                    $"An error occurred: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            finally
            {
                _isInstalling = false;
                btnInstall.Enabled = true;
            }
        }

        private bool CreateSteamShortcut()
        {
            try
            {
                string? steamPath = null;
                string steamExe;

                // Try to find Steam installation path from registry
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    steamPath = key?.GetValue("InstallPath") as string;
                }
                catch
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                        steamPath = key?.GetValue("InstallPath") as string;
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(steamPath))
                {
                    steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
                }

                steamExe = Path.Combine(steamPath, "steam.exe");
                if (!File.Exists(steamExe))
                {
                    throw new Exception("Steam executable not found");
                }

                // Create desktop shortcut using PowerShell
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, "Steam (Decky).lnk");

                string psCommand = $@"$WshShell = New-Object -ComObject WScript.Shell; " +
                                 $@"$Shortcut = $WshShell.CreateShortcut('{shortcutPath}'); " +
                                 $@"$Shortcut.TargetPath = '{steamExe}'; " +
                                 $@"$Shortcut.Arguments = '-dev'; " +
                                 $@"$Shortcut.WorkingDirectory = '{steamPath}'; " +
                                 $@"$Shortcut.Description = 'Launch Steam with Decky Loader'; " +
                                 "$Shortcut.Save()";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{psCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit();

                if (process?.ExitCode == 0)
                {
                    AppendOutput("Created Steam shortcut with -dev parameter");
                    return true;
                }
                else
                {
                    throw new Exception("Failed to create shortcut");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Error creating Steam shortcut: {ex.Message}");
                return false;
            }
        }

        private bool SetupAutostart()
        {
            try
            {
                string servicesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "homebrew",
                    "services"
                );
                string pluginLoader = Path.Combine(servicesDir, "PluginLoader_noconsole.exe");
                
                if (!File.Exists(pluginLoader))
                {
                    throw new Exception("PluginLoader_noconsole.exe not found");
                }

                // Create startup shortcut
                string startupFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup)
                );
                string shortcutPath = Path.Combine(startupFolder, "Decky Loader.lnk");

                string psCommand = $@"$WshShell = New-Object -ComObject WScript.Shell; " +
                                 $@"$Shortcut = $WshShell.CreateShortcut('{shortcutPath}'); " +
                                 $@"$Shortcut.TargetPath = '{pluginLoader}'; " +
                                 $@"$Shortcut.WorkingDirectory = '{servicesDir}'; " +
                                 $@"$Shortcut.Description = 'Decky Loader Autostart'; " +
                                 "$Shortcut.Save()";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"{psCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit();

                if (process?.ExitCode == 0)
                {
                    AppendOutput("Created autostart entry");
                    
                    // Start the process immediately
                    var pluginLoaderProcess = new ProcessStartInfo
                    {
                        FileName = pluginLoader,
                        WorkingDirectory = servicesDir,
                        UseShellExecute = true
                    };
                    Process.Start(pluginLoaderProcess);
                    AppendOutput("Started PluginLoader_noconsole.exe");
                    
                    return true;
                }
                else
                {
                    throw new Exception("Failed to create autostart entry");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Error setting up autostart: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SetupSteamDebug()
        {
            try
            {
                string? steamPath = null;

                // Try to find Steam installation path from registry
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                    steamPath = key?.GetValue("InstallPath") as string;
                }
                catch
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                        steamPath = key?.GetValue("InstallPath") as string;
                    }
                    catch
                    {
                        AppendOutput("Steam installation not found in registry");
                    }
                }

                if (string.IsNullOrEmpty(steamPath))
                {
                    steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
                }

                if (!Directory.Exists(steamPath))
                {
                    throw new Exception("Steam installation directory not found");
                }

                string debugFile = Path.Combine(steamPath, ".cef-enable-remote-debugging");
                File.WriteAllText(debugFile, "");
                AppendOutput("Created .cef-enable-remote-debugging file");

                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error setting up Steam debug: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CreateHomebrewDirectories()
        {
            try
            {
                string homebrewDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "homebrew"
                );
                string servicesDir = Path.Combine(homebrewDir, "services");

                Directory.CreateDirectory(homebrewDir);
                Directory.CreateDirectory(servicesDir);

                AppendOutput($"Created directory: {homebrewDir}");
                AppendOutput($"Created directory: {servicesDir}");

                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error creating directories: {ex.Message}");
                return false;
            }
        }

        private void KillExistingPluginLoaders()
        {
            try
            {
                foreach (var process in Process.GetProcessesByName("PluginLoader"))
                {
                    try
                    {
                        process.Kill();
                        AppendOutput("Terminated PluginLoader.exe process");
                    }
                    catch (Exception ex)
                    {
                        AppendOutput($"Warning: Could not terminate PluginLoader.exe: {ex.Message}");
                    }
                }

                foreach (var process in Process.GetProcessesByName("PluginLoader_noconsole"))
                {
                    try
                    {
                        process.Kill();
                        AppendOutput("Terminated PluginLoader_noconsole.exe process");
                    }
                    catch (Exception ex)
                    {
                        AppendOutput($"Warning: Could not terminate PluginLoader_noconsole.exe: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Warning: Error while checking for existing processes: {ex.Message}");
            }
        }

        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatus(message)));
                return;
            }

            lblStatus.Text = message;
            lblStatus.Refresh();
        }

        private void AppendOutput(string text)
        {
            if (txtOutput.InvokeRequired)
            {
                txtOutput.Invoke(new Action(() => AppendOutput(text)));
            }
            else
            {
                txtOutput.AppendText($"{text}\r\n");
                txtOutput.SelectionStart = txtOutput.TextLength;
                txtOutput.ScrollToCaret();
            }
        }
    }
}
