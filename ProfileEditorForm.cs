using System.ComponentModel;

namespace Socar.WinServicesManager;

public sealed class ProfileEditorForm : Form
{
    private const string Unchanged = "(unchanged)";

    private readonly NativeServiceApi _serviceApi;
    private readonly TextBox _nameTextBox = new();
    private readonly TextBox _searchTextBox = new();
    private readonly CheckBox _hideMicrosoftServicesCheckBox = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _detailsTextBox = new();
    private readonly TreeView _dependencyTree = new();
    private readonly BindingList<ServiceActionRow> _rows = [];
    private readonly List<ServiceActionRow> _allRows = [];
    private readonly List<ServiceSummary> _services = [];
    private readonly ServiceProfile _profile;
    private string _sortProperty = nameof(ServiceActionRow.DisplayName);
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public ProfileEditorForm(NativeServiceApi serviceApi, ServiceProfile? profile)
    {
        _serviceApi = serviceApi;
        _profile = profile ?? new ServiceProfile
        {
            Name = string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Text = profile is null ? "Create Profile" : "Edit Profile";
        MinimumSize = new Size(980, 620);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadServices();
        ApplyFilter();
    }

    public ServiceProfile Profile => _profile;

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var namePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };
        namePanel.Controls.Add(new Label { Text = "Profile name", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        _nameTextBox.Width = 360;
        _nameTextBox.Text = _profile.Name;
        namePanel.Controls.Add(_nameTextBox);

        _searchTextBox.Dock = DockStyle.Top;
        _searchTextBox.PlaceholderText = "Search services";

        _hideMicrosoftServicesCheckBox.Text = "Hide all Microsoft services";
        _hideMicrosoftServicesCheckBox.Checked = true;
        _hideMicrosoftServicesCheckBox.AutoSize = true;
        _hideMicrosoftServicesCheckBox.Padding = new Padding(0, 6, 0, 8);

        var contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 360,
            Panel1MinSize = 240,
            Panel2MinSize = 140
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.DataSource = _rows;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(ServiceActionRow.Include), HeaderText = "Use", SortMode = DataGridViewColumnSortMode.Programmatic, FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ServiceActionRow.DisplayName), HeaderText = "Display name", ReadOnly = true, SortMode = DataGridViewColumnSortMode.Programmatic, FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ServiceActionRow.ServiceName), HeaderText = "Service name", ReadOnly = true, SortMode = DataGridViewColumnSortMode.Programmatic, FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ServiceActionRow.CurrentStartType), HeaderText = "Current startup", ReadOnly = true, SortMode = DataGridViewColumnSortMode.Programmatic, FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ServiceActionRow.CurrentStatus), HeaderText = "Current status", ReadOnly = true, SortMode = DataGridViewColumnSortMode.Programmatic, FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ServiceActionRow.DesiredStartType),
            HeaderText = "New startup",
            SortMode = DataGridViewColumnSortMode.Programmatic,
            FillWeight = 14,
            FlatStyle = FlatStyle.Flat,
            DataSource = new[] { Unchanged, ServiceStartType.Automatic.ToString(), ServiceStartType.Manual.ToString(), ServiceStartType.Disabled.ToString() }
        });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ServiceActionRow.DesiredStatus),
            HeaderText = "New status",
            SortMode = DataGridViewColumnSortMode.Programmatic,
            FillWeight = 12,
            FlatStyle = FlatStyle.Flat,
            DataSource = new[] { Unchanged, DesiredServiceStatus.Running.ToString(), DesiredServiceStatus.Stopped.ToString() }
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

        contentSplit.Panel1.Controls.Add(_grid);
        contentSplit.Panel2.Controls.Add(lowerTabs);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        var saveButton = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        root.Controls.Add(namePanel, 0, 0);
        root.Controls.Add(_searchTextBox, 0, 1);
        root.Controls.Add(_hideMicrosoftServicesCheckBox, 0, 2);
        root.Controls.Add(contentSplit, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        _searchTextBox.TextChanged += (_, _) => ApplyFilter();
        _hideMicrosoftServicesCheckBox.CheckedChanged += (_, _) => ApplyFilter();
        _grid.ColumnHeaderMouseClick += (_, e) => SortByColumn(e.ColumnIndex);
        _grid.SelectionChanged += (_, _) => UpdateSelectedServiceDetails();
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 &&
                _grid.Columns[e.ColumnIndex].DataPropertyName is nameof(ServiceActionRow.DesiredStartType) or nameof(ServiceActionRow.DesiredStatus))
            {
                SyncIncludeState(_rows[e.RowIndex]);
                _grid.InvalidateRow(e.RowIndex);
            }
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
    }

    private void LoadServices()
    {
        var savedActions = _profile.Actions.ToDictionary(action => action.ServiceName, StringComparer.OrdinalIgnoreCase);
        _services.Clear();
        _services.AddRange(_serviceApi.GetServices());
        foreach (var service in _services)
        {
            savedActions.TryGetValue(service.Name, out var action);
            _allRows.Add(new ServiceActionRow
            {
                Include = action is not null,
                ServiceName = service.Name,
                DisplayName = service.DisplayName,
                CurrentStartType = service.StartType?.ToString() ?? "Unknown",
                CurrentStatus = service.State.ToString(),
                DesiredStartType = action?.DesiredStartType?.ToString() ?? Unchanged,
                DesiredStatus = action?.DesiredStatus?.ToString() ?? Unchanged,
                IsMicrosoftService = IsMicrosoftService(service)
            });
        }
    }

    private void ApplyFilter()
    {
        var filter = _searchTextBox.Text.Trim();
        var filtered = _allRows.AsEnumerable();

        if (_hideMicrosoftServicesCheckBox.Checked)
        {
            filtered = filtered.Where(row => !row.IsMicrosoftService);
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filtered.Where(row =>
                row.ServiceName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                row.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        }

        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var row in SortRows(filtered))
        {
            _rows.Add(row);
        }
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();
        UpdateSortGlyphs();
        UpdateSelectedServiceDetails();
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

    private IEnumerable<ServiceActionRow> SortRows(IEnumerable<ServiceActionRow> rows)
    {
        Func<ServiceActionRow, object?> keySelector = _sortProperty switch
        {
            nameof(ServiceActionRow.Include) => row => row.Include,
            nameof(ServiceActionRow.ServiceName) => row => row.ServiceName,
            nameof(ServiceActionRow.DisplayName) => row => row.DisplayName,
            nameof(ServiceActionRow.CurrentStartType) => row => row.CurrentStartType,
            nameof(ServiceActionRow.CurrentStatus) => row => row.CurrentStatus,
            nameof(ServiceActionRow.DesiredStartType) => row => row.DesiredStartType,
            nameof(ServiceActionRow.DesiredStatus) => row => row.DesiredStatus,
            _ => row => row.DisplayName
        };

        return _sortDirection == ListSortDirection.Ascending
            ? rows.OrderBy(keySelector, Comparer<object?>.Create(CompareSortValues))
            : rows.OrderByDescending(keySelector, Comparer<object?>.Create(CompareSortValues));
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

    private void UpdateSelectedServiceDetails()
    {
        var service = SelectedService();
        _detailsTextBox.Text = service is null ? string.Empty : FormatDetails(service);
        UpdateDependencyTree(service);
    }

    private ServiceSummary? SelectedService()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ServiceActionRow row)
        {
            return null;
        }

        return FindService(row.ServiceName);
    }

    private ServiceSummary? FindService(string serviceName)
    {
        return _services.FirstOrDefault(service => service.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static void SyncIncludeState(ServiceActionRow row)
    {
        if (row.DesiredStartType != Unchanged || row.DesiredStatus != Unchanged)
        {
            row.Include = true;
        }
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
        {
            base.OnFormClosing(e);
            return;
        }

        _grid.EndEdit();
        var profileName = _nameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            MessageBox.Show(this, "Profile name is required.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }

        var actions = _allRows
            .Where(row => row.Include && (row.DesiredStartType != Unchanged || row.DesiredStatus != Unchanged))
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
            MessageBox.Show(this, "Select at least one service action.", "Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }

        _profile.Name = profileName;
        _profile.Actions = actions;
        base.OnFormClosing(e);
    }

    private sealed class ServiceActionRow
    {
        public bool Include { get; set; }
        public required string ServiceName { get; init; }
        public required string DisplayName { get; init; }
        public required string CurrentStartType { get; init; }
        public required string CurrentStatus { get; init; }
        public string DesiredStartType { get; set; } = Unchanged;
        public string DesiredStatus { get; set; } = Unchanged;
        public bool IsMicrosoftService { get; init; }
    }
}
