namespace Socar.WinServicesManager;

public sealed class ServiceProfile
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ProfileServiceAction> Actions { get; set; } = [];
}

public sealed class ProfileServiceAction
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public required string ServiceName { get; set; }
    public string? DisplayName { get; set; }
    public ServiceStartType? DesiredStartType { get; set; }
    public DesiredServiceStatus? DesiredStatus { get; set; }
}

public sealed class AppSettings
{
    public DependencyStopPolicy DependencyStopPolicy { get; set; } = DependencyStopPolicy.AutoStopDependents;
}

public enum DesiredServiceStatus
{
    Running,
    Stopped
}

public enum DependencyStopPolicy
{
    AutoStopDependents,
    WarnAndSkipUnlessInProfile,
    FailAndContinue
}
