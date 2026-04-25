namespace Socar.WinServicesManager;

public sealed class ServiceGraphForm : Form
{
    private readonly ServiceGraphView _graphView = new();

    public ServiceGraphForm()
    {
        Text = "Windows Services Graph";
        MinimumSize = new Size(760, 520);
        Size = new Size(960, 720);
        StartPosition = FormStartPosition.Manual;

        _graphView.Dock = DockStyle.Fill;
        _graphView.ServiceSelected += (_, serviceName) => ServiceSelected?.Invoke(this, serviceName);
        Controls.Add(_graphView);
    }

    public event EventHandler<string>? ServiceSelected;

    public void SetServices(IReadOnlyList<ServiceSummary> services, string? selectedServiceName)
    {
        _graphView.SetServices(services, selectedServiceName);
    }

    public void SelectService(string? serviceName)
    {
        _graphView.SelectService(serviceName);
    }
}
