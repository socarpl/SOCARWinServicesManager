using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Socar.WinServicesManager.Service;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "SOCARWinServicesManagerProfileRunner";
        });
        builder.Services.AddHostedService<ProfileRunnerWorker>();
        await builder.Build().RunAsync();
    }
}
