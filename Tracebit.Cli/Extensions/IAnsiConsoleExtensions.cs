using System.Diagnostics.CodeAnalysis;

using Spectre.Console;

namespace Tracebit.Cli.Extensions;

public static class IAnsiConsoleExtensions
{
    [UnconditionalSuppressMessage("AOT", "IL3050:Requires Dynamic Code", Justification = "ExceptionSettings format is explicitly defined, safe for AOT")]
    public static void WritePrettyException(this IAnsiConsole console, Exception e, bool errorDetail, bool stacktrace)
    {
        if (e is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
            {
                console.WritePrettyException(inner, errorDetail, stacktrace);
            }
            return;
        }

        if (errorDetail || stacktrace)
        {
            var format = ExceptionFormats.ShowLinks | ExceptionFormats.ShortenEverything;
            if (!stacktrace)
                format |= ExceptionFormats.NoStackTrace;

            console.WriteException(e, new ExceptionSettings
            {
                Format = format,
                Style = new ExceptionStyle()
            });
            return;
        }

        if (e is Commands.MarkupException me)
        {
            console.MarkupLine($"[red]{me.FormattableMessage}[/]");
            return;
        }

        console.MarkupLineInterpolated($"[red]{e.Message}[/]");
    }
}

public static class SpinnerExtensions
{
    public static Status DefaultSpinner(this Status status)
    {
        return status.Spinner(Spinner.Known.BouncingBar).SpinnerStyle(new Style(foreground: Color.Purple));
    }
}

