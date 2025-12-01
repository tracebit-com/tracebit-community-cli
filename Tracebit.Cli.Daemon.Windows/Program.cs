using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using Tracebit.Cli.Commands;

namespace Tracebit.Cli.Daemon.Windows;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();
        Cli.Program.ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        var rootCommand = new RootCommand("Tracebit Daemon - Automatically refresh canaries in the background")
        {
            Daemon.IntervalOption,
            GlobalOptions.ErrorDetail,
            GlobalOptions.Stacktrace,
            GlobalOptions.Verbose
        };

        rootCommand.SetAction((parseResult, cancellationToken) => Daemon.RunAsync(serviceProvider, parseResult, cancellationToken));

        var parseResult = rootCommand.Parse(args);
        parseResult.InvocationConfiguration.EnableDefaultExceptionHandler = false;

        // Pass a default token - the Daemon.RunAsync method creates its own CancellationTokenSource
        return await parseResult.InvokeAsync(null, CancellationToken.None);
    }
}
