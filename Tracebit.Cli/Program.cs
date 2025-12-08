using System.CommandLine;
using System.Net.Http.Headers;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Tracebit.Cli.API;
using Tracebit.Cli.Config;
using Tracebit.Cli.Extensions;

namespace Tracebit.Cli;

public class Program
{
    // The name of the application (for use in e.g. config paths).
    // Note that this is not necessarily the same as the name of the binary that was called.
    public const string AppName = "tracebit";

    private static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler mainCancelHandler = (_, consoleArgs) =>
        {
            consoleArgs.Cancel = true;
            try
            {
                // ReSharper disable once AccessToDisposedClosure
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
        };
        Console.CancelKeyPress += mainCancelHandler;

        // Start fetching the latest release in the background while we perform the main command
        var client = serviceProvider.GetRequiredService<CliReleaseClient>();
        var getLatestCliRelease = client.GetLatestReleaseAsync(cts.Token);

        // Do the status update in the background while we perform the main command
        var tracebitClient = serviceProvider.GetRequiredService<TracebitClient>();
        var updateStatus = tracebitClient.UpdateStatusAsync(Environment.MachineName, cts.Token);

        var rootCommand = RootCommand();

        rootCommand.Subcommands.Add(Commands.Auth.Command(serviceProvider));
        rootCommand.Subcommands.Add(Commands.Deploy.Command(serviceProvider));
        rootCommand.Subcommands.Add(Commands.Portal.Command());
        rootCommand.Subcommands.Add(Commands.Refresh.Command(serviceProvider));
        rootCommand.Subcommands.Add(Commands.Remove.Command(serviceProvider));
        rootCommand.Subcommands.Add(Commands.Show.Command());
        rootCommand.Subcommands.Add(Commands.Trigger.Command());

        rootCommand.SetAction(async (parseResult, cancellationToken) => await Commands.Interactive.InteractiveAsync(parseResult, mainCancelHandler, cancellationToken));

        var parseResult = rootCommand.Parse(args);
        parseResult.InvocationConfiguration.EnableDefaultExceptionHandler = false;

        var exitCode = 1;
        try
        {
            exitCode = await parseResult.InvokeAsync(null, cts.Token);
        }
        catch (OperationCanceledException)
        {
            exitCode = 0;
        }
        catch (Exception e)
        {
            AnsiConsole.Console.WritePrettyException(e,
                parseResult.GetRequiredValue(Commands.GlobalOptions.ErrorDetail),
                parseResult.GetRequiredValue(Commands.GlobalOptions.Stacktrace));
        }

        await Task.WhenAny(
            Commands.Utils.ReportUpdateStatusFailures(parseResult, updateStatus),
            Task.Delay(1000, CancellationToken.None)
        );
        await Task.WhenAny(
            Commands.Utils.NotifyForUpdatesAsync(parseResult, getLatestCliRelease),
            Task.Delay(1000, CancellationToken.None)
        );

        return exitCode;
    }

    public static RootCommand RootCommand()
    {
        return new("Tracebit CLI")
        {
            Commands.GlobalOptions.ErrorDetail,
            Commands.GlobalOptions.Stacktrace,
            Commands.GlobalOptions.Verbose,
            Commands.GlobalOptions.Interactive
        };
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient(_ => Credentials.LoadCredsFromFile(CancellationToken.None).Result);

        services.ConfigureHttpClientDefaults(builder => builder.ConfigureHttpClient(client =>
        {
            client.DefaultRequestHeaders.UserAgent.Add(new("TracebitCli", Commands.Utils.GetCurrentVersion()?.ToString() ?? "Unknown"));
        }));

        services.AddHttpClient<TracebitClient>((serviceProvider, client) =>
        {
            var credentials = serviceProvider.GetRequiredService<Credentials>();
            client.BaseAddress = Constants.TracebitApiUrl;
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", credentials.Token);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Disable redirects to prevent potential confusion and wrong error handling
            AllowAutoRedirect = false
        })
        .AddHttpMessageHandler((serviceProvider) =>
        {
            var credentials = serviceProvider.GetRequiredService<Credentials>();
            return new TracebitApiAuthFailureHandler(credentials);
        });

        services.AddHttpClient<CliReleaseClient>((_, client) =>
        {
            client.BaseAddress = Constants.TracebitApiUrl;
        });
    }
}
