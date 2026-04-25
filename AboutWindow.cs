using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Socar.WinServicesManager;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "About SOCAR WinServicesManager";
        Width = 460;
        Height = 260;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildLayout();
    }

    private static UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var closeButton = new Button
        {
            Content = "Close",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        closeButton.Click += (_, _) => Window.GetWindow(closeButton)?.Close();
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "SOCAR WinServicesManager",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "(c) SOCAR Software 2026",
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Version: {Assembly.GetExecutingAssembly().GetName().Version}",
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Windows service profile manager, tray runner, background service, and XML/CLI tooling.",
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(panel);
        return root;
    }
}
