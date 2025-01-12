using System;
using System.Security.Principal;
using System.Windows.Forms;

namespace DeckyInstaller
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Check for admin privileges
            if (!IsAdministrator())
            {
                MessageBox.Show(
                    "This application requires administrator privileges to create the required files.",
                    "Administrator Rights Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }

        private static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
