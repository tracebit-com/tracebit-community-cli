using System.CommandLine;

using Tracebit.Cli.Commands;
using Tracebit.Cli.State;

namespace Tracebit.Cli.Tests.Commands;

public class MarkupExceptionTests
{
    [Fact]
    public void Message_HasMarkupRemoved()
    {
        var exception = new MarkupException($"[red]Something [purple]bad[/] happened[/]");

        Assert.Equal("Something bad happened", exception.Message);
    }

    [Fact]
    public void FormattableMessage_RetainsMarkup()
    {
        var exception = new MarkupException($"[red]Something bad happened[/]");

        Assert.Equal("[red]Something bad happened[/]", exception.FormattableMessage.ToString());
    }

    [Fact]
    public void InnerException_IsPreserved()
    {
        var inner = new InvalidOperationException("inner");

        var exception = new MarkupException($"outer", inner);

        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void CredentialAlreadyExistsException_MentionsCredentialAndRemoveCommand()
    {
        var parseResult = new RootCommand("test").Parse("");
        var existing = new AwsCredentialData { Name = "my-canary", CreatedAt = DateTime.Now, ExpiresAt = null, Path = "" };

        var exception = new CredentialAlreadyExistsException(parseResult, existing, "--name");

        Assert.Contains("my-canary", exception.Message);
        Assert.Contains("(AWS)", exception.Message);
        Assert.Contains("--name", exception.Message);
        Assert.Contains("remove", exception.Message);
    }

    [Fact]
    public void EmailCredentialAlreadyExistsException_DoesNotSuggestRenaming()
    {
        var parseResult = new RootCommand("test").Parse("");
        var existing = new EmailCredentialData { Name = "Backup Codes Email", CreatedAt = DateTime.Now, ExpiresAt = null, Path = "" };

        var exception = new EmailCredentialAlreadyExistsException(parseResult, existing);

        Assert.IsAssignableFrom<CredentialAlreadyExistsException>(exception);
        Assert.Contains("Backup Codes Email", exception.Message);
        Assert.DoesNotContain("--name", exception.Message);
        Assert.Contains("remove", exception.Message);
    }
}
