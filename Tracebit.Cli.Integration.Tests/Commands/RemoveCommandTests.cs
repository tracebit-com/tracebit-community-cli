using Tracebit.Cli.Deploy;
using Tracebit.Cli.State;
using Tracebit.Cli.Tests.Helpers;

namespace Tracebit.Cli.Integration.Tests.Commands;

/// <summary>
/// Tests for the remove command running against a mock API and an isolated home directory
/// </summary>
[Collection(CommandCollection.Name)]
public class RemoveCommandTests(CommandFixture fixture) : CommandTestBase(fixture)
{
    private string AwsCredentialsFile => Path.Combine(TempDirectory, ".aws", "credentials");
    private string SshConfigFile => Path.Combine(TempDirectory, ".ssh", "config");

    private static async Task DeployAwsAsync(string name = "canary", string profile = "canary-profile")
    {
        var credentials = TestDataBuilder.BuildAwsCredentials();
        await Deployer.WriteAwsKeyToConfigAsync(credentials, profile, "eu-west-1", CancellationToken.None);
        StateManager.AddAwsCredential(name, null, credentials, profile, "eu-west-1");
    }

    private static async Task<SshCredentialData> DeploySshAsync(string name = "canary")
    {
        var credentials = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3");
        await Deployer.WriteSshKeyToConfig(credentials, "canary-key", CancellationToken.None);
        return StateManager.AddSshCredential(name, null, credentials, "canary-key");
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("remove all")]
    [InlineData("remove aws")]
    [InlineData("remove ssh")]
    [InlineData("remove cookie")]
    [InlineData("remove username-password")]
    [InlineData("remove email")]
    public async Task Remove_NoCredentials_ReturnsErrorAndPrintsHint(string commandLine)
    {
        using var _ = Isolate();

        var exitCode = await RunAsync(commandLine);

        Assert.Equal(1, exitCode);
        Assert.Contains("No credentials are deployed on this device yet", Console.Output);
        Assert.Empty(RemoveRequests());
    }

    [Fact]
    public async Task RemoveAws_Selected_ExpiresCredentialAndRemovesProfile()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        await DeployAwsAsync();
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("remove aws");

        Assert.Equal(0, exitCode);
        Assert.Equal([("canary", AwsCredentialData.TypeDiscriminator)], RemoveRequests());
        Assert.Empty(StateManager.Credentials);
        Assert.DoesNotContain("[canary-profile]", await File.ReadAllTextAsync(AwsCredentialsFile));
        Assert.Contains("Credential canary (AWS) removed", Console.Output);
        Assert.Contains("won't be monitored by Tracebit", Console.Output);
    }

    [Fact]
    public async Task RemoveAws_Cancelled_KeepsCredential()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        await DeployAwsAsync();
        Console.Input.PushKey(ConsoleKey.DownArrow);
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("remove aws");

        Assert.Equal(1, exitCode);
        Assert.Empty(RemoveRequests());
        Assert.Single(StateManager.AwsCredentials);
        Assert.Contains("[canary-profile]", await File.ReadAllTextAsync(AwsCredentialsFile));
    }

    [Fact]
    public async Task RemoveSsh_Selected_ExpiresCredentialAndRemovesKeyAndConfig()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        var ssh = await DeploySshAsync();
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("remove ssh");

        Assert.Equal(0, exitCode);
        Assert.Equal([("canary", SshCredentialData.TypeDiscriminator)], RemoveRequests());
        Assert.Empty(StateManager.Credentials);
        Assert.False(File.Exists(ssh.Path));
        Assert.DoesNotContain("10.1.2.3", await File.ReadAllTextAsync(SshConfigFile));
    }

    [Fact]
    public async Task RemoveEmail_Selected_ExpiresCredentialAndPrintsInboxDetails()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        StateManager.AddEmailCredential("canary", null, EmailResponse());
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("remove email");

        Assert.Equal(0, exitCode);
        Assert.Equal([("canary", EmailCredentialData.TypeDiscriminator)], RemoveRequests());
        Assert.Empty(StateManager.Credentials);
        Assert.Contains("Sender: alerts@example.com", Console.Output);
        Assert.Contains("Subject: Your backup codes", Console.Output);
    }

    [Fact]
    public async Task RemoveCookie_Confirmed_ExpiresCredential()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        StateManager.AddBrowserCookieCredential("canary", null, HttpCredentials());
        Console.Input.PushKey(ConsoleKey.Enter);
        Console.Input.PushTextWithEnter("y");

        var exitCode = await RunAsync("remove cookie");

        Assert.Equal(0, exitCode);
        Assert.Equal([("canary", GitlabCookieCredentialData.TypeDiscriminator)], RemoveRequests());
        Assert.Empty(StateManager.Credentials);
        Assert.Contains("clear site data for the domain gitlab.example.com", Console.Output);
    }

    [Fact]
    public async Task RemoveCookie_Declined_KeepsCredential()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        StateManager.AddBrowserCookieCredential("canary", null, HttpCredentials());
        Console.Input.PushKey(ConsoleKey.Enter);
        Console.Input.PushTextWithEnter("n");

        var exitCode = await RunAsync("remove cookie");

        Assert.Equal(1, exitCode);
        Assert.Empty(RemoveRequests());
        Assert.Single(StateManager.BrowserCookieCredentials);
    }

    [Fact]
    public async Task RemoveCookie_Force_SkipsConfirmation()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        StateManager.AddBrowserCookieCredential("canary", null, HttpCredentials());
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("remove cookie --force");

        Assert.Equal(0, exitCode);
        Assert.Single(RemoveRequests());
        Assert.Empty(StateManager.Credentials);
        Assert.DoesNotContain("clear site data", Console.Output);
    }

    [Fact]
    public async Task RemoveUsernamePassword_Confirmed_ExpiresCredential()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        StateManager.AddUsernamePasswordCredential("canary", null, HttpCredentials());
        Console.Input.PushKey(ConsoleKey.Enter);
        Console.Input.PushTextWithEnter("y");

        var exitCode = await RunAsync("remove username-password");

        Assert.Equal(0, exitCode);
        Assert.Equal([("canary", GitlabUsernamePasswordCredentialData.TypeDiscriminator)], RemoveRequests());
        Assert.Empty(StateManager.Credentials);
    }

    [Fact]
    public async Task RemoveUsernamePassword_Declined_KeepsCredential()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        StateManager.AddUsernamePasswordCredential("canary", null, HttpCredentials());
        Console.Input.PushKey(ConsoleKey.Enter);
        Console.Input.PushTextWithEnter("n");

        var exitCode = await RunAsync("remove username-password");

        Assert.Equal(1, exitCode);
        Assert.Empty(RemoveRequests());
        Assert.Single(StateManager.UsernamePasswordCanaries);
        Assert.Contains("Username/password canary not removed", Console.Output);
    }

    [Fact]
    public async Task Remove_ApiFailure_ThrowsAndKeepsCredential()
    {
        using var _ = Isolate();
        SetupRemoveResponse(500);
        await DeployAwsAsync();
        Console.Input.PushKey(ConsoleKey.Enter);

        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync("remove aws"));

        Assert.Single(StateManager.AwsCredentials);
        Assert.Contains("[canary-profile]", await File.ReadAllTextAsync(AwsCredentialsFile));
    }

    [Fact]
    public async Task Remove_ApiFailureWithForce_RemovesCredentialAnyway()
    {
        using var _ = Isolate();
        SetupRemoveResponse(500);
        await DeployAwsAsync();
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("remove aws --force");

        Assert.Equal(0, exitCode);
        Assert.Empty(StateManager.Credentials);
        Assert.DoesNotContain("[canary-profile]", await File.ReadAllTextAsync(AwsCredentialsFile));
        Assert.Contains("Credential could not be deactivated due to 'InternalServerError'", Console.Output);
    }

    [Fact]
    public async Task Remove_SelectFromAllTypes_RemovesSelectedCredential()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        await DeployAwsAsync();
        StateManager.AddEmailCredential("canary", null, EmailResponse());
        Console.Input.PushKey(ConsoleKey.DownArrow);
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("remove");

        Assert.Equal(0, exitCode);
        Assert.Equal([("canary", EmailCredentialData.TypeDiscriminator)], RemoveRequests());
        var remaining = Assert.Single(StateManager.Credentials);
        Assert.IsType<AwsCredentialData>(remaining);
    }

    [Fact]
    public async Task RemoveAll_RemovesEveryCredential()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        await DeployAwsAsync();
        await DeploySshAsync();
        StateManager.AddEmailCredential("canary", null, EmailResponse());

        var exitCode = await RunAsync("remove all");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [AwsCredentialData.TypeDiscriminator, SshCredentialData.TypeDiscriminator, EmailCredentialData.TypeDiscriminator],
            RemoveRequests().Select(r => r.Type));
        Assert.Empty(StateManager.Credentials);
    }

    [Fact]
    public async Task RemoveAll_DeclinedPrompt_ReportsCancelledAndContinues()
    {
        using var _ = Isolate();
        SetupRemoveResponse();
        StateManager.AddBrowserCookieCredential("canary", null, HttpCredentials());
        StateManager.AddEmailCredential("canary", null, EmailResponse());
        Console.Input.PushTextWithEnter("n");

        var exitCode = await RunAsync("remove all");

        Assert.Equal(0, exitCode);
        Assert.Contains("Removal of canary (Cookie) cancelled", Console.Output);
        var remaining = Assert.Single(StateManager.Credentials);
        Assert.IsType<GitlabCookieCredentialData>(remaining);
    }

    [Fact]
    public async Task RemoveAll_ApiFailure_ReportsNetworkErrorAndKeepsCredentials()
    {
        using var _ = Isolate();
        SetupRemoveResponse(500);
        await DeployAwsAsync();
        StateManager.AddEmailCredential("canary", null, EmailResponse());

        var exitCode = await RunAsync("remove all");

        Assert.Equal(0, exitCode);
        Assert.Contains("canary (AWS) not removed due to a network error", Console.Output);
        Assert.Contains("canary (Email) not removed due to a network error", Console.Output);
        Assert.Equal(2, StateManager.Credentials.Count);
    }
}
