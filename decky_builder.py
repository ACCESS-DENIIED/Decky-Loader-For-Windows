import os
import subprocess
import shutil
import argparse
from pathlib import Path
import sys
import time
import PyInstaller

class DeckyBuilder:
    def __init__(self, release: str):
        self.release = release
        self.root_dir = Path(__file__).parent
        self.app_dir = self.root_dir / "app"
        self.src_dir = self.root_dir / "src"
        self.dist_dir = self.root_dir / "dist"
        self.homebrew_dir = self.dist_dir / "homebrew"
        
        # Setup user homebrew directory
        self.user_home = Path.home()
        self.user_homebrew_dir = self.user_home / "homebrew"
        self.homebrew_folders = [
            "data",
            "logs",
            "plugins",
            "services",
            "settings",
            "themes"
        ]

    def safe_remove_directory(self, path):
        """Safely remove a directory with retries for Windows"""
        max_retries = 3
        retry_delay = 1  # seconds

        for attempt in range(max_retries):
            try:
                if path.exists():
                    # On Windows, sometimes we need to remove .git directory separately
                    git_dir = path / '.git'
                    if git_dir.exists():
                        for item in git_dir.glob('**/*'):
                            if item.is_file():
                                try:
                                    item.chmod(0o777)  # Give full permissions
                                    item.unlink()
                                except:
                                    pass
                    
                    shutil.rmtree(path, ignore_errors=True)
                return
            except Exception as e:
                print(f"Attempt {attempt + 1} failed to remove {path}: {str(e)}")
                if attempt < max_retries - 1:
                    time.sleep(retry_delay)
                    continue
                else:
                    print(f"Warning: Could not fully remove {path}. Continuing anyway...")

    def setup_directories(self):
        """Setup directory structure"""
        print("Setting up directories...")
        # Clean up any existing directories
        if self.app_dir.exists():
            self.safe_remove_directory(self.app_dir)
        if self.src_dir.exists():
            self.safe_remove_directory(self.src_dir)
        if self.homebrew_dir.exists():
            self.safe_remove_directory(self.homebrew_dir)

        # Create fresh directories
        self.src_dir.mkdir(parents=True, exist_ok=True)
        self.homebrew_dir.mkdir(parents=True, exist_ok=True)

    def setup_homebrew(self):
        """Setup homebrew directory structure"""
        print("Setting up homebrew directory structure...")
        # Create dist directory
        (self.homebrew_dir / "dist").mkdir(parents=True, exist_ok=True)

        # Setup homebrew directory structure for both temp and user directories
        print("Setting up homebrew directory structure...")
        for directory in [self.homebrew_dir, self.user_homebrew_dir]:
            if not directory.exists():
                directory.mkdir(parents=True)
            
            for folder in self.homebrew_folders:
                folder_path = directory / folder
                if not folder_path.exists():
                    folder_path.mkdir(parents=True)

    def clone_repository(self):
        """Clone Decky Loader repository"""
        print("Cloning repository with release", self.release)
        if os.path.exists(self.app_dir):
            shutil.rmtree(self.app_dir)
        
        # Clone the repository
        subprocess.run(['git', 'clone', 'https://github.com/SteamDeckHomebrew/decky-loader.git', self.app_dir], check=True)
        os.chdir(self.app_dir)
        
        # If release is 'main', try to get the latest tag
        if self.release == 'main':
            try:
                # Get the latest tag
                result = subprocess.run(['git', 'describe', '--tags', '--abbrev=0'], 
                                     capture_output=True, text=True, check=True)
                self.release = result.stdout.strip()
                print(f"Using latest tag: {self.release}")
            except subprocess.CalledProcessError:
                print("Warning: Could not get latest tag, using 'main'")
        
        # Checkout the specified release
        subprocess.run(['git', 'checkout', self.release], check=True)
        os.chdir(self.root_dir)

    def build_frontend(self):
        """Build frontend files"""
        print("Building frontend...")
        try:
            frontend_dir = self.app_dir / "frontend"
            os.chdir(frontend_dir)

            # Create .loader.version file with the release tag
            with open(".loader.version", "w") as f:
                f.write(self.release)

            # Install dependencies and build
            subprocess.run(["pnpm", "i"], check=True)
            subprocess.run(["pnpm", "run", "build"], check=True)

            # Return to original directory
            os.chdir(self.root_dir)
        except Exception as e:
            print(f"Error building frontend: {str(e)}")
            raise

    def prepare_backend(self):
        """Prepare backend files for building."""
        print("Preparing backend files...")
        print("Copying files according to Dockerfile structure...")

        # Create src directory if it doesn't exist
        os.makedirs(self.src_dir, exist_ok=True)

        # Copy backend files from app/backend/decky_loader to src/decky_loader
        print("Copying backend files...")
        shutil.copytree(os.path.join(self.app_dir, "backend", "decky_loader"), 
                       os.path.join(self.src_dir, "decky_loader"), 
                       dirs_exist_ok=True)

        # Copy static, locales, and plugin directories to maintain decky_loader structure
        os.makedirs(os.path.join(self.src_dir, "decky_loader"), exist_ok=True)
        shutil.copytree(os.path.join(self.app_dir, "backend", "decky_loader", "static"),
                       os.path.join(self.src_dir, "decky_loader", "static"),
                       dirs_exist_ok=True)
        shutil.copytree(os.path.join(self.app_dir, "backend", "decky_loader", "locales"),
                       os.path.join(self.src_dir, "decky_loader", "locales"),
                       dirs_exist_ok=True)
        shutil.copytree(os.path.join(self.app_dir, "backend", "decky_loader", "plugin"),
                       os.path.join(self.src_dir, "decky_loader", "plugin"),
                       dirs_exist_ok=True)

        # Create legacy directory
        os.makedirs(os.path.join(self.src_dir, "src", "legacy"), exist_ok=True)

        # Copy main.py to src directory
        shutil.copy2(os.path.join(self.app_dir, "backend", "main.py"),
                    os.path.join(self.src_dir, "main.py"))

        # Create .loader.version file in src/dist
        print("Creating .loader.version...")
        os.makedirs(os.path.join(self.src_dir, "dist"), exist_ok=True)
        shutil.copy2(os.path.join(self.app_dir, "frontend", ".loader.version"),
                    os.path.join(self.src_dir, "dist", ".loader.version"))

        print("Backend preparation completed successfully!")
        return True

    def install_requirements(self):
        """Install Python requirements"""
        print("Installing Python requirements...")
        try:
            # Try both requirements.txt and pyproject.toml
            requirements_file = self.app_dir / "backend" / "requirements.txt"
            pyproject_file = self.app_dir / "backend" / "pyproject.toml"
            
            if requirements_file.exists():
                subprocess.run([
                    "pip", "install", "-r", str(requirements_file)
                ], check=True)
            elif pyproject_file.exists():
                subprocess.run([
                    "pip", "install", "poetry"
                ], check=True)
                subprocess.run([
                    "poetry", "install"
                ], cwd=self.app_dir / "backend", check=True)
            else:
                print("Warning: No requirements.txt or pyproject.toml found")
        except Exception as e:
            print(f"Error installing requirements: {str(e)}")
            raise

    def build_executables(self):
        """Build executables using PyInstaller"""
        print("Building executables...")
        try:
            # Clean services directory first
            services_dir = os.path.join(os.path.expanduser("~"), "homebrew", "services")
            if os.path.exists(services_dir):
                for item in os.listdir(services_dir):
                    item_path = os.path.join(services_dir, item)
                    if os.path.isfile(item_path):
                        os.remove(item_path)
                    elif os.path.isdir(item_path):
                        shutil.rmtree(item_path)

            # Build console version
            subprocess.run([
                'pyinstaller',
                '--clean',
                '--name', 'PluginLoader',
                '--onefile',
                '--console',
                '--add-data', 'decky_loader/static;decky_loader/static',
                '--add-data', 'decky_loader/locales;decky_loader/locales',
                '--add-data', 'src/legacy;src/legacy',
                '--add-data', 'decky_loader/plugin;decky_loader/plugin',
                '--hidden-import', 'logging.handlers',
                '--hidden-import', 'sqlite3',
                'main.py'
            ], check=True, cwd=self.src_dir)

            # Build no-console version
            subprocess.run([
                'pyinstaller',
                '--clean',
                '--name', 'PluginLoader_noconsole',
                '--onefile',
                '--noconsole',
                '--add-data', 'decky_loader/static;decky_loader/static',
                '--add-data', 'decky_loader/locales;decky_loader/locales',
                '--add-data', 'src/legacy;src/legacy',
                '--add-data', 'decky_loader/plugin;decky_loader/plugin',
                '--hidden-import', 'logging.handlers',
                '--hidden-import', 'sqlite3',
                'main.py'
            ], check=True, cwd=self.src_dir)

            # Create required directories
            services_dir = os.path.join(os.path.expanduser("~"), "homebrew", "services")
            logs_dir = os.path.join(os.path.expanduser("~"), "homebrew", "logs")
            os.makedirs(services_dir, exist_ok=True)
            os.makedirs(logs_dir, exist_ok=True)

            # Copy executables to homebrew directory
            print("Copying executables to homebrew directory...")
            shutil.copy2(
                os.path.join(self.src_dir, "dist", "PluginLoader.exe"),
                os.path.join(services_dir, "plugin_loader.exe")
            )
            shutil.copy2(
                os.path.join(self.src_dir, "dist", "PluginLoader_noconsole.exe"),
                os.path.join(services_dir, "PluginLoader_noconsole.exe")
            )

            # Create version file
            version_file = os.path.join(services_dir, ".loader.version")
            with open(version_file, "w") as f:
                f.write(self.release)

            return True
        except subprocess.CalledProcessError as e:
            print(f"Error during build process: {e}")
            return False

    def copy_to_homebrew(self):
        """Copy all necessary files to the homebrew directory"""
        print("Copying files to homebrew directory...")
        try:
            # Create homebrew directory if it doesn't exist
            os.makedirs(self.homebrew_dir, exist_ok=True)

            # We don't need to copy anything else since build_executables handles the file copying
            pass

        except Exception as e:
            print(f"Error during copy to homebrew: {str(e)}")
            raise

    def install_nodejs(self):
        """Install Node.js v18.18.0 with npm"""
        print("Installing Node.js v18.18.0...")
        try:
            # Create temp directory for downloads
            temp_dir = self.root_dir / "temp"
            temp_dir.mkdir(exist_ok=True)
            
            # Download Node.js installer
            node_installer = temp_dir / "node-v18.18.0-x64.msi"
            if not node_installer.exists():
                print("Downloading Node.js installer...")
                try:
                    import urllib.request
                    urllib.request.urlretrieve(
                        "https://nodejs.org/dist/v18.18.0/node-v18.18.0-x64.msi",
                        node_installer
                    )
                except Exception as e:
                    print(f"Error downloading Node.js installer: {str(e)}")
                    print("Please download Node.js v18.18.0 manually from: https://nodejs.org/dist/v18.18.0/node-v18.18.0-x64.msi")
                    print("Then place it in the following directory:", temp_dir)
                    input("Press Enter to continue once you've downloaded the installer...")

            if not node_installer.exists():
                raise Exception("Node.js installer not found. Please download it manually.")

            # Install Node.js using interactive mode
            print("Installing Node.js (this may take a few minutes)...")
            print("Please follow the installation wizard when it appears...")
            install_process = subprocess.run(
                ["msiexec", "/i", str(node_installer)],
                check=True
            )
            
            print("Waiting for Node.js installation to complete...")
            time.sleep(10)
            
            # Set environment variables for the current process
            nodejs_paths = [
                r"C:\Program Files\nodejs",
                os.path.join(os.environ["APPDATA"], "npm")
            ]
            
            for nodejs_path in nodejs_paths:
                if nodejs_path not in os.environ["PATH"]:
                    os.environ["PATH"] = nodejs_path + os.pathsep + os.environ["PATH"]

            # Verify installation
            max_retries = 3
            for attempt in range(max_retries):
                try:
                    node_version = subprocess.run(["node", "--version"], capture_output=True, text=True, check=True).stdout.strip()
                    npm_version = subprocess.run(["npm", "--version"], capture_output=True, text=True, check=True).stdout.strip()
                    print(f"Successfully installed Node.js {node_version} with npm {npm_version}")
                    break
                except subprocess.CalledProcessError as e:
                    if attempt == max_retries - 1:
                        print("Warning: Node.js installation completed but verification failed")
                        print(f"Error: {str(e)}")
                        print("You may need to restart your system for the changes to take effect")
                        print("After restarting, run this script again")
                        raise Exception("Node.js verification failed")
                    else:
                        print(f"Waiting for Node.js to be available (attempt {attempt + 1}/{max_retries})...")
                        time.sleep(5)
            
            # Clean up
            self.safe_remove_directory(temp_dir)
            
        except Exception as e:
            print(f"Error installing Node.js: {str(e)}")
            raise

    def check_dependencies(self):
        """Check and install required dependencies"""
        print("Checking dependencies...")
        try:
            # Check Node.js and npm first
            node_installed = False
            try:
                # Use shell=True to find node in PATH
                node_version = subprocess.run("node --version", shell=True, check=True, capture_output=True, text=True).stdout.strip()
                npm_version = subprocess.run("npm --version", shell=True, check=True, capture_output=True, text=True).stdout.strip()
                
                # Check if version meets requirements
                if not node_version.startswith("v18."):
                    print(f"Node.js {node_version} found, but v18.18.0 is required")
                    self.install_nodejs()
                else:
                    print(f"Node.js {node_version} with npm {npm_version} is installed")
                    node_installed = True

            except Exception as e:
                print(f"Node.js/npm not found or error: {str(e)}")
                self.install_nodejs()
                node_installed = True  # If we get here, Node.js was installed successfully

            if not node_installed:
                raise Exception("Failed to install Node.js")

            # Install pnpm globally if not present
            try:
                pnpm_version = subprocess.run("pnpm --version", shell=True, check=True, capture_output=True, text=True).stdout.strip()
                print(f"pnpm version {pnpm_version} is installed")
            except:
                print("Installing pnpm globally...")
                subprocess.run("npm i -g pnpm", shell=True, check=True)
                pnpm_version = subprocess.run("pnpm --version", shell=True, check=True, capture_output=True, text=True).stdout.strip()
                print(f"Installed pnpm version {pnpm_version}")

            # Check git
            try:
                git_version = subprocess.run("git --version", shell=True, check=True, capture_output=True, text=True).stdout.strip()
                print(f"{git_version} is installed")
            except:
                raise Exception("git is not installed. Please install git from https://git-scm.com/downloads")

            print("All dependencies are satisfied")
        except Exception as e:
            print(f"Error checking dependencies: {str(e)}")
            raise

    def setup_steam_config(self):
        """Configure Steam for Decky Loader"""
        print("Configuring Steam...")
        try:
            # Add -dev argument to Steam shortcut
            import winreg
            steam_path = None
            
            # Try to find Steam installation path from registry
            try:
                with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam") as key:
                    steam_path = winreg.QueryValueEx(key, "InstallPath")[0]
            except:
                try:
                    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Valve\Steam") as key:
                        steam_path = winreg.QueryValueEx(key, "InstallPath")[0]
                except:
                    print("Steam installation not found in registry")
            
            if steam_path:
                steam_exe = Path(steam_path) / "steam.exe"
                if steam_exe.exists():
                    # Create .cef-enable-remote-debugging file
                    debug_file = Path(steam_path) / ".cef-enable-remote-debugging"
                    debug_file.touch()
                    print("Created .cef-enable-remote-debugging file")
                    
                    # Create/modify Steam shortcut
                    desktop = Path.home() / "Desktop"
                    shortcut_path = desktop / "Steam.lnk"
                    
                    import pythoncom
                    from win32com.client import Dispatch
                    
                    shell = Dispatch("WScript.Shell")
                    shortcut = shell.CreateShortCut(str(shortcut_path))
                    shortcut.Targetpath = str(steam_exe)
                    shortcut.Arguments = "-dev"
                    shortcut.save()
                    print("Created Steam shortcut with -dev argument")

        except Exception as e:
            print(f"Error configuring Steam: {str(e)}")
            raise

    def setup_autostart(self):
        """Setup PluginLoader to run at startup"""
        print("Setting up autostart...")
        try:
            # Get the path to the no-console executable
            services_dir = os.path.join(os.path.expanduser("~"), "homebrew", "services")
            plugin_loader = os.path.join(services_dir, "PluginLoader_noconsole.exe")

            # Get the Windows Startup folder path
            startup_folder = os.path.join(os.environ["APPDATA"], "Microsoft", "Windows", "Start Menu", "Programs", "Startup")
            
            # Create a batch file in the startup folder
            startup_bat = os.path.join(startup_folder, "start_decky.bat")
            
            # Write the batch file with proper path escaping
            with open(startup_bat, "w") as f:
                f.write(f'@echo off\n"{plugin_loader}"')

            print(f"Created startup script at: {startup_bat}")
            return True

        except Exception as e:
            print(f"Error setting up autostart: {str(e)}")
            return False

    def run(self):
        """Run the build process"""
        try:
            print("Starting Decky Loader build process...")
            self.check_dependencies()
            self.setup_directories()
            self.clone_repository()
            self.setup_homebrew()
            self.build_frontend()
            self.prepare_backend()
            self.install_requirements()
            self.build_executables()
            self.copy_to_homebrew()
            self.setup_steam_config()
            self.setup_autostart()
            print("\nBuild process completed successfully\!")
            print("\nNext steps:")
            print("1. Close Steam if it's running")
            print("2. Launch Steam using the new shortcut on your desktop")
            print("3. Enter Big Picture Mode")
            print("4. Hold the STEAM button and press A to access the Decky menu")
        except Exception as e:
            print(f"Error during build process: {str(e)}")
            raise

def main():
    parser = argparse.ArgumentParser(description='Build and Install Decky Loader for Windows')
    parser.add_argument('--release', required=False, default="main", 
                      help='Release version/branch to build (default: main)')
    args = parser.parse_args()

    try:
        builder = DeckyBuilder(args.release)
        builder.run()
        print(f"\nDecky Loader has been installed to: {builder.user_homebrew_dir}")
    except Exception as e:
        print(f"Error during build process: {str(e)}")
        sys.exit(1)

if __name__ == "__main__":
    main()