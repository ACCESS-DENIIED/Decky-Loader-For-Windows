using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using System.Net.Http;

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

        private string[] criticalDependencies = Array.Empty<string>();

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
            progressBar.Maximum = _dependencies.Count + 6;
            txtOutput.Clear();
            UpdateStatus("Starting installation...");

            try
            {
                // Ensure Python Scripts directory is in PATH
                EnsurePythonInPath();
                
                // Check and install Python 3.11 first
                if (!await EnsurePython311Installed())
                {
                    throw new Exception("Failed to install Python 3.11");
                }
                progressBar.Value++;

                // Check and install Git
                if (!await EnsureGitInstalled())
                {
                    throw new Exception("Failed to install Git");
                }
                progressBar.Value++;

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
                if (command == "npm" || command == "pnpm")
                {
                    // Find npm/pnpm in the Node.js installation directory and common locations
                    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string[] possiblePaths = new[]
                    {
                        Path.Combine(programFiles, "nodejs", $"{command}.cmd"),
                        Path.Combine(programFiles, "nodejs", command),
                        Path.Combine(programFiles, "nodejs", $"{command}.exe"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", $"{command}.cmd"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", command),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", $"{command}.exe")
                    };

                    executablePath = possiblePaths.FirstOrDefault(File.Exists) ?? command;
                    if (executablePath == command)
                    {
                        AppendOutput($"Could not find {command} executable in standard locations.");
                        
                        // If pnpm is not found, try installing it using npm
                        if (command == "pnpm")
                        {
                            AppendOutput("Attempting to install pnpm globally using npm...");
                            var npmResult = await RunCommand("npm", "install -g pnpm");
                            if (!npmResult)
                            {
                                AppendOutput("Failed to install pnpm using npm.");
                                return false;
                            }
                            // Retry finding pnpm after installation
                            executablePath = possiblePaths.FirstOrDefault(File.Exists) ?? command;
                        }
                    }
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{executablePath}\" {arguments}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    // Add Node.js and npm paths to the environment
                    EnvironmentVariables = 
                    {
                        ["PATH"] = $"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}\\nodejs;{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\npm;{Environment.GetEnvironmentVariable("PATH")}"
                    }
                };

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

                // Install backend package
                UpdateStatus("Installing decky_loader package...");
                if (!await InstallBackendPackage())
                {
                    return false;
                }

                // Run the Python script with better progress reporting
                UpdateStatus("Building executable (this may take a few minutes)...");
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

                process.OutputDataReceived += (s, e) => 
                { 
                    if (e.Data != null) 
                    {
                        AppendOutput(e.Data);
                        
                        // Update status based on PyInstaller progress
                        if (e.Data.Contains("INFO: PyInstaller:"))
                        {
                            UpdateStatus("Analyzing Python dependencies...");
                        }
                        else if (e.Data.Contains("INFO: Analyzing"))
                        {
                            UpdateStatus("Analyzing required files...");
                        }
                        else if (e.Data.Contains("INFO: Processing"))
                        {
                            UpdateStatus("Processing application files...");
                        }
                        else if (e.Data.Contains("INFO: Copying"))
                        {
                            UpdateStatus("Copying required files...");
                        }
                        else if (e.Data.Contains("INFO: Building"))
                        {
                            UpdateStatus("Building final executable (this may take several minutes)...");
                        }
                    }
                };

                process.ErrorDataReceived += (s, e) => 
                { 
                    if (e.Data != null) 
                    {
                        AppendOutput($"Error: {e.Data}");
                        if (e.Data.Contains("Building EXE"))
                        {
                            UpdateStatus("Finalizing executable (please wait)...");
                        }
                    }
                };

                // Add a timer to show ongoing progress
                var progressTimer = new System.Windows.Forms.Timer();
                var dots = 0;
                progressTimer.Interval = 1000; // 1 second
                progressTimer.Tick += (s, e) =>
                {
                    dots = (dots + 1) % 4;
                    string currentStatus = lblStatus.Text.Split('.')[0]; // Get base status without dots
                    UpdateStatus($"{currentStatus}{"".PadRight(dots, '.')}");
                };
                progressTimer.Start();

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                progressTimer.Stop();
                progressTimer.Dispose();

                if (process.ExitCode != 0)
                {
                    AppendOutput("Failed to build executable");
                    return false;
                }

                UpdateStatus("Build completed successfully!");
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

        private async Task<bool> EnsurePython311Installed()
        {
            UpdateStatus("Checking Python 3.11...");
            try
            {
                // Check if Python 3.11 is installed
                var pythonPath = await GetPythonPath();
                if (!string.IsNullOrEmpty(pythonPath))
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = pythonPath,
                            Arguments = "-c \"import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    string version = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (version.Trim().StartsWith("3.11"))
                    {
                        AppendOutput("Python 3.11 is already installed");
                        return true;
                    }
                }

                // Python 3.11 not found, need to install it
                UpdateStatus("Installing Python 3.11...");
                string installerPath = Path.Combine(Path.GetTempPath(), "python311.exe");
                
                // Download Python installer
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync("https://www.python.org/ftp/python/3.11.0/python-3.11.0-amd64.exe");
                    using (var fs = new FileStream(installerPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                // Install Python
                var installProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = installerPath,
                        Arguments = "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0",
                        UseShellExecute = true,
                        Verb = "runas"
                    }
                };
                
                installProcess.Start();
                await installProcess.WaitForExitAsync();

                // Refresh PATH with null checks and error handling
                var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
                if (key != null)
                {
                    try
                    {
                        object? pathValue = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (pathValue != null)
                        {
                            string path = pathValue.ToString() ?? "";
                            if (!string.IsNullOrEmpty(path))
                            {
                                Environment.SetEnvironmentVariable("Path", path, EnvironmentVariableTarget.Machine);
                                AppendOutput("Successfully updated system PATH");
                            }
                            else
                            {
                                AppendOutput("Warning: System PATH is empty");
                            }
                        }
                        else
                        {
                            AppendOutput("Warning: Could not read system PATH");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendOutput($"Warning: Failed to update system PATH: {ex.Message}");
                    }
                    finally
                    {
                        key.Close();
                    }
                }
                else
                {
                    AppendOutput("Warning: Could not access system environment variables");
                }

                AppendOutput("Python 3.11 installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error installing Python: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> EnsureGitInstalled()
        {
            UpdateStatus("Checking Git...");
            try
            {
                // Check if Git is installed
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                try
                {
                    process.Start();
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        AppendOutput("Git is already installed");
                        return true;
                    }
                }
                catch { }

                // Git not found, need to install it
                UpdateStatus("Installing Git...");
                string installerPath = Path.Combine(Path.GetTempPath(), "git-installer.exe");
                
                // Download Git installer
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync("https://github.com/git-for-windows/git/releases/download/v2.42.0.windows.2/Git-2.42.0.2-64-bit.exe");
                    using (var fs = new FileStream(installerPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                // Install Git
                var installProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = installerPath,
                        Arguments = "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /COMPONENTS=\"icons,ext\\reg\\shellhere,assoc,assoc_sh\"",
                        UseShellExecute = true,
                        Verb = "runas"
                    }
                };
                
                installProcess.Start();
                await installProcess.WaitForExitAsync();

                // Refresh PATH with null checks and error handling
                var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
                if (key != null)
                {
                    try
                    {
                        object? pathValue = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (pathValue != null)
                        {
                            string path = pathValue.ToString() ?? "";
                            if (!string.IsNullOrEmpty(path))
                            {
                                Environment.SetEnvironmentVariable("Path", path, EnvironmentVariableTarget.Machine);
                                AppendOutput("Successfully updated system PATH");
                            }
                            else
                            {
                                AppendOutput("Warning: System PATH is empty");
                            }
                        }
                        else
                        {
                            AppendOutput("Warning: Could not read system PATH");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendOutput($"Warning: Failed to update system PATH: {ex.Message}");
                    }
                    finally
                    {
                        key.Close();
                    }
                }
                else
                {
                    AppendOutput("Warning: Could not access system environment variables");
                }

                AppendOutput("Git installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error installing Git: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> PrepareBackendFiles()
        {
            try
            {
                string backendDir = Path.Combine(Application.StartupPath, "app", "backend");
                
                // Create setup.py with exact dependencies from pyproject.toml
                string setupPy = @"
from setuptools import setup, find_packages

setup(
    name='decky_loader',
    version='1.0.0',
    packages=find_packages(),
    install_requires=[
        'aiohttp>=3.9.5',
        'aiohttp-jinja2>=1.5.1',
        'aiohttp-cors>=0.7.0',
        'watchdog>=4',
        'certifi',
        'packaging>=24',
        'multidict>=6.0.5',
        'setproctitle>=1.3.3'
    ],
    extras_require={
        'dev': [
            'pyinstaller>=6.8.0',
            'pyright>=1.1.335'
        ]
    }
)";
                await File.WriteAllTextAsync(Path.Combine(backendDir, "setup.py"), setupPy.TrimStart());

                // Update critical dependencies list to match
                criticalDependencies = new[] 
                { 
                    "aiohttp", 
                    "aiohttp_jinja2",
                    "aiohttp_cors",
                    "watchdog",
                    "certifi",
                    "packaging",
                    "multidict",
                    "setproctitle"
                };

                // Rest of the existing PrepareBackendFiles code...
                return true; // Return true if everything succeeded
            }
            catch (Exception ex)
            {
                AppendOutput($"Error preparing backend files: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> InstallBackendPackage()
        {
            try
            {
                string backendDir = Path.Combine(Application.StartupPath, "app", "backend");
                
                // Ensure backend directory exists
                if (!Directory.Exists(backendDir))
                {
                    AppendOutput("Error: Backend directory not found");
                    return false;
                }

                // Prepare backend files
                if (!await PrepareBackendFiles())
                {
                    return false;
                }

                // First, upgrade pip and install wheel
                UpdateStatus("Upgrading pip and installing wheel...");
                var pythonPath = await GetPythonPath();
                await RunCommand(pythonPath, "-m pip install --upgrade pip wheel");

                // Install the package and its dependencies in development mode
                UpdateStatus("Installing backend package and dependencies...");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pythonPath,
                        Arguments = "-m pip install -e . --no-cache-dir",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = backendDir,
                        // Add Python Scripts to PATH
                        EnvironmentVariables = 
                        {
                            ["PATH"] = $"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python311", "Scripts")};{Environment.GetEnvironmentVariable("PATH")}"
                        }
                    }
                };

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
                    AppendOutput("Failed to install backend package");
                    AppendOutput($"Error output: {error}");
                    return false;
                }

                // Only verify the dependencies we actually need from pyproject.toml
                string[] requiredDependencies = new[] 
                { 
                    "aiohttp", 
                    "aiohttp_jinja2",
                    "aiohttp_cors",
                    "watchdog",
                    "certifi",
                    "packaging",
                    "multidict",
                    "setproctitle"
                };

                foreach (var dep in requiredDependencies)
                {
                    UpdateStatus($"Verifying {dep} installation...");
                    bool installSuccess = false;
                    int retryCount = 0;
                    
                    while (!installSuccess && retryCount < 3)
                    {
                        var verifyProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = pythonPath,
                                Arguments = $"-c \"import {dep.Replace('-', '_')}\"",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };
                        
                        try
                        {
                            verifyProcess.Start();
                            string verifyError = await verifyProcess.StandardError.ReadToEndAsync();
                            await verifyProcess.WaitForExitAsync();
                            
                            if (verifyProcess.ExitCode == 0)
                            {
                                installSuccess = true;
                                AppendOutput($"Successfully verified {dep}");
                            }
                            else
                            {
                                AppendOutput($"Attempting to install {dep} (attempt {retryCount + 1}/3)...");
                                // Try to install the package with both pip and pip3
                                if (!await RunCommand(pythonPath, $"-m pip install --no-cache-dir {dep}"))
                                {
                                    await RunCommand(pythonPath, $"-m pip3 install --no-cache-dir {dep}");
                                }
                                retryCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendOutput($"Error verifying {dep}: {ex.Message}");
                            retryCount++;
                        }
                    }

                    if (!installSuccess)
                    {
                        AppendOutput($"Warning: Failed to verify/install {dep} after multiple attempts");
                    }
                }

                // Verify all dependencies are importable in a single Python session
                UpdateStatus("Verifying all dependencies together...");
                var finalVerification = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pythonPath,
                        Arguments = "-c \"" + string.Join(";", requiredDependencies.Select(dep => $"import {dep.Replace('-', '_')}")) + "\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                finalVerification.Start();
                string finalError = await finalVerification.StandardError.ReadToEndAsync();
                await finalVerification.WaitForExitAsync();

                if (finalVerification.ExitCode != 0)
                {
                    AppendOutput($"Warning: Some dependencies may have conflicts: {finalError}");
                }

                AppendOutput("Successfully installed backend package and verified dependencies");
                return true;
            }
            catch (Exception ex)
            {
                AppendOutput($"Error installing backend package: {ex.Message}");
                return false;
            }
        }

        private void EnsurePythonInPath()
        {
            try
            {
                string pythonScriptsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python311", "Scripts");
                string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
                
                if (!currentPath.Contains(pythonScriptsPath))
                {
                    Environment.SetEnvironmentVariable("PATH", $"{pythonScriptsPath};{currentPath}", EnvironmentVariableTarget.Process);
                    AppendOutput($"Added Python Scripts directory to PATH: {pythonScriptsPath}");
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"Warning: Could not add Python Scripts to PATH: {ex.Message}");
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
