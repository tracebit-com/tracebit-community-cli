using Tracebit.Cli.Commands;
using Tracebit.Cli.State;
using Tracebit.Cli.Tests.Helpers;

namespace Tracebit.Cli.Integration.Tests.Commands;

/// <summary>
/// Tests for the trigger command paths that don't shell out to the aws/ssh CLIs or open a browser
/// </summary>
[Collection(CommandCollection.Name)]
public class TriggerCommandTests(CommandFixture fixture) : CommandTestBase(fixture)
{
    [Fact]
    public async Task Trigger_NoCredentials_ReturnsErrorAndPrintsHint()
    {
        using var _ = Isolate();

        var exitCode = await RunAsync("trigger");

        Assert.Equal(1, exitCode);
        Assert.Contains("No credentials are deployed on this device yet", Console.Output);
    }

    [Theory]
    [InlineData("trigger aws", "AWS")]
    [InlineData("trigger ssh", "SSH")]
    [InlineData("trigger cookie", "cookie")]
    [InlineData("trigger username-password", "username/password")]
    [InlineData("trigger email", "email")]
    public async Task TriggerType_NoCredentialsOfType_ReturnsErrorAndPrintsHint(string commandLine, string prettyTypeName)
    {
        using var _ = Isolate();

        var exitCode = await RunAsync(commandLine);

        Assert.Equal(1, exitCode);
        Assert.Contains($"No {prettyTypeName} credentials are deployed on this device yet", Console.Output);
    }

    [Theory]
    [InlineData("trigger aws --name missing", "AWS credential missing not found")]
    [InlineData("trigger ssh --name missing", "SSH credential missing not found")]
    [InlineData("trigger cookie --name missing", "Cookie credential missing not found")]
    [InlineData("trigger username-password --name missing", "Username/Password credential missing not found")]
    public async Task TriggerType_UnknownName_Throws(string commandLine, string expectedMessage)
    {
        using var _ = Isolate();
        StateManager.AddAwsCredential("canary", null, TestDataBuilder.BuildAwsCredentials(), "canary-profile", "eu-west-1");
        StateManager.AddSshCredential("canary", null, TestDataBuilder.BuildSshCredentials(), "canary-key");
        StateManager.AddBrowserCookieCredential("canary", null, HttpCredentials());
        StateManager.AddUsernamePasswordCredential("canary", null, HttpCredentials());

        var exception = await Assert.ThrowsAsync<MarkupException>(() => RunAsync(commandLine));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task TriggerUsernamePassword_ByName_PrintsInstructions()
    {
        using var _ = Isolate();
        StateManager.AddUsernamePasswordCredential("canary", null, HttpCredentials());

        var exitCode = await RunAsync("trigger username-password --name canary");

        Assert.Equal(0, exitCode);
        Assert.Contains("To trigger canary (Username/Password) go to https://gitlab.example.com/", Console.Output);
    }

    [Fact]
    public async Task TriggerUsernamePassword_Prompted_PrintsInstructions()
    {
        using var _ = Isolate();
        StateManager.AddUsernamePasswordCredential("canary", null, HttpCredentials());
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("trigger username-password");

        Assert.Equal(0, exitCode);
        Assert.Contains("Select canary to trigger:", Console.Output);
        Assert.Contains("https://gitlab.example.com/", Console.Output);
    }

    [Fact]
    public async Task Trigger_PromptCancelled_ReturnsError()
    {
        using var _ = Isolate();
        StateManager.AddUsernamePasswordCredential("canary", null, HttpCredentials());
        Console.Input.PushKey(ConsoleKey.DownArrow);
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("trigger");

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("To trigger", Console.Output);
    }

    [Fact]
    public async Task Trigger_PromptSelected_TriggersSelectedCredential()
    {
        using var _ = Isolate();
        StateManager.AddUsernamePasswordCredential("canary", null, HttpCredentials());
        StateManager.AddEmailCredential("canary", null, EmailResponse());
        Console.Input.PushKey(ConsoleKey.DownArrow);
        Console.Input.PushKey(ConsoleKey.Enter);

        var exitCode = await RunAsync("trigger");

        Assert.Equal(0, exitCode);
        Assert.Contains("go to your 'me@example.com' email inbox", Console.Output);
    }

    [Fact]
    public async Task TriggerEmail_Triggerable_PrintsInstructions()
    {
        using var _ = Isolate();
        StateManager.AddEmailCredential("canary", null, EmailResponse(DateTime.UtcNow.AddHours(-1)));

        var exitCode = await RunAsync("trigger email");

        Assert.Equal(0, exitCode);
        Assert.Contains("open the email from 'alerts@example.com' with the subject 'Your backup codes'", Console.Output);
        Assert.Contains("You must allow showing images in the email", Console.Output);
    }

    [Fact]
    public async Task TriggerEmail_NotYetTriggerable_Throws()
    {
        using var _ = Isolate();
        StateManager.AddEmailCredential("canary", null, EmailResponse(DateTime.UtcNow.AddHours(1)));

        var exception = await Assert.ThrowsAsync<MarkupException>(() => RunAsync("trigger email"));

        Assert.Contains("cannot be triggered until", exception.Message);
        Assert.DoesNotContain("To trigger", Console.Output);
    }
}
