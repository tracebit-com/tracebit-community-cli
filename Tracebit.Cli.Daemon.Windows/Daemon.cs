using System.CommandLine;

using Spectre.Console;

using Tracebit.Cli.Commands;
using Tracebit.Cli.Extensions;

namespace Tracebit.Cli.Daemon.Windows;

public class Daemon
{
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromHours(1);

    internal static Option<int> IntervalOption = new(name: "--interval")
    {
        Description = "Refresh interval in minutes",
        DefaultValueFactory = (_) => (int)DefaultRefreshInterval.TotalMinutes
    };

    public static async Task<int> RunAsync(IServiceProvider services, ParseResult parseResult, CancellationToken cancellationToken)
    {
        var verbose = parseResult.GetValue(GlobalOptions.Verbose);
        var errorDetail = parseResult.GetValue(GlobalOptions.ErrorDetail);
        var stacktrace = parseResult.GetValue(GlobalOptions.Stacktrace);
        var intervalMinutes = parseResult.GetValue(IntervalOption);
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        if (verbose)
        {
            AnsiConsole.MarkupLineInterpolated($"Starting daemon mode with refresh interval: {interval.TotalMinutes} minutes");
        }

        // Link to the provided cancellation token (System.CommandLine handles Ctrl+C for us)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                if (verbose)
                {
                    AnsiConsole.WriteLine($"{DateTime.Now:u} Running refresh check");
                }

                // Call the refresh action directly
                await Refresh.RunRefreshActionAsync(services, parseResult, cts.Token);

                if (verbose)
                {
                    AnsiConsole.MarkupLineInterpolated($"{DateTime.Now:u} Refresh complete, next check in {interval.TotalMinutes} minutes");
                }
            }
            catch (TaskCanceledException) when (cts.IsCancellationRequested)
            { }
            catch (Exception e)
            {
                // Don't exit on exceptions - only when the context is cancelled
                AnsiConsole.Console.WritePrettyException(e, errorDetail, stacktrace);
            }

            await Task.Delay(interval, cts.Token);
        }

        return 0;
    }
}
