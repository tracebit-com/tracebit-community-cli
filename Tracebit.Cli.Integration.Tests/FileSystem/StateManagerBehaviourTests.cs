using System.CommandLine;
using System.Text.Json;

using Tracebit.Cli.API;
using Tracebit.Cli.API.CanaryCredentials;
using Tracebit.Cli.Commands;
using Tracebit.Cli.Integration.Tests.Helpers;
using Tracebit.Cli.State;
using Tracebit.Cli.Tests.Helpers;

namespace Tracebit.Cli.Integration.Tests.FileSystem;

/// <summary>
/// Tests for StateManager credential lookup, replacement and error behaviour
/// </summary>
public class StateManagerBehaviourTests : BaseIntegrationTest
{
    private string StateFile => Path.Combine(TempDirectory, "state.json");

    private static HttpCanaryCredentials HttpCredentials(string host = "gitlab.example.com") => new()
    {
        ConfirmationId = Guid.NewGuid(),
        BrowserDeploymentId = Guid.NewGuid(),
        HostNames = [host],
        ExpiresAt = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Credentials = JsonSerializer.SerializeToElement(new { })
    };

    [Fact]
    public void AddCredential_SameNameSameType_ReplacesExisting()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        StateManager.AddAwsCredential("canary", null, TestDataBuilder.BuildAwsCredentials(), "profile-1", "us-east-1");

        StateManager.AddAwsCredential("canary", null, TestDataBuilder.BuildAwsCredentials(), "profile-2", "eu-west-1");

        var aws = Assert.Single(StateManager.AwsCredentials);
        Assert.Equal("profile-2", aws.AwsProfile);
        Assert.Equal("eu-west-1", aws.AwsRegion);
    }

    [Fact]
    public void AddCredential_SameNameDifferentType_KeepsBoth()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);

        StateManager.AddAwsCredential("canary", null, TestDataBuilder.BuildAwsCredentials(), "default", "us-east-1");
        StateManager.AddSshCredential("canary", null, TestDataBuilder.BuildSshCredentials(), "canary-key");

        Assert.Equal(2, StateManager.Credentials.Count);
        Assert.Single(StateManager.AwsCredentials);
        Assert.Single(StateManager.SshCredentials);
    }

    [Fact]
    public void RemoveCredential_SameNameDifferentType_OnlyRemovesMatchingType()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        var aws = StateManager.AddAwsCredential("canary", null, TestDataBuilder.BuildAwsCredentials(), "default", "us-east-1");
        StateManager.AddSshCredential("canary", null, TestDataBuilder.BuildSshCredentials(), "canary-key");

        StateManager.RemoveCredential(aws);

        Assert.Empty(StateManager.AwsCredentials);
        Assert.Single(StateManager.SshCredentials);
    }

    [Fact]
    public void CheckIfCredentialExists_IsTypeSpecific()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        StateManager.AddAwsCredential("canary", null, TestDataBuilder.BuildAwsCredentials(), "default", "us-east-1");

        Assert.True(StateManager.CheckIfCredentialExists<AwsCredentialData>("canary"));
        Assert.False(StateManager.CheckIfCredentialExists<SshCredentialData>("canary"));
        Assert.False(StateManager.CheckIfCredentialExists<AwsCredentialData>("other"));
    }

    [Fact]
    public void EnsureCredentialDoesNotExist_ExistingCredential_Throws()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        StateManager.AddAwsCredential("canary", null, TestDataBuilder.BuildAwsCredentials(), "default", "us-east-1");
        var parseResult = new RootCommand().Parse("");

        var exception = Assert.Throws<CredentialAlreadyExistsException>(() =>
            StateManager.EnsureCredentialDoesNotExist<AwsCredentialData>("canary", parseResult, "--name"));
        Assert.Contains("canary", exception.Message);

        StateManager.EnsureCredentialDoesNotExist<SshCredentialData>("canary", parseResult, "--name");
        StateManager.EnsureCredentialDoesNotExist<AwsCredentialData>("other", parseResult, "--name");
    }

    [Fact]
    public void EnsureEmailCredentialDoesNotExist_ExistingEmail_ThrowsEmailSpecificException()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        StateManager.AddEmailCredential(Constants.EmailCanaryName, null, new GenerateAndSendCanaryEmailResponse
        {
            EmailTo = "to@example.com",
            EmailFrom = "from@example.com",
            EmailSubject = "subject",
            CredentialExpiresAt = null,
            CredentialTriggerableAfter = DateTime.UtcNow
        });
        var parseResult = new RootCommand().Parse("");

        Assert.Throws<EmailCredentialAlreadyExistsException>(() =>
            StateManager.EnsureEmailCredentialDoesNotExist(Constants.EmailCanaryName, parseResult));
    }

    [Fact]
    public void FilterNotExistingCanaryTypesForName_ExcludesDeployedTypesIncludingEmail()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        StateManager.AddAwsCredential("canary", null, TestDataBuilder.BuildAwsCredentials(), "default", "us-east-1");
        StateManager.AddSshCredential("other-name", null, TestDataBuilder.BuildSshCredentials(), "key");
        StateManager.AddEmailCredential(Constants.EmailCanaryName, null, new GenerateAndSendCanaryEmailResponse
        {
            EmailTo = "to@example.com",
            EmailFrom = "from@example.com",
            EmailSubject = "subject",
            CredentialExpiresAt = null,
            CredentialTriggerableAfter = DateTime.UtcNow
        });
        HashSet<string> allTypes = ["aws", "ssh", "email", "gitlab-cookie"];

        var remaining = StateManager.FilterNotExistingCanaryTypesForName("canary", allTypes);

        // ssh is deployed under a different name so still counts as missing for "canary"
        Assert.Equal(["gitlab-cookie", "ssh"], remaining.Order());
    }

    [Fact]
    public void AddBrowserCookieCredential_StoresHostAsTarget()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);

        var credential = StateManager.AddBrowserCookieCredential("canary", null, HttpCredentials());

        Assert.Equal("gitlab.example.com", credential.Target);
        var loaded = Assert.Single(StateManager.BrowserCookieCredentials);
        Assert.Equal("gitlab.example.com", loaded.Target);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), loaded.ExpiresAt);
    }

    [Fact]
    public void AddUsernamePasswordCredential_PersistsAndIsTypeIsolated()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);

        StateManager.AddUsernamePasswordCredential("canary", TestDataBuilder.BuildLabels(("env", "test")), HttpCredentials());

        var loaded = Assert.Single(StateManager.UsernamePasswordCanaries);
        Assert.Equal("gitlab-username-password", loaded.TypeName);
        Assert.Equal("env", Assert.Single(loaded.Labels!).Name);
        Assert.Empty(StateManager.BrowserCookieCredentials);
    }

    [Fact]
    public void AddEmailCredential_PersistsEmailFields()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        var triggerableAfter = new DateTime(2026, 5, 5, 5, 5, 5, DateTimeKind.Utc);

        StateManager.AddEmailCredential(Constants.EmailCanaryName, null, new GenerateAndSendCanaryEmailResponse
        {
            EmailTo = "to@example.com",
            EmailFrom = "from@example.com",
            EmailSubject = "Backup codes",
            CredentialExpiresAt = null,
            CredentialTriggerableAfter = triggerableAfter
        });

        var loaded = Assert.Single(StateManager.EmailCanaries);
        Assert.Equal("to@example.com", loaded.Target);
        Assert.Equal("from@example.com", loaded.EmailFrom);
        Assert.Equal("Backup codes", loaded.EmailSubject);
        Assert.Equal(triggerableAfter, loaded.CredentialTriggerableAfter);
    }

    [Fact]
    public void Credentials_UnknownTypeInStateFile_IsPreservedAcrossWrites()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        File.WriteAllText(StateFile, """{"credentials":[{"type":"future","name":"f","createdAt":"2025-01-01T00:00:00Z","expiresAt":null,"path":"","futureField":"keep me"}]}""");

        StateManager.AddSshCredential("canary", null, TestDataBuilder.BuildSshCredentials(), "key");

        var credentials = StateManager.Credentials;
        Assert.Equal(2, credentials.Count);
        var unknown = Assert.Single(credentials.OfType<UnknownCredentialData>());
        Assert.Equal("future", unknown.TypeName);
        Assert.Contains("keep me", File.ReadAllText(StateFile));
    }

    [Fact]
    public void Credentials_CorruptStateFile_ThrowsInvalidOperationExceptionNamingFile()
    {
        using var _ = StateManager.SetStateFileForTest(StateFile);
        File.WriteAllText(StateFile, "{ this is not json");

        var exception = Assert.Throws<InvalidOperationException>(() => StateManager.Credentials);

        Assert.Contains(StateFile, exception.Message);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void Credentials_StateFileInMissingDirectory_CreatesDirectory()
    {
        var nested = Path.Combine(TempDirectory, "nested", "dir", "state.json");
        using var _ = StateManager.SetStateFileForTest(nested);

        Assert.Empty(StateManager.Credentials);
        Assert.True(File.Exists(nested));
    }
}
