using System.Windows;
using System.Windows.Controls;

namespace Socar.WinServicesManager;

public sealed class RunResultWindow : Window
{
    public RunResultWindow(string resultText)
    {
        Title = "Profile Run Result";
        Width = 780;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        var closeButton = new Button { Content = "Close", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Bottom);
        root.Children.Add(closeButton);

        root.Children.Add(new TextBox
        {
            Text = resultText,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas")
        });

        Content = root;
    }
}
