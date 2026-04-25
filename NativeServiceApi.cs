using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Socar.WinServicesManager;

public sealed class NativeServiceApi
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerEnumerateService = 0x0004;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceEnumerateDependents = 0x0008;
    private const uint ServiceInterrogate = 0x0080;
    private const uint ServiceWin32 = 0x00000030;
    private const uint ServiceStateAll = 0x00000003;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceAcceptStop = 0x00000001;
    private const uint ServiceAcceptPauseContinue = 0x00000002;
    private const int ScStatusProcessInfo = 0;

    public IReadOnlyList<ServiceSummary> GetServices()
    {
        using var manager = OpenManager(ScManagerEnumerateService);
        var statusItems = EnumerateServices(manager);
        var services = new List<ServiceSummary>(statusItems.Count);

        foreach (var item in statusItems.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            services.Add(WithConfig(manager, item));
        }

        return services;
    }

    public void Start(string serviceName)
    {
        using var manager = OpenManager(ScManagerConnect);
        using var service = OpenExistingService(manager, serviceName, ServiceStart | ServiceQueryStatus);

        if (!StartService(service, 0, null))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1056)
            {
                throw CreateWin32Exception(error, $"Could not start service '{serviceName}'.");
            }
        }
    }

    public void Stop(string serviceName)
    {
        using var manager = OpenManager(ScManagerConnect);
        using var service = OpenExistingService(manager, serviceName, ServiceStop | ServiceQueryStatus | ServiceInterrogate);

        if (!ControlService(service, ServiceControlStop, out _))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1062)
            {
                throw CreateWin32Exception(error, $"Could not stop service '{serviceName}'.");
            }
        }
    }

    public void SetStartType(string serviceName, ServiceStartType startType)
    {
        using var manager = OpenManager(ScManagerConnect);
        using var service = OpenExistingService(manager, serviceName, ServiceChangeConfig);

        if (!ChangeServiceConfig(
                service,
                0xffffffff,
                (uint)startType,
                0xffffffff,
                null,
                null,
                IntPtr.Zero,
                null,
                null,
                null,
                null))
        {
            throw CreateWin32Exception(Marshal.GetLastWin32Error(), $"Could not change startup type for service '{serviceName}'.");
        }
    }

    public ServiceSummary GetService(string serviceName)
    {
        using var manager = OpenManager(ScManagerEnumerateService);
        var service = EnumerateServices(manager).FirstOrDefault(s => s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            throw new InvalidOperationException($"Service '{serviceName}' was not found.");
        }

        return WithConfig(manager, service);
    }

    private static SafeServiceHandle OpenManager(uint access)
    {
        var handle = OpenSCManager(null, null, access);
        if (handle.IsInvalid)
        {
            throw CreateWin32Exception(Marshal.GetLastWin32Error(), "Could not open the Service Control Manager.");
        }

        return handle;
    }

    private static SafeServiceHandle OpenExistingService(SafeServiceHandle manager, string serviceName, uint access)
    {
        var handle = OpenService(manager, serviceName, access);
        if (handle.IsInvalid)
        {
            throw CreateWin32Exception(Marshal.GetLastWin32Error(), $"Could not open service '{serviceName}'.");
        }

        return handle;
    }

    private static List<ServiceSummary> EnumerateServices(SafeServiceHandle manager)
    {
        _ = EnumServicesStatusEx(
            manager,
            ScStatusProcessInfo,
            ServiceWin32,
            ServiceStateAll,
            IntPtr.Zero,
            0,
            out var bytesNeeded,
            out _,
            out _,
            null);

        var error = Marshal.GetLastWin32Error();
        if (error != 234)
        {
            throw CreateWin32Exception(error, "Could not query service list size.");
        }

        var buffer = Marshal.AllocHGlobal((int)bytesNeeded);
        try
        {
            if (!EnumServicesStatusEx(
                    manager,
                    ScStatusProcessInfo,
                    ServiceWin32,
                    ServiceStateAll,
                    buffer,
                    bytesNeeded,
                    out _,
                    out var servicesReturned,
                    out _,
                    null))
            {
                throw CreateWin32Exception(Marshal.GetLastWin32Error(), "Could not enumerate services.");
            }

            var items = new List<ServiceSummary>((int)servicesReturned);
            var itemSize = Marshal.SizeOf<EnumServiceStatusProcess>();

            for (var i = 0; i < servicesReturned; i++)
            {
                var itemPointer = IntPtr.Add(buffer, i * itemSize);
                var nativeItem = Marshal.PtrToStructure<EnumServiceStatusProcess>(itemPointer);
                var status = nativeItem.ServiceStatusProcess;

                items.Add(new ServiceSummary(
                    Marshal.PtrToStringUni(nativeItem.ServiceName) ?? string.Empty,
                    Marshal.PtrToStringUni(nativeItem.DisplayName) ?? string.Empty,
                    (ServiceRunState)status.CurrentState,
                    null,
                    null,
                    null,
                    [],
                    [],
                    (status.ControlsAccepted & ServiceAcceptStop) != 0,
                    (status.ControlsAccepted & ServiceAcceptPauseContinue) != 0));
            }

            return items;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ServiceSummary WithConfig(SafeServiceHandle manager, ServiceSummary service)
    {
        using var handle = OpenService(manager, service.Name, ServiceQueryConfig | ServiceEnumerateDependents);
        if (handle.IsInvalid)
        {
            return service;
        }

        _ = QueryServiceConfig(handle, IntPtr.Zero, 0, out var bytesNeeded);
        var error = Marshal.GetLastWin32Error();
        if (error != 122)
        {
            return service;
        }

        var buffer = Marshal.AllocHGlobal((int)bytesNeeded);
        try
        {
            if (!QueryServiceConfig(handle, buffer, bytesNeeded, out _))
            {
                return service;
            }

            var config = Marshal.PtrToStructure<QueryServiceConfigNative>(buffer);
            return service with
            {
                StartType = (ServiceStartType)config.StartType,
                BinaryPath = Marshal.PtrToStringUni(config.BinaryPathName),
                Account = Marshal.PtrToStringUni(config.ServiceStartName),
                DependsOn = ReadMultiString(config.Dependencies),
                RequiredBy = EnumerateDependentServices(handle)
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<string> EnumerateDependentServices(SafeServiceHandle service)
    {
        _ = EnumDependentServices(service, ServiceStateAll, IntPtr.Zero, 0, out var bytesNeeded, out var servicesReturned);
        if (bytesNeeded == 0 || servicesReturned == 0)
        {
            return [];
        }

        var error = Marshal.GetLastWin32Error();
        if (error != 234)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)bytesNeeded);
        try
        {
            if (!EnumDependentServices(service, ServiceStateAll, buffer, bytesNeeded, out _, out servicesReturned))
            {
                return [];
            }

            var dependents = new List<string>((int)servicesReturned);
            var itemSize = Marshal.SizeOf<EnumServiceStatus>();

            for (var i = 0; i < servicesReturned; i++)
            {
                var itemPointer = IntPtr.Add(buffer, i * itemSize);
                var nativeItem = Marshal.PtrToStructure<EnumServiceStatus>(itemPointer);
                var serviceName = Marshal.PtrToStringUni(nativeItem.ServiceName);
                if (!string.IsNullOrWhiteSpace(serviceName))
                {
                    dependents.Add(serviceName);
                }
            }

            return dependents.Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<string> ReadMultiString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return [];
        }

        var values = new List<string>();
        var offset = 0;

        while (true)
        {
            var value = Marshal.PtrToStringUni(IntPtr.Add(pointer, offset * 2));
            if (string.IsNullOrEmpty(value))
            {
                break;
            }

            if (!value.StartsWith('+'))
            {
                values.Add(value);
            }

            offset += value.Length + 1;
        }

        return values.Order(StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static Win32Exception CreateWin32Exception(int error, string message)
    {
        return new Win32Exception(error, $"{message} {new Win32Exception(error).Message}");
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeServiceHandle OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeServiceHandle OpenService(SafeServiceHandle serviceControlManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumServicesStatusEx(
        SafeServiceHandle serviceControlManager,
        int infoLevel,
        uint serviceType,
        uint serviceState,
        IntPtr services,
        uint bufferSize,
        out uint bytesNeeded,
        out uint servicesReturned,
        out uint resumeHandle,
        string? groupName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryServiceConfig(
        SafeServiceHandle service,
        IntPtr serviceConfig,
        uint bufferSize,
        out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartService(SafeServiceHandle service, int argumentCount, string[]? arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(SafeServiceHandle service, uint control, out ServiceStatus serviceStatus);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig(
        SafeServiceHandle service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumDependentServices(
        SafeServiceHandle service,
        uint serviceState,
        IntPtr services,
        uint bufferSize,
        out uint bytesNeeded,
        out uint servicesReturned);

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public ServiceStatusProcess ServiceStatusProcess;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatus
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public ServiceStatus ServiceStatus;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryServiceConfigNative
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public IntPtr BinaryPathName;
        public IntPtr LoadOrderGroup;
        public uint TagId;
        public IntPtr Dependencies;
        public IntPtr ServiceStartName;
        public IntPtr DisplayName;
    }

    private sealed class SafeServiceHandle : SafeHandle
    {
        public SafeServiceHandle()
            : base(IntPtr.Zero, true)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            return CloseServiceHandle(handle);
        }
    }
}
