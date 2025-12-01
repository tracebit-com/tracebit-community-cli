using Tracebit.Cli.E2E.Tests.Helpers;

namespace Tracebit.Cli.E2E.Tests.Workflows;

/// <summary>
/// E2E tests for authentication workflows
/// Note: These tests test the command structure and help output
/// Tests that interact with actual credentials state are in integration tests
/// </summary>
public class AuthWorkflowTests
{
    // Note: We can't easily test auth status with isolated credentials in E2E tests
    // because Environment.GetFolderPath doesn't respect env vars.
    // Tests for actual auth state belong in integration tests where we can mock dependencies.

    [Fact]
    public async Task AuthStatus_SubcommandHelp_ShowsStatusHelp()
    {
        var result = await CliTestHelper.ExecuteAsync("auth status --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status", result.StandardOutput);
        Assert.Contains("authentication", result.StandardOutput);
    }
}
