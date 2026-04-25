using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace Socar.WinServicesManager;

public sealed class ProfilesWindow : Window
{
    private readonly ProfileRepository _repository;
    private readonly NativeServiceApi _serviceApi;
    private readonly ObservableCollection<ProfileListRow> _profiles = [];
    private readonly DataGrid _grid = new();
    private readonly TextBlock _databaseText = new();
    private readonly MenuItem _editMenuItem = new() { Header = "Edit selected" };
    private readonly MenuItem _deleteMenuItem = new() { Header = "Delete selected" };
    private readonly MenuItem _runMenuItem = new() { Header = "Run selected" };
    private readonly MenuItem _exportMenuItem = new() { Header = "Export selected to XML" };
    private GraphWindow? _graphWindow;

    public ProfilesWindow(ProfileRepository repository, NativeServiceApi serviceApi)
    {
        _repository = repository;
        _serviceApi = serviceApi;

        Title = "SOCAR WinServicesManager Profiles";
        MinWidth = 780;
        MinHeight = 480;
        Width = 920;
        Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildLayout();
        Loaded += (_, _) => LoadProfiles();
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        _databaseText.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_databaseText, Dock.Bottom);
        root.Children.Add(_databaseText);

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.ItemsSource = _profiles;
        _grid.Columns.Add(new DataGridTextColumn { Header = "Profile", Binding = new Binding(nameof(ProfileListRow.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Services", Binding = new Binding(nameof(ProfileListRow.ActionCount)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Updated", Binding = new Binding(nameof(ProfileListRow.UpdatedAt)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.MouseDoubleClick += (_, _) => EditProfile();
        root.Children.Add(_grid);

        return root;
    }

    private Menu BuildMenu()
    {
        var menu = new Menu { Margin = new Thickness(0, 0, 0, 8) };

        var profileMenu = new MenuItem { Header = "_Profile" };
        var createItem = new MenuItem { Header = "_Create" };
        var snapshotItem = new MenuItem { Header = "_Snapshot current system" };
        var importItem = new MenuItem { Header = "_Import XML" };
        createItem.Click += (_, _) => CreateProfile();
        snapshotItem.Click += async (_, _) => await CreateSnapshotProfileAsync();
        _editMenuItem.Click += (_, _) => EditProfile();
        _deleteMenuItem.Click += (_, _) => DeleteProfile();
        _runMenuItem.Click += (_, _) => RunProfile();
        _exportMenuItem.Click += (_, _) => ExportProfile();
        importItem.Click += (_, _) => ImportProfile();
        profileMenu.Items.Add(createItem);
        profileMenu.Items.Add(snapshotItem);
        profileMenu.Items.Add(new Separator());
        profileMenu.Items.Add(_editMenuItem);
        profileMenu.Items.Add(_deleteMenuItem);
        profileMenu.Items.Add(_runMenuItem);
        profileMenu.Items.Add(new Separator());
        profileMenu.Items.Add(_exportMenuItem);
        profileMenu.Items.Add(importItem);

        var toolsMenu = new MenuItem { Header = "_Tools" };
        var settingsItem = new MenuItem { Header = "_Settings" };
        var serviceManagerItem = new MenuItem { Header = "_Service manager" };
        var graphItem = new MenuItem { Header = "_Graph" };
        settingsItem.Click += (_, _) => EditSettings();
        serviceManagerItem.Click += (_, _) => new ServiceManagerWindow(_serviceApi).Show();
        graphItem.Click += async (_, _) => await ShowGraphAsync();
        toolsMenu.Items.Add(settingsItem);
        toolsMenu.Items.Add(serviceManagerItem);
        toolsMenu.Items.Add(graphItem);

        var serviceMenu = new MenuItem { Header = "_Windows Service" };
        var installServiceItem = new MenuItem { Header = "_Install / uninstall service" };
        installServiceItem.Click += (_, _) => new WindowsServiceInstallerWindow { Owner = this }.ShowDialog();
        serviceMenu.Items.Add(installServiceItem);

        var helpMenu = new MenuItem { Header = "_Help" };
        var aboutItem = new MenuItem { Header = "_About" };
        aboutItem.Click += (_, _) => new AboutWindow { Owner = this }.ShowDialog();
        helpMenu.Items.Add(aboutItem);

        menu.Items.Add(profileMenu);
        menu.Items.Add(toolsMenu);
        menu.Items.Add(serviceMenu);
        menu.Items.Add(helpMenu);
        return menu;
    }

    private void LoadProfiles()
    {
        _profiles.Clear();
        foreach (var profile in _repository.GetProfiles())
        {
            var fullProfile = _repository.GetProfile(profile.Id);
            _profiles.Add(new ProfileListRow(fullProfile.Id, fullProfile.Name, fullProfile.Actions.Count, fullProfile.UpdatedAt.ToLocalTime().ToString("G")));
        }

        _databaseText.Text = $"Database: {_repository.DatabasePath}";
        UpdateButtons();
    }

    private void CreateProfile()
    {
        var editor = new ProfileEditorWindow(_serviceApi, null) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            SaveProfile(editor.Profile);
        }
    }

    private async Task CreateSnapshotProfileAsync()
    {
        var prompt = new PromptWindow(
            "Create Snapshot Profile",
            "Snapshot profile name",
            $"Snapshot {DateTime.Now:yyyy-MM-dd HH-mm-ss}")
        {
            Owner = this
        };

        if (prompt.ShowDialog() != true)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt.Value))
        {
            MessageBox.Show(this, "Snapshot profile name is required.", "Snapshot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsEnabled = false;
            var services = await Task.Run(_serviceApi.GetServices);
            var profile = new ServiceProfile
            {
                Name = prompt.Value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Actions = services
                    .Where(service => service.StartType is not null)
                    .Select(service => new ProfileServiceAction
                    {
                        ServiceName = service.Name,
                        DisplayName = service.DisplayName,
                        DesiredStartType = service.StartType,
                        DesiredStatus = service.State == ServiceRunState.Running
                            ? DesiredServiceStatus.Running
                            : DesiredServiceStatus.Stopped
                    })
                    .ToList()
            };

            SaveProfile(profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Snapshot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void EditProfile()
    {
        var selected = SelectedProfile();
        if (selected is null)
        {
            return;
        }

        var editor = new ProfileEditorWindow(_serviceApi, _repository.GetProfile(selected.Id)) { Owner = this };
        if (editor.ShowDialog() == true)
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

        if (MessageBox.Show(this, $"Delete profile '{selected.Name}'?", "Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
        new RunResultWindow(result) { Owner = this }.ShowDialog();
    }

    private void ExportProfile()
    {
        var selected = SelectedProfile();
        if (selected is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Profile to XML",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            FileName = $"{SanitizeFileName(selected.Name)}.xml"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ProfileXmlSerializer.Save(_repository.GetProfile(selected.Id), dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportProfile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Profile XML",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var profile = ProfileXmlSerializer.Load(dialog.FileName);
            profile.Name = UniqueProfileName(profile.Name);
            _repository.SaveProfile(profile);
            LoadProfiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditSettings()
    {
        var settings = _repository.GetSettings();
        var settingsWindow = new SettingsWindow(settings) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            _repository.SaveSettings(settings);
        }
    }

    private async Task ShowGraphAsync()
    {
        try
        {
            if (_graphWindow is null || !_graphWindow.IsLoaded)
            {
                _graphWindow = new GraphWindow { Owner = this };
                _graphWindow.Closed += (_, _) => _graphWindow = null;
                _graphWindow.Show();
            }
            else
            {
                _graphWindow.Activate();
            }

            var services = await Task.Run(_serviceApi.GetServices);
            _graphWindow.SetServices(services, null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Graph", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(this, ex.Message, "Save Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ProfileListRow? SelectedProfile()
    {
        return _grid.SelectedItem as ProfileListRow;
    }

    private void UpdateButtons()
    {
        var hasSelection = SelectedProfile() is not null;
        _editMenuItem.IsEnabled = hasSelection;
        _deleteMenuItem.IsEnabled = hasSelection;
        _runMenuItem.IsEnabled = hasSelection;
        _exportMenuItem.IsEnabled = hasSelection;
    }

    private string UniqueProfileName(string requestedName)
    {
        var existingNames = _repository.GetProfiles()
            .Select(profile => profile.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        if (!existingNames.Contains(requestedName))
        {
            return requestedName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{requestedName} ({i})";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidCharacter, '_');
        }

        return fileName;
    }
}
