using Tracebit.Cli.Integration.Tests.Helpers;
using Tracebit.Cli.State;
using Tracebit.Cli.Tests.Helpers;

namespace Tracebit.Cli.Integration.Tests.FileSystem;

/// <summary>
/// Integration tests for StateManager file operations
/// Note: These tests use isolated state files per test via SetStateFileForTest.
/// Tests can run in parallel safely.
/// </summary>
public class StateManagerFileTests : BaseIntegrationTest
{
    private string GetTestStateFilePath() => Path.Combine(TempDirectory, "state.json");

    [Fact]
    public void Credentials_EmptyFile_ReturnsEmptyState()
    {
        using var _ = StateManager.SetStateFileForTest(GetTestStateFilePath());
        Assert.NotNull(StateManager.Credentials);
    }

    [Fact]
    public void SaveAsync_ThenLoad_PersistsCredentials()
    {
        using var _ = StateManager.SetStateFileForTest(GetTestStateFilePath());

        var credential = StateManager.AddAwsCredential(
            "integration-test-cred",
            null,
            TestDataBuilder.BuildAwsCredentials(),
            "default",
            "us-east-1"
        );

        var credentials = StateManager.Credentials;
        Assert.Contains(credentials, c => c.Name == "integration-test-cred");
        var loaded = credentials.First(c => c.Name == "integration-test-cred");
        Assert.Equal("aws", loaded.TypeName);

        StateManager.RemoveCredential(credential);
    }

    [Fact]
    public void AddAwsCredential_SaveAndLoad_PreservesAllFields()
    {
        using var _ = StateManager.SetStateFileForTest(GetTestStateFilePath());

        var awsCredentials = TestDataBuilder.BuildAwsCredentials();
        var labels = new List<Tracebit.Cli.API.Label>
        {
            new() { Name = "environment", Value = "test" },
            new() { Name = "owner", Value = "integration-tests" }
        };

        var credential = StateManager.AddAwsCredential(
            "test-aws-integration",
            labels,
            awsCredentials,
            "default",
            "us-west-2"
        );

        var awsCreds = StateManager.AwsCredentials;
        Assert.Contains(awsCreds, c => c.Name == "test-aws-integration");

        var cred = awsCreds.First(c => c.Name == "test-aws-integration");
        Assert.NotNull(cred.Labels);
        Assert.Equal(2, cred.Labels.Count);
        Assert.Contains(cred.Labels, l => l.Name == "environment" && l.Value == "test");

        StateManager.RemoveCredential(credential);
    }

    [Fact]
    public void RemoveCredential_SaveAndLoad_CredentialIsRemoved()
    {
        using var _ = StateManager.SetStateFileForTest(GetTestStateFilePath());

        var credential = StateManager.AddAwsCredential(
            "to-be-removed",
            null,
            TestDataBuilder.BuildAwsCredentials(),
            "default",
            "us-east-1"
        );

        StateManager.RemoveCredential(credential);

        Assert.DoesNotContain(StateManager.Credentials, c => c.Name == "to-be-removed");
    }

    [Fact]
    public void MultipleCredentials_SaveAndLoad_AllPersisted()
    {
        using var _ = StateManager.SetStateFileForTest(GetTestStateFilePath());

        var awsCred1 = StateManager.AddAwsCredential("aws-cred-1", null, TestDataBuilder.BuildAwsCredentials(), "default", "us-east-1");
        var awsCred2 = StateManager.AddAwsCredential("aws-cred-2", null, TestDataBuilder.BuildAwsCredentials(), "prod", "eu-west-1");
        var sshCred1 = StateManager.AddSshCredential("ssh-cred-1", null, TestDataBuilder.BuildSshCredentials(), "test-key.pem");

        Assert.Contains(StateManager.AwsCredentials, c => c.Name == "aws-cred-1");
        Assert.Contains(StateManager.AwsCredentials, c => c.Name == "aws-cred-2");
        Assert.Contains(StateManager.SshCredentials, c => c.Name == "ssh-cred-1");

        StateManager.RemoveCredential(awsCred1);
        StateManager.RemoveCredential(awsCred2);
        StateManager.RemoveCredential(sshCred1);
    }
}


