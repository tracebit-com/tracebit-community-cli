using System.CommandLine;

namespace Tracebit.Cli.Commands;

public static class GlobalOptions
{
    public static readonly Option<bool> Stacktrace = new("--stacktrace")
    {
        Description = "Enable full stacktraces for exceptions",
        DefaultValueFactory = _ => Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development",
        Hidden = true,
        Recursive = true
    };

    public static readonly Option<bool> ErrorDetail = new("--error-detail")
    {
        Description = "Enable detailed error output",
        Recursive = true
    };

    public static readonly Option<bool> Verbose = new("--verbose", "-v")
    {
        Description = "Enable verbose output",
        Recursive = true
    };

    internal static readonly Option<bool> Interactive = new("--interactive")
    {
        Description = "Whether to run in interactive mode",
        Recursive = true,
        Hidden = true,
        DefaultValueFactory = _ => false
    };
}
