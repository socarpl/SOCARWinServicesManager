using System.ComponentModel;

namespace Socar.WinServicesManager;

public sealed class ProfilesForm : Form
{
    private readonly ProfileRepository _repository;
    private readonly NativeServiceApi _serviceApi;
    private readonly BindingList<ProfileRow> _rows = [];
    private readonly DataGridView _grid = new();
    private readonly Label _databaseLabel = new();
    private readonly Button _createButton = new();
    private readonly Button _editButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _runButton = new();
    private readonly Button _settingsButton = new();
    private readonly Button _servicesButton = new();

    public ProfilesForm(ProfileRepository repository, NativeServiceApi serviceApi)
    {
        _repository = repository;
        _serviceApi = serviceApi;

        Text = "Windows Service Profiles";
        MinimumSize = new Size(760, 480);
        Size = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        WireEvents();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LoadProfiles();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };

        _createButton.Text = "Create";
        _editButton.Text = "Edit";
        _deleteButton.Text = "Delete";
        _runButton.Text = "Run";
        _settingsButton.Text = "Settings";
        _servicesButton.Text = "Service manager";

        toolbar.Controls.Add(_createButton);
        toolbar.Controls.Add(_editButton);
        toolbar.Controls.Add(_deleteButton);
        toolbar.Controls.Add(_runButton);
        toolbar.Controls.Add(_settingsButton);
        toolbar.Controls.Add(_servicesButton);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProfileRow.Name), HeaderText = "Profile", FillWeight = 45 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProfileRow.ActionCount), HeaderText = "Services", FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ProfileRow.UpdatedAt), HeaderText = "Updated", FillWeight = 25 });
        _grid.DataSource = _rows;

        _databaseLabel.AutoSize = true;
        _databaseLabel.Padding = new Padding(0, 8, 0, 0);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_databaseLabel, 0, 2);
        Controls.Add(root);
    }

    private void WireEvents()
    {
        _createButton.Click += (_, _) => CreateProfile();
        _editButton.Click += (_, _) => EditProfile();
        _deleteButton.Click += (_, _) => DeleteProfile();
        _runButton.Click += (_, _) => RunProfile();
        _settingsButton.Click += (_, _) => EditSettings();
        _servicesButton.Click += (_, _) => new MainForm().Show(this);
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += (_, _) => EditProfile();
    }

    private void LoadProfiles()
    {
        _rows.Clear();
        foreach (var profile in _repository.GetProfiles())
        {
            var fullProfile = _repository.GetProfile(profile.Id);
            _rows.Add(new ProfileRow(fullProfile.Id, fullProfile.Name, fullProfile.Actions.Count, fullProfile.UpdatedAt.ToLocalTime().ToString("G")));
        }

        _databaseLabel.Text = $"Database: {Path.Combine(AppContext.BaseDirectory, "service-profiles.db")}";
        UpdateButtons();
    }

    private void CreateProfile()
    {
        using var editor = new ProfileEditorForm(_serviceApi, null);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            SaveProfile(editor.Profile);
        }
    }

    private void EditProfile()
    {
        var selected = SelectedProfile();
        if (selected is null)
        {
            return;
        }

        using var editor = new ProfileEditorForm(_serviceApi, _repository.GetProfile(selected.Id));
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            SaveProfile(editor.Profile);
        }
    }

    private void DeleteProfile()
    {
        var selected = SelectedProfile();
        if (selected is null)
        {
            return;
        }

        var result = MessageBox.Show(this, $"Delete profile '{selected.Name}'?", "Delete Profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            return;
        }

        _repository.DeleteProfile(selected.Id);
        LoadProfiles();
    }

    private void RunProfile()
    {
        var selected = SelectedProfile();
        if (selected is null)
        {
            return;
        }

        var result = new ProfileRunner(_serviceApi).Run(_repository.GetProfile(selected.Id), _repository.GetSettings());
        using var resultForm = new RunResultForm(result);
        resultForm.ShowDialog(this);
    }

    private void EditSettings()
    {
        var settings = _repository.GetSettings();
        using var settingsForm = new SettingsForm(settings);
        if (settingsForm.ShowDialog(this) == DialogResult.OK)
        {
            _repository.SaveSettings(settings);
        }
    }

    private void SaveProfile(ServiceProfile profile)
    {
        try
        {
            _repository.SaveProfile(profile);
            LoadProfiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save Profile", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private ProfileRow? SelectedProfile()
    {
        return _grid.CurrentRow?.DataBoundItem as ProfileRow;
    }

    private void UpdateButtons()
    {
        var hasSelection = SelectedProfile() is not null;
        _editButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
        _runButton.Enabled = hasSelection;
    }

    private sealed record ProfileRow(long Id, string Name, int ActionCount, string UpdatedAt);
}
