using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeckyInstaller
{
    internal static class Program
    {
        [STAThread]
        static async Task Main(string[] args)
        {
            // Check for admin privileges
            if (!IsAdministrator())
            {
                MessageBox.Show(
                    "This application requires administrator privileges to install dependencies and make system changes.",
                    "Administrator Rights Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            ApplicationConfiguration.Initialize();

            if (args.Length > 0)
            {
                // Check for --release or -r argument
                int versionIndex = Array.FindIndex(args, arg => arg == "--release" || arg == "-r");
                if (versionIndex >= 0 && versionIndex + 1 < args.Length)
                {
                    string version = args[versionIndex + 1];
                    var form = new MainForm(version);
                    await form.StartInstallation();
                    return;
                }
                else
                {
                    Console.WriteLine("Usage: DeckyInstaller.exe --release <version>");
                    Console.WriteLine("Example: DeckyInstaller.exe --release v3.0.5");
                    return;
                }
            }

            Application.Run(new MainForm());
        }

        private static bool IsAdministrator()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}
