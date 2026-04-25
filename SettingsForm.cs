namespace Socar.WinServicesManager;

public sealed class SettingsForm : Form
{
    private readonly ComboBox _dependencyPolicyCombo = new();
    private readonly AppSettings _settings;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 150);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "When stopping a service with running dependent services",
            AutoSize = true
        }, 0, 0);

        _dependencyPolicyCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _dependencyPolicyCombo.Width = 470;
        _dependencyPolicyCombo.Items.Add(new PolicyOption("Automatically stop dependent services", DependencyStopPolicy.AutoStopDependents));
        _dependencyPolicyCombo.Items.Add(new PolicyOption("Warn and skip unless dependents are also in the profile", DependencyStopPolicy.WarnAndSkipUnlessInProfile));
        _dependencyPolicyCombo.Items.Add(new PolicyOption("Fail that service and continue", DependencyStopPolicy.FailAndContinue));
        _dependencyPolicyCombo.SelectedItem = _dependencyPolicyCombo.Items
            .Cast<PolicyOption>()
            .First(option => option.Policy == settings.DependencyStopPolicy);
        root.Controls.Add(_dependencyPolicyCombo, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        var saveButton = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 2);

        Controls.Add(root);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult == DialogResult.OK && _dependencyPolicyCombo.SelectedItem is PolicyOption option)
        {
            _settings.DependencyStopPolicy = option.Policy;
        }
    }

    private sealed record PolicyOption(string Label, DependencyStopPolicy Policy)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
