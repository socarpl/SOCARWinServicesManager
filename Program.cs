using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Socar.WinServicesManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!EnsureAdministrator())
        {
            return;
        }

        SharedRuntimeConfig.EnsureMainAppConfig();
        var repository = new ProfileRepository();
        repository.Initialize();
        var app = new Application();
        app.Run(new ProfilesWindow(repository, new NativeServiceApi()));
    }

    private static bool EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return true;
        }

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                MessageBox.Show("This application must be run as administrator.", "SOCAR WinServicesManager", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch
        {
            MessageBox.Show("This application must be run as administrator.", "SOCAR WinServicesManager", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return false;
    }
}
