using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Socar.WinServicesManager;

public sealed class ServiceManagerWindow : Window
{
    private readonly NativeServiceApi _serviceApi;
    private readonly ObservableCollection<ServiceSummary> _visibleServices = [];
    private readonly List<ServiceSummary> _services = [];
    private readonly DataGrid _grid = new();
    private readonly TextBox _searchTextBox = new();
    private readonly TextBox _detailsTextBox = new();
    private readonly TreeView _dependencyTree = new();
    private GraphWindow? _graphWindow;

    public ServiceManagerWindow(NativeServiceApi serviceApi)
    {
        _serviceApi = serviceApi;
        Title = "SOCAR WinServicesManager - Service Manager";
        MinWidth = 980;
        MinHeight = 620;
        Width = 1180;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildLayout();
        Loaded += async (_, _) => await RefreshServicesAsync();
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(12) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _searchTextBox.Width = 340;
        _searchTextBox.Margin = new Thickness(0, 0, 6, 0);
        var refreshButton = new Button { Content = "Refresh", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var graphButton = new Button { Content = "Graph window", MinWidth = 110, Margin = new Thickness(0, 0, 6, 0) };
        var startButton = new Button { Content = "Start", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var stopButton = new Button { Content = "Stop", MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var restartButton = new Button { Content = "Restart", MinWidth = 80 };
        var aboutButton = new Button { Content = "About", MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };

        toolbar.Children.Add(_searchTextBox);
        foreach (var button in new[] { refreshButton, graphButton, startButton, stopButton, restartButton, aboutButton })
        {
            toolbar.Children.Add(button);
        }
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var split = new Grid();
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.ItemsSource = _visibleServices;
        _grid.Columns.Add(new DataGridTextColumn { Header = "Display name", Binding = new Binding(nameof(ServiceSummary.DisplayName)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Service name", Binding = new Binding(nameof(ServiceSummary.Name)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(ServiceSummary.State)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Startup", Binding = new Binding(nameof(ServiceSummary.StartType)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Account", Binding = new Binding(nameof(ServiceSummary.Account)), Width = new DataGridLength(1.4, DataGridLengthUnitType.Star) });
        _grid.SelectionChanged += (_, _) => UpdateDetails();
        Grid.SetRow(_grid, 0);
        split.Children.Add(_grid);

        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
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
        refreshButton.Click += async (_, _) => await RefreshServicesAsync();
        graphButton.Click += (_, _) => ShowGraphWindow();
        startButton.Click += async (_, _) => await RunServiceActionAsync(service => _serviceApi.Start(service.Name));
        stopButton.Click += async (_, _) => await RunServiceActionAsync(service => _serviceApi.Stop(service.Name));
        restartButton.Click += async (_, _) => await RunServiceActionAsync(service =>
        {
            if (service.State == ServiceRunState.Running)
            {
                _serviceApi.Stop(service.Name);
            }
            _serviceApi.Start(service.Name);
        });
        aboutButton.Click += (_, _) => new AboutWindow { Owner = this }.ShowDialog();

        return root;
    }

    private async Task RefreshServicesAsync()
    {
        _services.Clear();
        _services.AddRange(await Task.Run(_serviceApi.GetServices));
        ApplyFilter();
        SyncGraph();
    }

    private void ApplyFilter()
    {
        var filter = _searchTextBox.Text.Trim();
        var rows = string.IsNullOrWhiteSpace(filter)
            ? _services
            : _services.Where(service =>
                service.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                service.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                (service.Account?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();

        _visibleServices.Clear();
        foreach (var service in rows.OrderBy(service => service.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            _visibleServices.Add(service);
        }
        UpdateDetails();
    }

    private async Task RunServiceActionAsync(Action<ServiceSummary> action)
    {
        if (_grid.SelectedItem is not ServiceSummary service)
        {
            return;
        }

        try
        {
            await Task.Run(() => action(service));
            await RefreshServicesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Service Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateDetails()
    {
        var service = _grid.SelectedItem as ServiceSummary;
        _detailsTextBox.Text = service is null ? string.Empty : ServiceDetailsFormatter.FormatDetails(service);
        DependencyTreeBuilder.Populate(_dependencyTree, service, FindService);
        _graphWindow?.SelectService(service?.Name);
    }

    private ServiceSummary? FindService(string serviceName)
    {
        return _services.FirstOrDefault(service => service.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowGraphWindow()
    {
        if (_graphWindow is null || !_graphWindow.IsLoaded)
        {
            _graphWindow = new GraphWindow();
            _graphWindow.Owner = this;
            _graphWindow.ServiceSelected += (_, serviceName) => SelectService(serviceName);
            _graphWindow.Closed += (_, _) => _graphWindow = null;
            _graphWindow.Show();
        }
        else
        {
            _graphWindow.Activate();
        }

        SyncGraph();
    }

    private void SyncGraph()
    {
        _graphWindow?.SetServices(_services, (_grid.SelectedItem as ServiceSummary)?.Name);
    }

    private void SelectService(string serviceName)
    {
        var service = _visibleServices.FirstOrDefault(item => item.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            _searchTextBox.Clear();
            ApplyFilter();
            service = _visibleServices.FirstOrDefault(item => item.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        }

        if (service is not null)
        {
            _grid.SelectedItem = service;
            _grid.ScrollIntoView(service);
        }
    }
}
