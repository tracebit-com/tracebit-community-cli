using Tracebit.Cli.E2E.Tests.Helpers;

namespace Tracebit.Cli.E2E.Tests.Workflows;

/// <summary>
/// E2E tests for authentication. These only touch the sandboxed config directory and never reach the API.
/// </summary>
public class AuthWorkflowTests
{
    [Fact]
    public async Task AuthStatus_SubcommandHelp_ShowsStatusHelp()
    {
        var result = await CliTestHelper.ExecuteAsync("auth status --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("status", result.StandardOutput);
        Assert.Contains("authentication", result.StandardOutput);
    }

    [Fact]
    public async Task AuthStatus_NotLoggedIn_ExplainsHowToLogIn()
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync("auth status");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not yet logged into Tracebit", result.StandardOutput);
        Assert.Contains("auth", result.StandardOutput);
    }

    [Fact]
    public async Task AuthStatus_TokenForThisEnvironment_IsValid()
    {
        await using var env = new IsolatedCliEnvironment();
        await env.WriteTokenAsync(TestTokens.Create(DateTimeOffset.UtcNow.AddDays(1)));

        var result = await env.RunAsync("auth status --verbose");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Tracebit credentials are valid", result.StandardOutput);
        Assert.Contains("Credentials expire:", result.StandardOutput);
    }

    [Fact]
    public async Task AuthStatus_ExpiredToken_IsRejected()
    {
        await using var env = new IsolatedCliEnvironment();
        await env.WriteTokenAsync(TestTokens.Create(DateTimeOffset.UtcNow.AddMinutes(-5)));

        var result = await env.RunAsync("auth status");

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("credentials are valid", result.StandardOutput);
        Assert.Contains("invalid", result.StandardOutput);
    }

    [Fact]
    public async Task AuthStatus_ProductionTokenOnStaging_IsRejected()
    {
        await using var env = new IsolatedCliEnvironment(tracebitEnvironment: "stage");
        await env.WriteTokenAsync(TestTokens.Create(DateTimeOffset.UtcNow.AddDays(1), issuer: TestTokens.ProductionHost));

        var result = await env.RunAsync("auth status");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("API token is invalid", result.StandardOutput);
    }

    [Fact]
    public async Task AuthStatus_StagingTokenOnProduction_IsRejected()
    {
        await using var env = new IsolatedCliEnvironment(tracebitEnvironment: "production");
        await env.WriteTokenAsync(TestTokens.Create(DateTimeOffset.UtcNow.AddDays(1), issuer: TestTokens.StagingHost));

        var result = await env.RunAsync("auth status");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("API token is invalid", result.StandardOutput);
    }

    [Fact]
    public async Task AuthStatus_CorruptCredentialsFile_TellsUserToDeleteIt()
    {
        await using var env = new IsolatedCliEnvironment();
        await File.WriteAllTextAsync(Path.Combine(env.ConfigDirectory, "credentials.json"), "{not json");

        var result = await env.RunAsync("auth status");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("credentials file might be corrupted", result.StandardOutput + result.StandardError);
    }

    [Fact]
    public async Task AuthToken_NonInteractive_FailsInsteadOfHanging()
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync("auth --token");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(env.ConfigDirectory, "credentials.json")) &&
                     new FileInfo(Path.Combine(env.ConfigDirectory, "credentials.json")).Length > 0,
            "No token should be stored when none was entered");
    }
}
