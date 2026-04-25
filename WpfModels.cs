using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Socar.WinServicesManager;

public sealed class ProfileListRow(long id, string name, int actionCount, string updatedAt)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
    public int ActionCount { get; } = actionCount;
    public string UpdatedAt { get; } = updatedAt;
}

public sealed class ServiceActionEditRow : INotifyPropertyChanged
{
    private bool _include;
    private string _desiredStartType = WpfUi.Unchanged;
    private string _desiredStatus = WpfUi.Unchanged;

    public bool Include
    {
        get => _include;
        set => SetField(ref _include, value);
    }

    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public required string CurrentStartType { get; init; }
    public required string CurrentStatus { get; init; }
    public string? BinaryPath { get; init; }
    public bool IsMicrosoftService { get; init; }

    public string DesiredStartType
    {
        get => _desiredStartType;
        set
        {
            if (SetField(ref _desiredStartType, value) && value != WpfUi.Unchanged)
            {
                Include = true;
            }
        }
    }

    public string DesiredStatus
    {
        get => _desiredStatus;
        set
        {
            if (SetField(ref _desiredStatus, value) && value != WpfUi.Unchanged)
            {
                Include = true;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public static class WpfUi
{
    public const string Unchanged = "(unchanged)";

    public static readonly string[] StartupOptions =
    [
        Unchanged,
        nameof(ServiceStartType.Automatic),
        nameof(ServiceStartType.Manual),
        nameof(ServiceStartType.Disabled)
    ];

    public static readonly string[] StatusOptions =
    [
        Unchanged,
        nameof(DesiredServiceStatus.Running),
        nameof(DesiredServiceStatus.Stopped)
    ];
}
