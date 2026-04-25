using System.Windows;
using System.Windows.Controls;

namespace Socar.WinServicesManager;

public sealed class PromptWindow : Window
{
    private readonly TextBox _textBox = new();

    public PromptWindow(string title, string label, string initialValue)
    {
        Title = title;
        Width = 440;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var okButton = new Button { Content = "OK", MinWidth = 84, Margin = new Thickness(0, 0, 6, 0) };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        okButton.Click += (_, _) => DialogResult = true;
        cancelButton.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
        _textBox.Text = initialValue;
        _textBox.SelectAll();
        panel.Children.Add(_textBox);
        root.Children.Add(panel);

        Content = root;
        Loaded += (_, _) => _textBox.Focus();
    }

    public string Value => _textBox.Text.Trim();
}
