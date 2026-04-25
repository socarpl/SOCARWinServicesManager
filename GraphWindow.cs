using System.Windows;
using System.Windows.Controls;

namespace Socar.WinServicesManager;

public sealed class GraphWindow : Window
{
    private readonly ServiceGraphControl _graph = new();
    private readonly TextBlock _displayNameText = new();
    private readonly TextBlock _serviceNameText = new();
    private readonly TextBlock _descriptionText = new();
    private IReadOnlyList<ServiceSummary> _services = [];

    public GraphWindow()
    {
        Title = "SOCAR WinServicesManager Graph";
        MinWidth = 760;
        MinHeight = 520;
        Width = 960;
        Height = 720;
        Content = BuildLayout();
        _graph.ServiceSelected += (_, serviceName) =>
        {
            UpdateSelectedService(serviceName);
            ServiceSelected?.Invoke(this, serviceName);
        };
    }

    public event EventHandler<string>? ServiceSelected;

    public void SetServices(IReadOnlyList<ServiceSummary> services, string? selectedServiceName)
    {
        _services = services;
        _graph.SetServices(services, selectedServiceName);
        UpdateSelectedService(selectedServiceName);
    }

    public void SelectService(string? serviceName)
    {
        _graph.SelectService(serviceName);
        UpdateSelectedService(serviceName);
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        var infoPanel = new StackPanel
        {
            Width = 280,
            Margin = new Thickness(12),
        };
        DockPanel.SetDock(infoPanel, Dock.Left);

        infoPanel.Children.Add(new TextBlock
        {
            Text = "Service",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        infoPanel.Children.Add(Label("Name"));
        _displayNameText.TextWrapping = TextWrapping.Wrap;
        _displayNameText.Margin = new Thickness(0, 0, 0, 12);
        infoPanel.Children.Add(_displayNameText);

        infoPanel.Children.Add(Label("Short name"));
        _serviceNameText.TextWrapping = TextWrapping.Wrap;
        _serviceNameText.Margin = new Thickness(0, 0, 0, 12);
        infoPanel.Children.Add(_serviceNameText);

        infoPanel.Children.Add(Label("Description"));
        _descriptionText.TextWrapping = TextWrapping.Wrap;
        _descriptionText.Margin = new Thickness(0, 0, 0, 12);
        infoPanel.Children.Add(_descriptionText);

        root.Children.Add(infoPanel);
        root.Children.Add(_graph);
        return root;
    }

    private void UpdateSelectedService(string? serviceName)
    {
        var service = serviceName is null
            ? null
            : _services.FirstOrDefault(item => item.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

        if (service is null)
        {
            _displayNameText.Text = "No service selected";
            _serviceNameText.Text = "-";
            _descriptionText.Text = "Click a dot in the graph to inspect a service.";
            return;
        }

        _displayNameText.Text = service.DisplayName;
        _serviceNameText.Text = service.Name;
        _descriptionText.Text = BuildDescription(service);
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
    }

    private static string BuildDescription(ServiceSummary service)
    {
        return string.Join(Environment.NewLine, [
            $"Status: {service.State}",
            $"Startup: {service.StartType?.ToString() ?? "Unknown"}",
            $"Account: {service.Account ?? "Unknown"}",
            $"Depends on: {service.DependsOn.Count}",
            $"Required by: {service.RequiredBy.Count}",
            string.Empty,
            service.BinaryPath ?? "Binary path unknown"
        ]);
    }
}
