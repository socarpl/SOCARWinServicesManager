using System.ComponentModel;

namespace Socar.WinServicesManager;

public sealed class ProfileRunner(NativeServiceApi serviceApi)
{
    public string Run(ServiceProfile profile, AppSettings settings)
    {
        var log = new List<string>
        {
            $"Running profile '{profile.Name}' at {DateTime.Now:G}",
            string.Empty
        };

        var services = serviceApi.GetServices().ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        var actions = profile.Actions.ToDictionary(action => action.ServiceName, StringComparer.OrdinalIgnoreCase);

        foreach (var action in profile.Actions.Where(action => action.DesiredStartType is not null))
        {
            TryRun(log, $"Set startup type for {action.ServiceName}", () =>
            {
                if (services.TryGetValue(action.ServiceName, out var service) &&
                    service.StartType == action.DesiredStartType)
                {
                    log.Add($"SKIP: Startup type for {action.ServiceName} already is {action.DesiredStartType}.");
                    return;
                }

                serviceApi.SetStartType(action.ServiceName, action.DesiredStartType!.Value);
                RefreshService(services, action.ServiceName);
            });
        }

        foreach (var action in profile.Actions.Where(action => action.DesiredStatus == DesiredServiceStatus.Stopped))
        {
            StopService(action.ServiceName, settings.DependencyStopPolicy, actions, services, log, []);
        }

        foreach (var action in profile.Actions.Where(action => action.DesiredStatus == DesiredServiceStatus.Running))
        {
        TryRun(log, $"Start {action.ServiceName}", () =>
        {
                services.TryGetValue(action.ServiceName, out var service);
                var startType = action.DesiredStartType ?? service?.StartType;
                if (startType is null or ServiceStartType.Disabled)
                {
                    serviceApi.SetStartType(action.ServiceName, ServiceStartType.Manual);
                    log.Add($"  Startup type for {action.ServiceName} changed to Manual before start.");
                }

                if (service?.State == ServiceRunState.Running)
                {
                    log.Add($"SKIP: {action.ServiceName} already is Running.");
                    return;
                }

                serviceApi.Start(action.ServiceName);
                WaitForState(action.ServiceName, ServiceRunState.Running, TimeSpan.FromSeconds(25));
                RefreshService(services, action.ServiceName);
            });
        }

        log.Add(string.Empty);
        log.Add("Profile run completed.");
        return string.Join(Environment.NewLine, log);
    }

    private void StopService(
        string serviceName,
        DependencyStopPolicy policy,
        IReadOnlyDictionary<string, ProfileServiceAction> profileActions,
        Dictionary<string, ServiceSummary> services,
        List<string> log,
        HashSet<string> visited)
    {
        if (!visited.Add(serviceName))
        {
            return;
        }

        if (!services.TryGetValue(serviceName, out var service))
        {
            log.Add($"SKIP: {serviceName} was not found.");
            return;
        }

        var runningDependents = service.RequiredBy
            .Where(name => services.TryGetValue(name, out var dependent) && dependent.State == ServiceRunState.Running)
            .ToList();

        if (runningDependents.Count > 0)
        {
            if (policy == DependencyStopPolicy.AutoStopDependents)
            {
                foreach (var dependentName in runningDependents)
                {
                    StopService(dependentName, policy, profileActions, services, log, visited);
                }
            }
            else if (policy == DependencyStopPolicy.WarnAndSkipUnlessInProfile)
            {
                var unprofiledDependents = runningDependents
                    .Where(name => !profileActions.TryGetValue(name, out var dependentAction) ||
                                   dependentAction.DesiredStatus != DesiredServiceStatus.Stopped)
                    .ToList();

                if (unprofiledDependents.Count > 0)
                {
                    log.Add($"SKIP: {serviceName} has running dependent services not marked to stop: {string.Join(", ", unprofiledDependents)}");
                    return;
                }

                foreach (var dependentName in runningDependents)
                {
                    StopService(dependentName, policy, profileActions, services, log, visited);
                }
            }
            else
            {
                log.Add($"FAIL: {serviceName} has running dependent services: {string.Join(", ", runningDependents)}");
                return;
            }
        }

        TryRun(log, $"Stop {serviceName}", () =>
        {
            if (services.TryGetValue(serviceName, out var currentService) &&
                currentService.State == ServiceRunState.Stopped)
            {
                log.Add($"SKIP: {serviceName} already is Stopped.");
                return;
            }

            serviceApi.Stop(serviceName);
            WaitForState(serviceName, ServiceRunState.Stopped, TimeSpan.FromSeconds(25));
            RefreshService(services, serviceName);
        });
    }

    private void RefreshService(Dictionary<string, ServiceSummary> services, string serviceName)
    {
        try
        {
            services[serviceName] = serviceApi.GetService(serviceName);
        }
        catch
        {
            services.Remove(serviceName);
        }
    }

    private void WaitForState(string serviceName, ServiceRunState targetState, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var service = serviceApi.GetService(serviceName);
            if (service.State == targetState)
            {
                return;
            }

            Thread.Sleep(500);
        }

        throw new TimeoutException($"{serviceName} did not reach {targetState} within {timeout.TotalSeconds:N0} seconds.");
    }

    private static void TryRun(List<string> log, string label, Action action)
    {
        try
        {
            action();
            log.Add($"OK: {label}");
        }
        catch (Win32Exception ex)
        {
            log.Add($"ERROR: {label}: {ex.Message}");
        }
        catch (Exception ex)
        {
            log.Add($"ERROR: {label}: {ex.Message}");
        }
    }
}
