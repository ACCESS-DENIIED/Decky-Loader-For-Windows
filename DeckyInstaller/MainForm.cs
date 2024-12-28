using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace DeckyInstaller
{
    public partial class MainForm : Form
    {
        private class Release
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;
            
            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }
        }

        private readonly List<DependencyItem> _dependencies = new()
        {
            new DependencyItem("Python 3.11", "Python.Python.3.11", "python --version", true),
            new DependencyItem("Node.js LTS", "OpenJS.NodeJS.LTS", "node --version", true),
            new DependencyItem("Git", "Git.Git", "git --version", true),
            new DependencyItem("Visual Studio Build Tools", "Microsoft.VisualStudio.2022.BuildTools", "", true)
        };

        private bool _isInstalling = false;

        private string _selectedVersion = "v3.0.5";
        private bool _cliMode = false;

        public MainForm(string? version = null)
        {
            InitializeComponent();
            if (version != null)
            {
                _selectedVersion = version;
                _cliMode = true;
            }
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            if (_cliMode)
            {
                Hide();
                await StartInstallation();
                Application.Exit();
                return;
            }

            // Copy decky_builder.py to the application directory
            try
            {
                string rootDir = Path.GetDirectoryName(Application.StartupPath)!;
                string sourceScript = Path.Combine(rootDir, "decky_builder.py");
                string targetScript = Path.Combine(Application.StartupPath, "decky_builder.py");

                if (File.Exists(sourceScript) && !File.Exists(targetScript))
                {
                    File.Copy(sourceScript, targetScript, true);
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Warning: Could not copy decky_builder.py: {ex.Message}");
            }

            txtOutput.AppendText("Ready to install Decky Loader...\r\n");
            await LoadVersions();
            CheckWinget();
        }

        private async void btnInstall_Click(object? sender, EventArgs e)
        {
            if (_isInstalling) return;
            var selectedItem = cmbVersions.SelectedItem?.ToString();
            if (selectedItem != null && !selectedItem.StartsWith("---") && !string.IsNullOrWhiteSpace(selectedItem))
            {
                _selectedVersion = selectedItem;
                await StartInstallation();
            }
        }

        private void cmbVersions_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedItem = cmbVersions.SelectedItem?.ToString();
            if (selectedItem != null && (selectedItem.StartsWith("---") || string.IsNullOrWhiteSpace(selectedItem)))
            {
                // If a header or separator is selected, try to select the next item
                if (cmbVersions.SelectedIndex + 1 < cmbVersions.Items.Count)
                {
                    cmbVersions.SelectedIndex++;
                }
                else if (cmbVersions.SelectedIndex - 1 >= 0)
                {
                    cmbVersions.SelectedIndex--;
                }
            }
        }

        private async Task LoadVersions()
        {
            try
            {
                UpdateStatus("Fetching available versions...");
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Decky-Loader-Installer");
                
                var response = await client.GetAsync("https://api.github.com/repos/SteamDeckHomebrew/decky-loader/releases");
                response.EnsureSuccessStatusCode();
                
                var releases = await response.Content.ReadAsStringAsync();
                var releaseData = System.Text.Json.JsonSerializer.Deserialize<List<Release>>(releases);

                if (releaseData != null && releaseData.Any())
                {
                    var stableVersions = releaseData.Where(r => !r.Prerelease)
                                                  .Select(r => r.TagName)
                                                  .Take(3)
                                                  .ToList();
                    
                    var preReleaseVersions = releaseData.Where(r => r.Prerelease)
                                                      .Select(r => r.TagName)
                                                      .Take(3)
                                                      .ToList();

                    cmbVersions.Items.Clear();
                    
                    // Add stable versions
                    if (stableVersions.Any())
                    {
                        cmbVersions.Items.Add("--- Stable Versions ---");
                        foreach (var version in stableVersions)
                        {
                            cmbVersions.Items.Add(version);
                        }
                    }

                    // Add pre-release versions
                    if (preReleaseVersions.Any())
                    {
                        if (stableVersions.Any()) cmbVersions.Items.Add(""); // Separator
                        cmbVersions.Items.Add("--- Pre-release Versions ---");
                        foreach (var version in preReleaseVersions)
                        {
                            cmbVersions.Items.Add(version);
                        }
                    }

                    // Select latest stable version, or pre-release if no stable version exists
                    var defaultVersion = stableVersions.FirstOrDefault() ?? preReleaseVersions.FirstOrDefault();
                    if (defaultVersion != null)
                    {
                        cmbVersions.SelectedItem = defaultVersion;
                        _selectedVersion = defaultVersion;
                    }
                }
                else
                {
                    cmbVersions.Items.Add(_selectedVersion);
                    cmbVersions.SelectedIndex = 0;
                }

                UpdateStatus("Ready to install...");
            }
            catch (Exception ex)
            {
                AppendOutput($"Error fetching versions: {ex.Message}");
                cmbVersions.Items.Add(_selectedVersion);
                cmbVersions.SelectedIndex = 0;
            }
        }

        public async Task StartInstallation()
        {
            _isInstalling = true;
            if (!_cliMode)
            {
                btnInstall.Enabled = false;
                cmbVersions.Enabled = false;
            }
            progressBar.Value = 0;
            progressBar.Maximum = _dependencies.Count + 4; // +4 for npm install, pnpm install, python script, and final setup
            txtOutput.Clear();
            UpdateStatus("Starting installation...");

            try
            {
                // Check and install dependencies only if they don't exist
                foreach (var dependency in _dependencies)
                {
                    UpdateStatus($"Checking {dependency.Name}...");
                    if (!await CheckDependency(dependency))
                    {
                        UpdateStatus($"Installing {dependency.Name}...");
                        if (dependency.Name == "Node.js LTS")
                        {
                            await InstallNodeJs();
                        }
                        else
                        {
                            await InstallDependency(dependency);
                        }
                    }
                    progressBar.Value++;
                    await Task.Delay(100);
                }

                // Install pnpm globally if not already installed
                UpdateStatus("Checking pnpm installation...");
                bool pnpmExists = false;
                try
                {
                    var pnpmProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "pnpm",
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    pnpmProcess.Start();
                    await pnpmProcess.WaitForExitAsync();
                    pnpmExists = pnpmProcess.ExitCode == 0;
                }
                catch { }

                if (!pnpmExists)
                {
                    UpdateStatus("Installing pnpm globally...");
                    await RunCommand("npm", "install -g pnpm");
                }
                progressBar.Value++;

                // Install Python dependencies
                UpdateStatus("Installing Python dependencies...");
                string pythonPath = await GetPythonPath();
                await RunCommand(pythonPath, "-m pip install --upgrade pip");
                await RunCommand(pythonPath, "-m pip install PyInstaller requests psutil");
                progressBar.Value++;

                // Run Python script
                UpdateStatus("Running Decky Loader installer...");
                string scriptPath = Path.Combine(Application.StartupPath, "decky_builder.py");
                
                // Copy decky_builder.py to the current directory if it doesn't exist
                if (!File.Exists(scriptPath))
                {
                    string sourceScript = Path.Combine(Path.GetDirectoryName(Application.StartupPath)!, "decky_builder.py");
                    File.Copy(sourceScript, scriptPath, true);
                }

                bool success = await RunPythonScript(scriptPath);
                progressBar.Value++;

                if (success)
                {
                    UpdateStatus("Installation completed successfully!");
                    progressBar.Value = progressBar.Maximum;
                    if (!_cliMode)
                    {
                        MessageBox.Show(
                            "Decky Loader has been installed successfully!",
                            "Installation Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                }
                else
                {
                    UpdateStatus("Installation failed. Check the output above for details.");
                    if (!_cliMode)
                    {
                        MessageBox.Show(
                            "There was an error during installation. Please check the output for details.",
                            "Installation Failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                if (!_cliMode)
                {
                    MessageBox.Show(
                        $"An unexpected error occurred: {ex.Message}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            finally
            {
                _isInstalling = false;
                if (!_cliMode)
                {
                    btnInstall.Enabled = true;
                    cmbVersions.Enabled = true;
                }
            }
        }

        private async Task<bool> InstallNodeJs()
        {
            try
            {
                AppendOutput("Installing Node.js v18.18.0...");
                string nodeVersion = "v18.18.0";
                string nodeUrl = $"https://nodejs.org/dist/{nodeVersion}/node-{nodeVersion}-x64.msi";
                string installerPath = Path.Combine(Path.GetTempPath(), "node-installer.msi");

                // Download Node.js installer
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(nodeUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        AppendOutput($"Error downloading Node.js: {response.StatusCode} ({response.ReasonPhrase})");
                        return false;
                    }

                    using (var fs = new FileStream(installerPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                // Install Node.js
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "msiexec",
                        Arguments = $"/i \"{installerPath}\" /qn",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas"
                    }
                };

                process.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput($"Error: {e.Data}"); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                // Clean up installer
                try
                {
                    File.Delete(installerPath);
                }
                catch { }

                if (process.ExitCode != 0)
                {
                    AppendOutput($"Node.js installation failed with exit code {process.ExitCode}");
                    return false;
                }

                // Add Node.js to PATH for this process
                var nodeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs");
                Environment.SetEnvironmentVariable("PATH", $"{nodeDir};{Environment.GetEnvironmentVariable("PATH")}", EnvironmentVariableTarget.Process);

                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error installing Node.js: {ex.Message}");
                return false;
            }
        }

        private void CheckWinget()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                UpdateStatus($"Winget version: {output.Trim()}");
            }
            catch
            {
                MessageBox.Show(
                    "Winget is not installed. Please install App Installer from the Microsoft Store.",
                    "Dependency Missing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1",
                    UseShellExecute = true
                });
                Application.Exit();
            }
        }

        private async Task<bool> CheckDependency(DependencyItem dependency)
        {
            UpdateStatus($"Checking for {dependency.Name}...");
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = dependency.Name == "Python 3.11" ? "python" : "cmd.exe",
                        Arguments = dependency.Name == "Python 3.11" ? "-c \"import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')\"" : $"/c {dependency.CheckCommand}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                var output = new StringBuilder();
                process.OutputDataReceived += (s, e) => 
                { 
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                        AppendOutput(e.Data);
                    }
                };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput($"Error: {e.Data}"); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (dependency.Name == "Python 3.11")
                {
                    string version = output.ToString().Trim();
                    if (version.StartsWith("3.11"))
                    {
                        AppendOutput($"Found Python {version}");
                        return true;
                    }
                    AppendOutput($"Python {version} found, but version 3.11 is required");
                    return false;
                }

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error checking {dependency.Name}: {ex.Message}");
                return false;
            }
        }

        private async Task InstallDependency(DependencyItem dependency)
        {
            UpdateStatus($"Installing {dependency.Name}...");
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = $"install {dependency.WingetId} --accept-source-agreements --accept-package-agreements",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput($"Error: {e.Data}"); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                AppendOutput($"Error installing {dependency.Name}: {ex.Message}");
                MessageBox.Show(
                    $"Error installing {dependency.Name}: {ex.Message}",
                    "Installation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private async Task<bool> RunCommand(string command, string arguments)
        {
            try
            {
                string executablePath = command;
                if (command == "npm")
                {
                    // Find npm in the Node.js installation directory
                    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string[] possiblePaths = new[]
                    {
                        Path.Combine(programFiles, "nodejs", "npm.cmd"),
                        Path.Combine(programFiles, "nodejs", "npm"),
                        Path.Combine(programFiles, "nodejs", "npm.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npm.cmd"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npm"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npm.exe")
                    };

                    executablePath = possiblePaths.FirstOrDefault(File.Exists) ?? command;
                    if (executablePath == command)
                    {
                        AppendOutput("Could not find npm executable in standard locations.");
                        return false;
                    }
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // For npm commands, we need to use cmd.exe
                if (command == "npm")
                {
                    startInfo.FileName = "cmd.exe";
                    startInfo.Arguments = $"/c \"{executablePath}\" {arguments}";
                }

                var process = new Process { StartInfo = startInfo };

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) => 
                { 
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                        AppendOutput(e.Data);
                    }
                };
                
                process.ErrorDataReceived += (s, e) => 
                { 
                    if (e.Data != null)
                    {
                        error.AppendLine(e.Data);
                        AppendOutput($"Error: {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    string errorOutput = error.ToString().Trim();
                    if (!string.IsNullOrEmpty(errorOutput))
                    {
                        AppendOutput($"Command failed with errors:\n{errorOutput}");
                    }
                    else
                    {
                        AppendOutput($"Command failed with exit code {process.ExitCode}");
                    }
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error running command {command}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RunPythonScript(string scriptPath)
        {
            try
            {
                // Get Python path
                string pythonPath = await GetPythonPath();
                if (string.IsNullOrEmpty(pythonPath))
                {
                    AppendOutput("Error: Python 3.11 is required but not found.");
                    return false;
                }

                // Get the root directory (where decky_builder.py is located)
                string rootDir = Path.GetDirectoryName(Application.StartupPath)!;
                string deckyBuilderPath = Path.Combine(rootDir, "decky_builder.py");

                // Copy decky_builder.py if it doesn't exist in the root directory
                if (!File.Exists(deckyBuilderPath))
                {
                    string sourceScript = Path.Combine(Path.GetDirectoryName(Application.StartupPath)!, "decky_builder.py");
                    File.Copy(sourceScript, deckyBuilderPath, true);
                }

                // Create necessary directories
                string appDir = Path.Combine(rootDir, "app");
                string frontendDir = Path.Combine(appDir, "frontend");
                string backendDir = Path.Combine(appDir, "backend");
                string deckyLoaderDir = Path.Combine(backendDir, "decky_loader");

                Directory.CreateDirectory(appDir);
                Directory.CreateDirectory(frontendDir);
                Directory.CreateDirectory(backendDir);
                Directory.CreateDirectory(deckyLoaderDir);

                // Create version files in key locations
                string[] versionFiles = new[]
                {
                    Path.Combine(appDir, ".loader.version"),
                    Path.Combine(frontendDir, ".loader.version"),
                    Path.Combine(backendDir, ".loader.version"),
                    Path.Combine(deckyLoaderDir, ".loader.version")
                };

                foreach (string versionFile in versionFiles)
                {
                    await File.WriteAllTextAsync(versionFile, _selectedVersion);
                }

                // Install required Python packages
                UpdateStatus("Installing required Python packages...");
                var pipInstallProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pythonPath,
                        Arguments = "-m pip install --upgrade pip pyinstaller requests psutil",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas",
                        WorkingDirectory = rootDir
                    }
                };

                pipInstallProcess.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };
                pipInstallProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput($"Error: {e.Data}"); };

                pipInstallProcess.Start();
                pipInstallProcess.BeginOutputReadLine();
                pipInstallProcess.BeginErrorReadLine();
                await pipInstallProcess.WaitForExitAsync();

                if (pipInstallProcess.ExitCode != 0)
                {
                    AppendOutput("Failed to install required Python packages.");
                    return false;
                }

                // Install decky_loader package from the backend directory
                UpdateStatus("Installing decky_loader package...");
                var installDeckyProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pythonPath,
                        Arguments = "-m pip install -e backend",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas",
                        WorkingDirectory = appDir
                    }
                };

                installDeckyProcess.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };
                installDeckyProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput($"Error: {e.Data}"); };

                installDeckyProcess.Start();
                installDeckyProcess.BeginOutputReadLine();
                installDeckyProcess.BeginErrorReadLine();
                await installDeckyProcess.WaitForExitAsync();

                if (installDeckyProcess.ExitCode != 0)
                {
                    AppendOutput("Failed to install decky_loader package.");
                    return false;
                }

                // Run the Python script
                UpdateStatus("Running decky_builder.py...");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pythonPath,
                        Arguments = $"\"{deckyBuilderPath}\" --release {_selectedVersion}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        Verb = "runas",
                        WorkingDirectory = rootDir
                    }
                };

                process.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput($"Error: {e.Data}"); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    AppendOutput("Failed to run decky_builder.py");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error running Python script: {ex.Message}");
                return false;
            }
        }

        private async Task<string> GetPythonPath()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = "-c \"import sys; print(sys.executable)\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                var output = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    string pythonPath = output.ToString().Trim();
                    if (!string.IsNullOrEmpty(pythonPath))
                    {
                        return pythonPath;
                    }
                }

                // Fallback to checking common Python installation paths
                string[] possiblePaths = new[]
                {
                    @"C:\Program Files\Python311\python.exe",
                    @"C:\Program Files (x86)\Python311\python.exe",
                    @"C:\Python311\python.exe"
                };

                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }

                return "python"; // Last resort, try using python from PATH
            }
            catch
            {
                return "python"; // Fallback to using python from PATH
            }
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            
            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, true);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
            }
        }

        private void UpdateStatus(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatus(message)));
                return;
            }

            // Update the status label with the current operation
            lblStatus.Text = message;
            // Force the label to refresh immediately
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

    public class DependencyItem
    {
        public string Name { get; }
        public string WingetId { get; }
        public string CheckCommand { get; }
        public bool Required { get; }

        public DependencyItem(string name, string wingetId, string checkCommand, bool required)
        {
            Name = name;
            WingetId = wingetId;
            CheckCommand = checkCommand;
            Required = required;
        }
    }
}