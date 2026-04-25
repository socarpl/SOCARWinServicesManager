using System.Windows.Controls;

namespace Socar.WinServicesManager;

public static class ServiceDetailsFormatter
{
    public static string FormatDetails(ServiceSummary service)
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
}

public static class DependencyTreeBuilder
{
    public static void Populate(TreeView tree, ServiceSummary? selected, Func<string, ServiceSummary?> findService)
    {
        tree.Items.Clear();
        if (selected is null)
        {
            return;
        }

        var root = new TreeViewItem { Header = FormatServiceNodeText(selected), IsExpanded = true };
        var dependencyRoot = new TreeViewItem { Header = "Depends on", IsExpanded = true };
        AddDependencyNodes(dependencyRoot, selected.DependsOn, service => service.DependsOn, NewVisitedSet(selected.Name), findService);
        if (dependencyRoot.Items.Count == 0)
        {
            dependencyRoot.Items.Add(new TreeViewItem { Header = "No service dependencies" });
        }

        var dependentRoot = new TreeViewItem { Header = "Required by / stop impact", IsExpanded = true };
        AddDependencyNodes(dependentRoot, selected.RequiredBy, service => service.RequiredBy, NewVisitedSet(selected.Name), findService);
        if (dependentRoot.Items.Count == 0)
        {
            dependentRoot.Items.Add(new TreeViewItem { Header = "No dependent services" });
        }

        root.Items.Add(dependencyRoot);
        root.Items.Add(dependentRoot);
        tree.Items.Add(root);
    }

    private static void AddDependencyNodes(
        TreeViewItem parent,
        IReadOnlyList<string> serviceNames,
        Func<ServiceSummary, IReadOnlyList<string>> nextSelector,
        HashSet<string> visited,
        Func<string, ServiceSummary?> findService)
    {
        foreach (var serviceName in serviceNames.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            var service = findService(serviceName);
            var node = new TreeViewItem { Header = service is null ? serviceName : FormatServiceNodeText(service), IsExpanded = true };
            parent.Items.Add(node);

            if (service is null)
            {
                continue;
            }

            if (!visited.Add(service.Name))
            {
                node.Items.Add(new TreeViewItem { Header = "Already shown" });
                continue;
            }

            AddDependencyNodes(node, nextSelector(service), nextSelector, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase), findService);
        }
    }

    private static string FormatServiceNodeText(ServiceSummary service)
    {
        return $"{service.DisplayName} ({service.Name}) - {service.State}";
    }

    private static HashSet<string> NewVisitedSet(string serviceName)
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { serviceName };
    }
}
