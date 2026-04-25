using System.Security.Principal;
using Spectre.Console;
using Socar.WinServicesManager;

namespace Socar.WinServicesManager.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h", StringComparer.OrdinalIgnoreCase) || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            ShowHelp();
            return args.Length == 0 ? 1 : 0;
        }

        if (!IsAdministrator())
        {
            AnsiConsole.MarkupLine("[red]This application must be run as administrator to manipulate Windows services.[/]");
            AnsiConsole.MarkupLine("[yellow]No XML was loaded, no plan was listed, and no service changes were made.[/]");
            return 3;
        }

        var force = args.Any(arg => arg.Equals("-f", StringComparison.OrdinalIgnoreCase) || arg.Equals("--force", StringComparison.OrdinalIgnoreCase));
        var xmlPath = args.FirstOrDefault(arg => !arg.StartsWith("-", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(xmlPath))
        {
            ShowHelp();
            return 1;
        }

        if (!File.Exists(xmlPath))
        {
            AnsiConsole.MarkupLine($"[red]XML profile file was not found:[/] {Markup.Escape(xmlPath)}");
            return 2;
        }

        try
        {
            var profile = ProfileXmlSerializer.Load(xmlPath);
            var serviceApi = new NativeServiceApi();
            var services = AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Reading current Windows services...", _ => serviceApi.GetServices());

            ShowPlan(profile, services);

            if (!force && !AnsiConsole.Confirm("Apply this profile now?", false))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled. No service changes were made.[/]");
                return 0;
            }

            if (!IsAdministrator())
            {
                AnsiConsole.MarkupLine("[red]Administrator rights were lost before applying the profile.[/]");
                AnsiConsole.MarkupLine("[yellow]No service changes were made.[/]");
                return 3;
            }

            var settings = new AppSettings
            {
                DependencyStopPolicy = DependencyStopPolicy.AutoStopDependents
            };

            var result = AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .Start("Applying profile...", _ => new ProfileRunner(serviceApi).Run(profile, settings));

            AnsiConsole.Write(new Rule("[green]Run result[/]"));
            AnsiConsole.WriteLine(result);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return 10;
        }
    }

    private static void ShowHelp()
    {
        AnsiConsole.Write(new FigletText("SOCAR WinServicesManager Profile Runner").Color(Color.CornflowerBlue));
        AnsiConsole.MarkupLine("Runs a SOCAR WinServicesManager XML profile from the command line.");
        AnsiConsole.WriteLine();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Usage");
        table.AddColumn("Description");
        table.AddRow("[cyan]Socar.WinServicesManager.Cli.exe <profile.xml>[/]", "Show planned changes, ask for confirmation, then apply the profile.");
        table.AddRow("[cyan]Socar.WinServicesManager.Cli.exe <profile.xml> -f[/]", "Apply the profile without asking for confirmation.");
        table.AddRow("[cyan]Socar.WinServicesManager.Cli.exe -h[/]", "Show this help screen.");
        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("[yellow]Note:[/] run this console app as administrator. Services not present in the XML are left unchanged.");
    }

    private static void ShowPlan(ServiceProfile profile, IReadOnlyList<ServiceSummary> currentServices)
    {
        var services = currentServices.ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        AnsiConsole.Write(new Rule($"[blue]Profile: {Markup.Escape(profile.Name)}[/]"));

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Service");
        table.AddColumn("Current startup");
        table.AddColumn("Target startup");
        table.AddColumn("Current state");
        table.AddColumn("Target state");
        table.AddColumn("Plan");

        foreach (var action in profile.Actions.OrderBy(action => action.DisplayName ?? action.ServiceName, StringComparer.CurrentCultureIgnoreCase))
        {
            services.TryGetValue(action.ServiceName, out var service);
            var startupPlan = action.DesiredStartType is null
                ? "unchanged"
                : service?.StartType == action.DesiredStartType ? "skip startup" : "change startup";
            var statePlan = action.DesiredStatus is null
                ? "unchanged"
                : MatchesState(service?.State, action.DesiredStatus.Value) ? "skip state" : action.DesiredStatus.Value == DesiredServiceStatus.Running ? "start" : "stop";

            table.AddRow(
                Markup.Escape(action.DisplayName ?? action.ServiceName),
                Markup.Escape(service?.StartType?.ToString() ?? "not found"),
                Markup.Escape(action.DesiredStartType?.ToString() ?? "unchanged"),
                Markup.Escape(service?.State.ToString() ?? "not found"),
                Markup.Escape(action.DesiredStatus?.ToString() ?? "unchanged"),
                Markup.Escape($"{startupPlan}; {statePlan}"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Dependency policy for CLI runs: automatically stop dependent services.[/]");
    }

    private static bool MatchesState(ServiceRunState? currentState, DesiredServiceStatus target)
    {
        return target switch
        {
            DesiredServiceStatus.Running => currentState == ServiceRunState.Running,
            DesiredServiceStatus.Stopped => currentState == ServiceRunState.Stopped,
            _ => false
        };
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
