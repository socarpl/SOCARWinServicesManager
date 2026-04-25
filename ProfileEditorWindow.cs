using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Socar.WinServicesManager;

public sealed class ProfileEditorWindow : Window
{
    private readonly NativeServiceApi _serviceApi;
    private readonly ServiceProfile _profile;
    private readonly List<ServiceSummary> _services = [];
    private readonly List<ServiceActionEditRow> _allRows = [];
    private readonly ObservableCollection<ServiceActionEditRow> _visibleRows = [];
    private readonly TextBox _nameTextBox = new();
    private readonly TextBox _searchTextBox = new();
    private readonly CheckBox _hideMicrosoftCheckBox = new() { Content = "Hide all Microsoft services", IsChecked = true };
    private readonly DataGrid _grid = new();
    private readonly TextBox _detailsTextBox = new();
    private readonly TreeView _dependencyTree = new();

    public ProfileEditorWindow(NativeServiceApi serviceApi, ServiceProfile? profile)
    {
        _serviceApi = serviceApi;
        _profile = profile ?? new ServiceProfile { Name = string.Empty, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

        Title = profile is null ? "Create Profile" : "Edit Profile";
        MinWidth = 980;
        MinHeight = 640;
        Width = 1200;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildLayout();
        LoadServices();
        ApplyFilter();
    }

    public ServiceProfile Profile => _profile;

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var saveButton = new Button { Content = "Save", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0) };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };
        saveButton.Click += (_, _) => Save();
        cancelButton.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        namePanel.Children.Add(new TextBlock { Text = "Profile name", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _nameTextBox.Width = 360;
        _nameTextBox.Text = _profile.Name;
        namePanel.Children.Add(_nameTextBox);
        top.Children.Add(namePanel);
        _searchTextBox.Margin = new Thickness(0, 0, 0, 6);
        _searchTextBox.ToolTip = "Search services";
        top.Children.Add(_searchTextBox);
        top.Children.Add(_hideMicrosoftCheckBox);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var split = new Grid();
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.ItemsSource = _visibleRows;
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Use", Binding = new Binding(nameof(ServiceActionEditRow.Include)), Width = 54 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Display name", Binding = new Binding(nameof(ServiceActionEditRow.DisplayName)), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Service name", Binding = new Binding(nameof(ServiceActionEditRow.ServiceName)), IsReadOnly = true, Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Current startup", Binding = new Binding(nameof(ServiceActionEditRow.CurrentStartType)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Current status", Binding = new Binding(nameof(ServiceActionEditRow.CurrentStatus)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridComboBoxColumn { Header = "New startup", SelectedItemBinding = new Binding(nameof(ServiceActionEditRow.DesiredStartType)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = WpfUi.StartupOptions, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridComboBoxColumn { Header = "New status", SelectedItemBinding = new Binding(nameof(ServiceActionEditRow.DesiredStatus)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = WpfUi.StatusOptions, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.SelectionChanged += (_, _) => UpdateSelectedServiceDetails();
        Grid.SetRow(_grid, 0);
        split.Children.Add(_grid);

        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(splitter, 1);
        split.Children.Add(splitter);

        var tabs = new TabControl();
        _detailsTextBox.IsReadOnly = true;
        _detailsTextBox.AcceptsReturn = true;
        _detailsTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _detailsTextBox.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        tabs.Items.Add(new TabItem { Header = "Details", Content = _detailsTextBox });
        tabs.Items.Add(new TabItem { Header = "Dependency tree", Content = _dependencyTree });
        Grid.SetRow(tabs, 2);
        split.Children.Add(tabs);

        root.Children.Add(split);
        _searchTextBox.TextChanged += (_, _) => ApplyFilter();
        _hideMicrosoftCheckBox.Checked += (_, _) => ApplyFilter();
        _hideMicrosoftCheckBox.Unchecked += (_, _) => ApplyFilter();
        return root;
    }

    private void LoadServices()
    {
        var savedActions = _profile.Actions.ToDictionary(action => action.ServiceName, StringComparer.OrdinalIgnoreCase);
        _services.AddRange(_serviceApi.GetServices());

        foreach (var service in _services)
        {
            savedActions.TryGetValue(service.Name, out var action);
            _allRows.Add(new ServiceActionEditRow
            {
                Include = action is not null,
                ServiceName = service.Name,
                DisplayName = service.DisplayName,
                CurrentStartType = service.StartType?.ToString() ?? "Unknown",
                CurrentStatus = service.State.ToString(),
                DesiredStartType = action?.DesiredStartType?.ToString() ?? WpfUi.Unchanged,
                DesiredStatus = action?.DesiredStatus?.ToString() ?? WpfUi.Unchanged,
                BinaryPath = service.BinaryPath,
                IsMicrosoftService = IsMicrosoftService(service)
            });
        }
    }

    private void ApplyFilter()
    {
        var filter = _searchTextBox.Text.Trim();
        var rows = _allRows.AsEnumerable();
        if (_hideMicrosoftCheckBox.IsChecked == true)
        {
            rows = rows.Where(row => !row.IsMicrosoftService);
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            rows = rows.Where(row =>
                row.ServiceName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                row.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        }

        _visibleRows.Clear();
        foreach (var row in rows.OrderBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _visibleRows.Add(row);
        }
        CollectionViewSource.GetDefaultView(_grid.ItemsSource)?.Refresh();
        UpdateSelectedServiceDetails();
    }

    private void Save()
    {
        var profileName = _nameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            MessageBox.Show(this, "Profile name is required.", "Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var actions = _allRows
            .Where(row => row.Include && (row.DesiredStartType != WpfUi.Unchanged || row.DesiredStatus != WpfUi.Unchanged))
            .Select(row => new ProfileServiceAction
            {
                ProfileId = _profile.Id,
                ServiceName = row.ServiceName,
                DisplayName = row.DisplayName,
                DesiredStartType = Enum.TryParse<ServiceStartType>(row.DesiredStartType, out var startType) ? startType : null,
                DesiredStatus = Enum.TryParse<DesiredServiceStatus>(row.DesiredStatus, out var status) ? status : null
            })
            .ToList();

        if (actions.Count == 0)
        {
            MessageBox.Show(this, "Select at least one service action.", "Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profile.Name = profileName;
        _profile.Actions = actions;
        DialogResult = true;
    }

    private void UpdateSelectedServiceDetails()
    {
        var service = SelectedService();
        _detailsTextBox.Text = service is null ? string.Empty : ServiceDetailsFormatter.FormatDetails(service);
        DependencyTreeBuilder.Populate(_dependencyTree, service, FindService);
    }

    private ServiceSummary? SelectedService()
    {
        return _grid.SelectedItem is ServiceActionEditRow row ? FindService(row.ServiceName) : null;
    }

    private ServiceSummary? FindService(string serviceName)
    {
        return _services.FirstOrDefault(service => service.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMicrosoftService(ServiceSummary service)
    {
        var binaryPath = service.BinaryPath ?? string.Empty;
        var account = service.Account ?? string.Empty;
        return binaryPath.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase) ||
               binaryPath.Contains(@"%SystemRoot%", StringComparison.OrdinalIgnoreCase) ||
               binaryPath.Contains(@"%windir%", StringComparison.OrdinalIgnoreCase) ||
               binaryPath.Contains(@"Microsoft", StringComparison.OrdinalIgnoreCase) ||
               account.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase) &&
               service.Name.StartsWith("Win", StringComparison.OrdinalIgnoreCase);
    }
}
