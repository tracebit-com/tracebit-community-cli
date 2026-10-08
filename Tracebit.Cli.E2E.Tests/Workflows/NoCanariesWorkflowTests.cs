using Tracebit.Cli.E2E.Tests.Helpers;

namespace Tracebit.Cli.E2E.Tests.Workflows;

/// <summary>
/// E2E tests for commands run before any canary is deployed, and for input the CLI must refuse before it calls the
/// API or writes anything.
/// </summary>
public class NoCanariesWorkflowTests
{
    [Fact]
    public async Task Show_NoCanaries_SuggestsDeploy()
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync("show");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No credentials are deployed on this device yet", result.StandardOutput);
        Assert.Contains("deploy", result.StandardOutput);
    }

    [Theory]
    [InlineData("show aws")]
    [InlineData("show ssh")]
    [InlineData("show email")]
    public async Task ShowType_NoCanaries_Succeeds(string command)
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync(command);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("deploy", result.StandardOutput);
    }

    [Theory]
    [InlineData("remove all")]
    [InlineData("remove aws")]
    [InlineData("remove ssh")]
    [InlineData("trigger")]
    [InlineData("trigger aws")]
    [InlineData("trigger ssh")]
    public async Task RemoveOrTrigger_NoCanaries_FailsWithHint(string command)
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync(command);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("deploy", result.StandardOutput);
    }

    [Fact]
    public async Task Refresh_NoCanaries_Succeeds()
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync("refresh");

        Assert.Equal(0, result.ExitCode);
    }

    [SandboxedFact]
    public async Task DeployAws_InjectedProfileName_IsRefusedBeforeWritingAnything()
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync("deploy aws --aws-profile \"evil]\\n[default\"");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid AWS profile name", result.StandardOutput + result.StandardError);
        Assert.False(Directory.Exists(Path.Combine(env.HomeDirectory, ".aws")));
        Assert.Empty(env.ReadState());
    }

    [SandboxedFact]
    public async Task DeployAws_InvalidRegion_IsRefusedBeforeWritingAnything()
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync("deploy aws --aws-region \"us-east-1 extra\"");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(env.HomeDirectory, ".aws")));
        Assert.Empty(env.ReadState());
    }

    [SandboxedFact]
    public async Task DeploySsh_KeyFileOutsideSshDirectory_IsRefusedBeforeWritingAnything()
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync("deploy ssh --ssh-key-file ../evil");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid SSH key file name", result.StandardOutput + result.StandardError);
        Assert.False(Directory.Exists(env.SshDirectory));
        Assert.False(File.Exists(Path.Combine(env.HomeDirectory, "evil")));
        Assert.Empty(env.ReadState());
    }

    [SandboxedFact]
    public async Task Deploy_MalformedLabels_IsRefused()
    {
        await using var env = new IsolatedCliEnvironment();

        var result = await env.RunAsync("deploy aws --labels novalue");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid label format", result.StandardOutput + result.StandardError);
        Assert.Empty(env.ReadState());
    }

    [Fact]
    public async Task PlaintextTracebitUrl_IsRefused()
    {
        await using var env = new IsolatedCliEnvironment();
        env.SetEnvironmentVariable("TRACEBIT_URL", "http://community.stage.tracebit.com");

        var result = await env.RunAsync("show");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("TRACEBIT_URL must use https", result.StandardOutput + result.StandardError);
    }
}
