using System.CommandLine;
using System.Diagnostics;

using Spectre.Console;

using Tracebit.Cli.API;

namespace Tracebit.Cli.Commands;

static class Portal
{
    public static Command Command()
    {
        var command = new Command("portal", "Open the Tracebit Portal in your browser");

        command.SetAction(_ =>
        {
            var portalUrl = Constants.TracebitUrl;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = portalUrl.ToString(), UseShellExecute = true });
                AnsiConsole.MarkupLineInterpolated($"Opening [blue link]{portalUrl}[/] in your browser");
            }
            catch
            {
                AnsiConsole.MarkupLineInterpolated($"Open [blue link]{portalUrl}[/] in your browser to continue");
            }

            return 0;
        });

        return command;
    }
}
