using Tracebit.Cli.E2E.Tests.Helpers;

namespace Tracebit.Cli.E2E.Tests;

/// <summary>
/// Basic E2E tests for CLI execution
/// </summary>
public class BasicExecutionTests
{
    [Fact]
    public async Task Help_WithNoArguments_ShowsUsage()
    {
        var result = await CliTestHelper.ExecuteAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Tracebit CLI", result.StandardOutput);
        Assert.Contains("auth", result.StandardOutput);
        Assert.Contains("deploy", result.StandardOutput);
        Assert.Contains("show", result.StandardOutput);
    }

    [Fact]
    public async Task Version_WithVersionFlag_ShowsVersion()
    {
        var result = await CliTestHelper.ExecuteAsync("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrEmpty(result.StandardOutput));
        // Version should be in format like "0.0.1" or "1.0.0"
        Assert.Matches(@"\d+\.\d+\.\d+", result.StandardOutput);
    }

    [Fact]
    public async Task InvalidCommand_ShowsError()
    {
        var result = await CliTestHelper.ExecuteAsync("invalid-command");

        Assert.NotEqual(0, result.ExitCode);
        // System.CommandLine shows suggestions for invalid commands
        Assert.False(string.IsNullOrEmpty(result.StandardError));
    }

    [Fact]
    public async Task AuthHelp_ShowsAuthOptions()
    {
        var result = await CliTestHelper.ExecuteAsync("auth --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("auth", result.StandardOutput);
        Assert.Contains("Authenticate", result.StandardOutput);
        Assert.Contains("status", result.StandardOutput);
    }

    [Fact]
    public async Task DeployHelp_ShowsDeployOptions()
    {
        var result = await CliTestHelper.ExecuteAsync("deploy --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("deploy", result.StandardOutput);
        Assert.Contains("canaries", result.StandardOutput);
        Assert.Contains("aws", result.StandardOutput);
        Assert.Contains("ssh", result.StandardOutput);
    }

    [Fact]
    public async Task ShowCommand_WithoutCredentials_ShowsHelpfulMessage()
    {
        var result = await CliTestHelper.ExecuteAsync("show");

        Assert.Equal(0, result.ExitCode);
        // Should show message about no credentials or empty state
        Assert.False(string.IsNullOrEmpty(result.StandardOutput));
    }
}

