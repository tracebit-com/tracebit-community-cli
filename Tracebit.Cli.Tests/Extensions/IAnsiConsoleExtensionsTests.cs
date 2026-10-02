using Spectre.Console;

using Tracebit.Cli.Commands;
using Tracebit.Cli.Extensions;

namespace Tracebit.Cli.Tests.Extensions;

public class IAnsiConsoleExtensionsTests
{
    private static (IAnsiConsole console, StringWriter output) CreateConsole()
    {
        var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output)
        });
        return (console, output);
    }

    [Fact]
    public void WritePrettyException_PlainException_WritesMessageOnly()
    {
        var (console, output) = CreateConsole();

        console.WritePrettyException(new InvalidOperationException("Something broke"), errorDetail: false, stacktrace: false);

        Assert.Equal("Something broke", output.ToString().Trim());
    }

    [Fact]
    public void WritePrettyException_MessageWithBrackets_IsNotInterpretedAsMarkup()
    {
        var (console, output) = CreateConsole();

        console.WritePrettyException(new Exception("Profile [canary] not found"), errorDetail: false, stacktrace: false);

        Assert.Contains("Profile [canary] not found", output.ToString());
    }

    [Fact]
    public void WritePrettyException_MarkupException_RendersMarkupAsText()
    {
        var (console, output) = CreateConsole();

        console.WritePrettyException(new MarkupException($"Run [purple]tracebit auth[/] to log in"), errorDetail: false, stacktrace: false);

        Assert.Equal("Run tracebit auth to log in", output.ToString().Trim());
    }

    [Fact]
    public void WritePrettyException_AggregateException_WritesEachInnerException()
    {
        var (console, output) = CreateConsole();
        var aggregate = new AggregateException(new Exception("first failure"), new MarkupException($"[red]second failure[/]"));

        console.WritePrettyException(aggregate, errorDetail: false, stacktrace: false);

        var text = output.ToString();
        Assert.Contains("first failure", text);
        Assert.Contains("second failure", text);
        Assert.DoesNotContain("One or more errors occurred", text);
    }

    [Fact]
    public void WritePrettyException_ErrorDetail_IncludesExceptionTypeWithoutStackTrace()
    {
        var (console, output) = CreateConsole();
        Exception exception;
        try
        {
            throw new InvalidOperationException("detailed failure");
        }
        catch (Exception e)
        {
            exception = e;
        }

        console.WritePrettyException(exception, errorDetail: true, stacktrace: false);

        var text = output.ToString();
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("detailed failure", text);
        Assert.DoesNotContain(nameof(WritePrettyException_ErrorDetail_IncludesExceptionTypeWithoutStackTrace), text);
    }

    [Fact]
    public void WritePrettyException_Stacktrace_IncludesStackFrames()
    {
        var (console, output) = CreateConsole();
        Exception exception;
        try
        {
            throw new InvalidOperationException("traced failure");
        }
        catch (Exception e)
        {
            exception = e;
        }

        console.WritePrettyException(exception, errorDetail: false, stacktrace: true);

        var text = output.ToString();
        Assert.Contains("traced failure", text);
        Assert.Contains(nameof(WritePrettyException_Stacktrace_IncludesStackFrames), text);
    }
}
