using System.Windows;
using System.Windows.Controls;

namespace Socar.WinServicesManager;

public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ComboBox _policyCombo = new();

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "Settings";
        Width = 560;
        Height = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildLayout();
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var saveButton = new Button { Content = "Save", MinWidth = 84, Margin = new Thickness(0, 0, 6, 0) };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        saveButton.Click += (_, _) => Save();
        cancelButton.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "When stopping a service with running dependent services", Margin = new Thickness(0, 0, 0, 6) });
        _policyCombo.Items.Add(new PolicyOption("Automatically stop dependent services", DependencyStopPolicy.AutoStopDependents));
        _policyCombo.Items.Add(new PolicyOption("Warn and skip unless dependents are also in the profile", DependencyStopPolicy.WarnAndSkipUnlessInProfile));
        _policyCombo.Items.Add(new PolicyOption("Fail that service and continue", DependencyStopPolicy.FailAndContinue));
        _policyCombo.SelectedItem = _policyCombo.Items.Cast<PolicyOption>().First(option => option.Policy == _settings.DependencyStopPolicy);
        panel.Children.Add(_policyCombo);
        root.Children.Add(panel);
        return root;
    }

    private void Save()
    {
        if (_policyCombo.SelectedItem is PolicyOption option)
        {
            _settings.DependencyStopPolicy = option.Policy;
        }

        DialogResult = true;
    }

    private sealed record PolicyOption(string Label, DependencyStopPolicy Policy)
    {
        public override string ToString() => Label;
    }
}
