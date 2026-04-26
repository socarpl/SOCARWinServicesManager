using System.Diagnostics;
using System.IO.Pipes;
using Socar.WinServicesManager;

namespace Socar.WinServicesManager.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "SOCAR WinServicesManager",
            Visible = true
        };
        _notifyIcon.ContextMenuStrip = BuildMenu();
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _notifyIcon.ContextMenuStrip = BuildMenu();
                _notifyIcon.ContextMenuStrip.Show(Cursor.Position);
            }
        };
    }

    private static Icon LoadTrayIcon()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? SystemIcons.Application;
        }

        return SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        try
        {
            var repository = new ProfileRepository();
            repository.Initialize();
            var profiles = repository.GetProfiles();

            if (profiles.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("No profiles found") { Enabled = false });
            }
            else
            {
                foreach (var profile in profiles)
                {
                    var item = new ToolStripMenuItem(profile.Name)
                    {
                        Tag = profile.Id
                    };
                    item.Click += async (_, _) => await RunProfileAsync(profile.Id, profile.Name);
                    menu.Items.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            menu.Items.Add(new ToolStripMenuItem($"Could not read profiles: {ex.Message}") { Enabled = false });
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open main app", null, (_, _) => OpenMainApp());
        menu.Items.Add("Refresh menu", null, (_, _) => _notifyIcon.ContextMenuStrip = BuildMenu());
        menu.Items.Add("About", null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        return menu;
    }

    private async Task RunProfileAsync(long profileId, string profileName)
    {
        try
        {
            var response = await SendRunProfileRequestAsync(profileId);
            _notifyIcon.ShowBalloonTip(
                5000,
                "Profile run completed",
                response.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
                    ? $"'{profileName}' was sent to the service."
                    : response,
                response.StartsWith("OK", StringComparison.OrdinalIgnoreCase) ? ToolTipIcon.Info : ToolTipIcon.Error);
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(8000, "Profile run failed", ex.Message, ToolTipIcon.Error);
        }
    }

    private static async Task<string> SendRunProfileRequestAsync(long profileId)
    {
        await using var pipe = new NamedPipeClientStream(".", SharedRuntimeConfig.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pipe.ConnectAsync(timeout.Token);

        await using var writer = new StreamWriter(pipe, leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(pipe, leaveOpen: true);

        await writer.WriteLineAsync(IpcMessages.RunProfile(profileId));
        var response = await reader.ReadToEndAsync(timeout.Token);
        return string.IsNullOrWhiteSpace(response) ? "ERROR: Empty response from service." : response.Trim();
    }

    private static void OpenMainApp()
    {
        var appPath = SharedRuntimeConfig.ResolveMainAppPath();
        if (!File.Exists(appPath))
        {
            MessageBox.Show(
                $"Main app was not found at:{Environment.NewLine}{appPath}{Environment.NewLine}{Environment.NewLine}Set {SharedRuntimeConfig.MainAppPathEnvironmentVariable} if it is installed elsewhere.",
                "SOCAR WinServicesManager Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = appPath,
            UseShellExecute = true
        });
    }

    private static void ShowAbout()
    {
        MessageBox.Show(
            "SOCAR WinServicesManager\r\n(c) SOCAR Software 2026\r\n\r\nTray profile launcher for SOCAR WinServicesManager.",
            "About SOCAR WinServicesManager",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
