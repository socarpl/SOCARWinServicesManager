namespace Socar.WinServicesManager;

public sealed record ServiceSummary(
    string Name,
    string DisplayName,
    ServiceRunState State,
    ServiceStartType? StartType,
    string? BinaryPath,
    string? Account,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> RequiredBy,
    bool AcceptsStop,
    bool AcceptsPauseContinue);

public enum ServiceRunState : uint
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7
}

public enum ServiceStartType : uint
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4
}
