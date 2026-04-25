using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Socar.WinServicesManager;

public sealed class WindowsServiceInstallerWindow : Window
{
    private const string ServiceName = "SOCARWinServicesManagerProfileRunner";
    private const string ServiceDisplayName = "SOCAR WinServicesManager Profile Runner";

    private readonly TextBlock _installedText = new();
    private readonly TextBlock _runningText = new();
    private readonly Button _installButton = new() { Content = "Install service", MinWidth = 120 };
    private readonly Button _uninstallButton = new() { Content = "Uninstall service", MinWidth = 120 };

    public WindowsServiceInstallerWindow()
    {
        Title = "Windows Service";
        Width = 520;
        Height = 220;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildLayout();
        Loaded += (_, _) => RefreshStatus();
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(14) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        _installButton.Margin = new Thickness(0, 0, 8, 0);
        _installButton.Click += (_, _) => InstallService();
        _uninstallButton.Click += (_, _) => UninstallService();
        buttons.Children.Add(_installButton);
        buttons.Children.Add(_uninstallButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(panel, "Windows service installed:", 0, 0);
        Grid.SetRow(_installedText, 0);
        Grid.SetColumn(_installedText, 1);
        panel.Children.Add(_installedText);

        AddLabel(panel, "Windows service running:", 1, 0);
        Grid.SetRow(_runningText, 1);
        Grid.SetColumn(_runningText, 1);
        panel.Children.Add(_runningText);

        var pathText = new TextBlock
        {
            Text = $"Service executable: {ServiceExePath()}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Grid.SetRow(pathText, 2);
        Grid.SetColumnSpan(pathText, 2);
        panel.Children.Add(pathText);

        root.Children.Add(panel);
        return root;
    }

    private static void AddLabel(Grid grid, string text, int row, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 0, 10, 8),
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private void RefreshStatus()
    {
        var status = QueryStatus();
        _installedText.Text = status.Installed ? "yes" : "no";
        _runningText.Text = status.Running ? "running" : "not running";
        _installButton.IsEnabled = !status.Installed;
        _uninstallButton.IsEnabled = status.Installed;
    }

    private void InstallService()
    {
        var exePath = ServiceExePath();
        if (!File.Exists(exePath))
        {
            MessageBox.Show(this, $"Service executable was not found:{Environment.NewLine}{exePath}", "Install Service", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            RunSc("create", ServiceName, "binPath=", exePath, "start=", "auto", "obj=", "LocalSystem", "DisplayName=", ServiceDisplayName);
            RunSc("description", ServiceName, "Runs SOCAR WinServicesManager profiles requested by the tray app.");
            RunSc("start", ServiceName);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Install Service", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshStatus();
        }
    }

    private void UninstallService()
    {
        try
        {
            var status = QueryStatus();
            if (status.Running)
            {
                RunSc("stop", ServiceName, allowFailure: true);
            }

            RunSc("delete", ServiceName);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Uninstall Service", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshStatus();
        }
    }

    private static ServiceInstallStatus QueryStatus()
    {
        var result = RunSc("query", ServiceName, allowFailure: true);
        var installed = !result.Contains("FAILED 1060", StringComparison.OrdinalIgnoreCase);
        var running = installed && result.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        return new ServiceInstallStatus(installed, running);
    }

    private static string RunSc(params string[] arguments)
    {
        return RunSc(arguments, allowFailure: false);
    }

    private static string RunSc(string command, string serviceName, bool allowFailure = false)
    {
        return RunSc([command, serviceName], allowFailure);
    }

    private static string RunSc(string command, string serviceName, string argument, bool allowFailure = false)
    {
        return RunSc([command, serviceName, argument], allowFailure);
    }

    private static string RunSc(string command, string serviceName, params string[] arguments)
    {
        return RunSc([command, serviceName, .. arguments], allowFailure: false);
    }

    private static string RunSc(string[] arguments, bool allowFailure)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start sc.exe.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var combined = string.Join(Environment.NewLine, new[] { output, error }.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(combined);
        }

        return combined;
    }

    private static string ServiceExePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Socar.WinServicesManager.Service.exe");
    }

    private sealed record ServiceInstallStatus(bool Installed, bool Running);
}
