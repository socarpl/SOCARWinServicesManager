using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Socar.WinServicesManager;

public sealed class MainForm : Form
{
    private readonly NativeServiceApi _serviceApi = new();
    private readonly BindingList<ServiceGridRow> _rows = [];
    private readonly BindingSource _bindingSource = new();

    private readonly DataGridView _grid = new();
    private readonly TextBox _searchTextBox = new();
    private readonly TextBox _detailsTextBox = new();
    private readonly TreeView _dependencyTree = new();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _graphButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _restartButton = new();

    private ServiceGraphForm? _graphForm;
    private List<ServiceSummary> _services = [];
    private string _sortProperty = nameof(ServiceGridRow.DisplayName);
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public MainForm()
    {
        Text = "SOCAR WinServicesManager";
        MinimumSize = new Size(980, 620);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        WireEvents();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SetStatus(IsElevated()
            ? "Running as administrator."
            : "Read-only actions work without elevation. Start and stop may require administrator rights.");
        await RefreshServicesAsync();
        ShowGraphWindow();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };

        _searchTextBox.Width = 360;
        _searchTextBox.PlaceholderText = "Search services";

        _refreshButton.Text = "Refresh";
        _graphButton.Text = "Graph window";
        _startButton.Text = "Start";
        _stopButton.Text = "Stop";
        _restartButton.Text = "Restart";

        toolbar.Controls.Add(_searchTextBox);
        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_graphButton);
        toolbar.Controls.Add(_startButton);
        toolbar.Controls.Add(_stopButton);
        toolbar.Controls.Add(_restartButton);

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

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ServiceGridRow.DisplayName),
            HeaderText = "Display name",
            SortMode = DataGridViewColumnSortMode.Programmatic,
            FillWeight = 34
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ServiceGridRow.Name),
            HeaderText = "Service name",
            SortMode = DataGridViewColumnSortMode.Programmatic,
            FillWeight = 24
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ServiceGridRow.State),
            HeaderText = "Status",
            SortMode = DataGridViewColumnSortMode.Programmatic,
            FillWeight = 14
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ServiceGridRow.StartType),
            HeaderText = "Startup",
            SortMode = DataGridViewColumnSortMode.Programmatic,
            FillWeight = 14
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ServiceGridRow.Account),
            HeaderText = "Account",
            SortMode = DataGridViewColumnSortMode.Programmatic,
            FillWeight = 24
        });

        var lowerTabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        var detailsTab = new TabPage("Details");
        var dependencyTab = new TabPage("Dependency tree");

        _detailsTextBox.Dock = DockStyle.Fill;
        _detailsTextBox.Multiline = true;
        _detailsTextBox.ReadOnly = true;
        _detailsTextBox.ScrollBars = ScrollBars.Vertical;
        _detailsTextBox.Font = new Font(FontFamily.GenericMonospace, 9f);

        _dependencyTree.Dock = DockStyle.Fill;
        _dependencyTree.HideSelection = false;
        _dependencyTree.FullRowSelect = true;
        _dependencyTree.ShowNodeToolTips = true;

        detailsTab.Controls.Add(_detailsTextBox);
        dependencyTab.Controls.Add(_dependencyTree);
        lowerTabs.TabPages.Add(detailsTab);
        lowerTabs.TabPages.Add(dependencyTab);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(0, 8, 0, 0);

        _bindingSource.DataSource = _rows;
        _grid.DataSource = _bindingSource;

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(lowerTabs, 0, 2);
        root.Controls.Add(_statusLabel, 0, 3);

        Controls.Add(root);
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await RefreshServicesAsync();
        _graphButton.Click += (_, _) => ShowGraphWindow();
        _searchTextBox.TextChanged += (_, _) => ApplyFilter();
        _grid.ColumnHeaderMouseClick += (_, e) => SortByColumn(e.ColumnIndex);
        _grid.SelectionChanged += (_, _) => UpdateSelectionState();
        _startButton.Click += async (_, _) => await RunServiceActionAsync("start", service => _serviceApi.Start(service.Name));
        _stopButton.Click += async (_, _) => await RunServiceActionAsync("stop", service => _serviceApi.Stop(service.Name));
        _restartButton.Click += async (_, _) =>
        {
            await RunServiceActionAsync("restart", service =>
            {
                if (service.State == ServiceRunState.Running)
                {
                    _serviceApi.Stop(service.Name);
                    WaitForState(service.Name, ServiceRunState.Stopped, TimeSpan.FromSeconds(20));
                }

                _serviceApi.Start(service.Name);
            });
        };
    }

    private async Task RefreshServicesAsync()
    {
        await RunUiOperationAsync("Loading services...", () =>
        {
            _services = _serviceApi.GetServices().ToList();
        });
        ApplyFilter();
        SetStatus($"Loaded {_services.Count:N0} services.");
    }

    private void ApplyFilter()
    {
        var filter = _searchTextBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _services
            : _services.Where(service =>
                service.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                service.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                (service.Account?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToList();

        filtered = SortServices(filtered).ToList();

        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var service in filtered)
        {
            _rows.Add(ServiceGridRow.FromSummary(service));
        }
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();
        UpdateSelectionState();
        SyncGraphServices();
        UpdateSortGlyphs();
    }

    private void SortByColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _grid.Columns.Count)
        {
            return;
        }

        var propertyName = _grid.Columns[columnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return;
        }

        if (_sortProperty.Equals(propertyName, StringComparison.Ordinal))
        {
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _sortProperty = propertyName;
            _sortDirection = ListSortDirection.Ascending;
        }

        ApplyFilter();
    }

    private IEnumerable<ServiceSummary> SortServices(IEnumerable<ServiceSummary> services)
    {
        Func<ServiceSummary, object?> keySelector = _sortProperty switch
        {
            nameof(ServiceGridRow.Name) => service => service.Name,
            nameof(ServiceGridRow.DisplayName) => service => service.DisplayName,
            nameof(ServiceGridRow.State) => service => service.State,
            nameof(ServiceGridRow.StartType) => service => service.StartType,
            nameof(ServiceGridRow.Account) => service => service.Account,
            _ => service => service.DisplayName
        };

        return _sortDirection == ListSortDirection.Ascending
            ? services.OrderBy(keySelector, Comparer<object?>.Create(CompareSortValues))
            : services.OrderByDescending(keySelector, Comparer<object?>.Create(CompareSortValues));
    }

    private void UpdateSortGlyphs()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = column.DataPropertyName.Equals(_sortProperty, StringComparison.Ordinal)
                ? _sortDirection == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending
                : SortOrder.None;
        }
    }

    private static int CompareSortValues(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        return left is IComparable comparableLeft && left.GetType() == right.GetType()
            ? comparableLeft.CompareTo(right)
            : string.Compare(left.ToString(), right.ToString(), StringComparison.CurrentCultureIgnoreCase);
    }

    private async Task RunServiceActionAsync(string actionName, Action<ServiceSummary> action)
    {
        var selected = SelectedService();
        if (selected is null)
        {
            return;
        }

        var serviceName = selected.Name;
        await RunUiOperationAsync($"{CultureName(actionName)} '{serviceName}'...", () => action(selected));

        var refreshed = await Task.Run(() => _serviceApi.GetService(serviceName));
        var index = _services.FindIndex(service => service.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _services[index] = refreshed;
        }

        ApplyFilter();
        SelectService(serviceName);
        SetStatus($"{CultureName(actionName)} command completed for '{serviceName}'.");
    }

    private async Task RunUiOperationAsync(string workingMessage, Action action)
    {
        try
        {
            SetBusy(true);
            SetStatus(workingMessage);
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "Service Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void WaitForState(string serviceName, ServiceRunState targetState, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var service = _serviceApi.GetService(serviceName);
            if (service.State == targetState)
            {
                return;
            }

            Thread.Sleep(500);
        }

        throw new TimeoutException($"Service '{serviceName}' did not reach {targetState} within {timeout.TotalSeconds:N0} seconds.");
    }

    private ServiceSummary? SelectedService()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ServiceGridRow row)
        {
            return null;
        }

        return _services.FirstOrDefault(service => service.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
    }

    private bool SelectService(string serviceName)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is ServiceGridRow service && service.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
                return true;
            }
        }

        return false;
    }

    private void UpdateSelectionState()
    {
        var service = SelectedService();
        var hasSelection = service is not null;
        _startButton.Enabled = hasSelection && service!.State is ServiceRunState.Stopped;
        _stopButton.Enabled = hasSelection && service!.State is ServiceRunState.Running && service.AcceptsStop;
        _restartButton.Enabled = hasSelection && service!.State is ServiceRunState.Running && service.AcceptsStop;
        _detailsTextBox.Text = service is null ? string.Empty : FormatDetails(service);
        UpdateDependencyTree(service);
        SyncGraphSelection();
    }

    private void SetBusy(bool isBusy)
    {
        UseWaitCursor = isBusy;
        _refreshButton.Enabled = !isBusy;
        _searchTextBox.Enabled = !isBusy;
        _grid.Enabled = !isBusy;
        UpdateSelectionState();
    }

    private void ShowGraphWindow()
    {
        if (_graphForm is null || _graphForm.IsDisposed)
        {
            _graphForm = new ServiceGraphForm();
            _graphForm.ServiceSelected += (_, serviceName) => SelectServiceFromGraph(serviceName);
            _graphForm.FormClosed += (_, _) => _graphForm = null;
            PositionGraphWindow(_graphForm);
            _graphForm.Show(this);
        }
        else
        {
            _graphForm.Show();
            _graphForm.WindowState = FormWindowState.Normal;
            _graphForm.Activate();
        }

        SyncGraphServices();
    }

    private void PositionGraphWindow(Form graphForm)
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var left = Right + 12;
        var top = Top;

        if (left + graphForm.Width > workingArea.Right)
        {
            left = Math.Max(workingArea.Left, Left - graphForm.Width - 12);
        }

        graphForm.Location = new Point(left, Math.Max(workingArea.Top, top));
    }

    private void SyncGraphServices()
    {
        _graphForm?.SetServices(_services, SelectedService()?.Name);
    }

    private void SyncGraphSelection()
    {
        _graphForm?.SelectService(SelectedService()?.Name);
    }

    private void SelectServiceFromGraph(string serviceName)
    {
        if (!SelectService(serviceName))
        {
            _searchTextBox.Clear();
            ApplyFilter();
            SelectService(serviceName);
        }

        Activate();
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private static string FormatDetails(ServiceSummary service)
    {
        return string.Join(Environment.NewLine, [
            $"Display name: {service.DisplayName}",
            $"Service name: {service.Name}",
            $"Status: {service.State}",
            $"Startup type: {service.StartType?.ToString() ?? "Unknown"}",
            $"Account: {service.Account ?? "Unknown"}",
            $"Accepts stop: {service.AcceptsStop}",
            $"Accepts pause/continue: {service.AcceptsPauseContinue}",
            $"Depends on: {service.DependsOn.Count}",
            $"Required by: {service.RequiredBy.Count}",
            string.Empty,
            "Binary path:",
            service.BinaryPath ?? "Unknown"
        ]);
    }

    private void UpdateDependencyTree(ServiceSummary? selected)
    {
        _dependencyTree.BeginUpdate();
        try
        {
            _dependencyTree.Nodes.Clear();
            if (selected is null)
            {
                return;
            }

            var root = new TreeNode(FormatServiceNodeText(selected))
            {
                ToolTipText = "Selected service"
            };

            var dependencyRoot = new TreeNode("Depends on");
            AddDependencyNodes(dependencyRoot, selected.DependsOn, service => service.DependsOn, NewVisitedSet(selected.Name));
            if (dependencyRoot.Nodes.Count == 0)
            {
                dependencyRoot.Nodes.Add(new TreeNode("No service dependencies"));
            }

            var dependentRoot = new TreeNode("Required by / stop impact");
            AddDependencyNodes(dependentRoot, selected.RequiredBy, service => service.RequiredBy, NewVisitedSet(selected.Name));
            if (dependentRoot.Nodes.Count == 0)
            {
                dependentRoot.Nodes.Add(new TreeNode("No dependent services"));
            }

            root.Nodes.Add(dependencyRoot);
            root.Nodes.Add(dependentRoot);
            _dependencyTree.Nodes.Add(root);
            root.Expand();
            dependencyRoot.Expand();
            dependentRoot.Expand();
        }
        finally
        {
            _dependencyTree.EndUpdate();
        }
    }

    private void AddDependencyNodes(
        TreeNode parent,
        IReadOnlyList<string> serviceNames,
        Func<ServiceSummary, IReadOnlyList<string>> nextSelector,
        HashSet<string> visited)
    {
        foreach (var serviceName in serviceNames.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            var service = FindService(serviceName);
            var node = new TreeNode(service is null ? serviceName : FormatServiceNodeText(service))
            {
                ToolTipText = serviceName
            };
            parent.Nodes.Add(node);

            if (service is null)
            {
                continue;
            }

            if (!visited.Add(service.Name))
            {
                node.Nodes.Add(new TreeNode("Already shown"));
                continue;
            }

            AddDependencyNodes(node, nextSelector(service), nextSelector, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase));
        }
    }

    private ServiceSummary? FindService(string serviceName)
    {
        return _services.FirstOrDefault(service => service.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatServiceNodeText(ServiceSummary service)
    {
        return $"{service.DisplayName} ({service.Name}) - {service.State}";
    }

    private static HashSet<string> NewVisitedSet(string serviceName)
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            serviceName
        };
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string CultureName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private sealed class ServiceGridRow
    {
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required string State { get; init; }
        public required string StartType { get; init; }
        public required string Account { get; init; }

        public static ServiceGridRow FromSummary(ServiceSummary service)
        {
            return new ServiceGridRow
            {
                Name = service.Name,
                DisplayName = service.DisplayName,
                State = service.State.ToString(),
                StartType = service.StartType?.ToString() ?? "Unknown",
                Account = service.Account ?? string.Empty
            };
        }
    }
}
