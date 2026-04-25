using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Socar.WinServicesManager;

namespace Socar.WinServicesManager.Service;

public sealed class ProfileRunnerWorker(ILogger<ProfileRunnerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Profile runner service started. Database: {DatabasePath}", SharedRuntimeConfig.ResolveDatabasePath());

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = CreatePipeServer();

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Named pipe request failed.");
            }
        }
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            SharedRuntimeConfig.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true)
        {
            AutoFlush = true
        };

        var message = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(message))
        {
            await writer.WriteLineAsync("ERROR: Empty request.");
            return;
        }

        if (!IpcMessages.TryParseRunProfile(message, out var profileId))
        {
            await writer.WriteLineAsync("ERROR: Unsupported request.");
            return;
        }

        try
        {
            var repository = new ProfileRepository();
            repository.Initialize();
            var profile = repository.GetProfile(profileId);
            var settings = repository.GetSettings();
            var result = new ProfileRunner(new NativeServiceApi()).Run(profile, settings);
            await writer.WriteLineAsync("OK");
            foreach (var line in result.Split(Environment.NewLine))
            {
                await writer.WriteLineAsync(line);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not run profile {ProfileId}.", profileId);
            await writer.WriteLineAsync($"ERROR: {ex.Message}");
        }
    }
}
