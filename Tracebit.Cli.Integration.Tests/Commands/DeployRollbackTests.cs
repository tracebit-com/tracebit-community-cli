using Tracebit.Cli.API;
using Tracebit.Cli.Deploy;
using Tracebit.Cli.State;
using Tracebit.Cli.Tests.Helpers;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

using DeployCommand = Tracebit.Cli.Commands.Deploy;

namespace Tracebit.Cli.Integration.Tests.Commands;

/// <summary>
/// Deployment is local write, then state, then API confirmation. These tests cover what happens when confirmation fails.
/// </summary>
[Collection(CommandCollection.Name)]
public class DeployRollbackTests(CommandFixture fixture) : CommandTestBase(fixture)
{
    private const string ConfirmPath = "/v1/credentials/confirm-credentials";
    private readonly CommandFixture _fixture = fixture;

    private TracebitClient Client() => new(new HttpClient { BaseAddress = new Uri(_fixture.Api.Url!) });

    private void SetupConfirmResponse(int statusCode) => _fixture.Api
        .Given(Request.Create().WithPath(ConfirmPath).UsingPost())
        .RespondWith(Response.Create().WithStatusCode(statusCode));

    [Fact]
    public async Task DeployAws_FreshDeployConfirmFails_RollsBackFilesAndState()
    {
        using var _ = Isolate();
        SetupConfirmResponse(500);
        var credentials = TestDataBuilder.BuildAwsCredentials();

        await Assert.ThrowsAsync<HttpRequestException>(() => DeployCommand.DeployAwsAsync(credentials, "canary", "eu-west-1", AwsProfileOwnership.None,
            () => StateManager.AddAwsCredential("canary", null, credentials, "canary", "eu-west-1"), Client(), verbose: false, CancellationToken.None));

        Assert.Empty(StateManager.Credentials);
        Assert.DoesNotContain("[canary]", await File.ReadAllTextAsync(Path.Combine(TempDirectory, ".aws", "credentials")));
        Assert.DoesNotContain("[profile canary]", await File.ReadAllTextAsync(Path.Combine(TempDirectory, ".aws", "config")));
    }

    [Fact]
    public async Task DeploySsh_FreshDeployConfirmFails_RollsBackFilesAndState()
    {
        using var _ = Isolate();
        SetupConfirmResponse(500);
        var credentials = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3");

        await Assert.ThrowsAsync<HttpRequestException>(() => DeployCommand.DeploySshAsync(credentials, "canary-key", SshKeyOwnership.None,
            () => StateManager.AddSshCredential("canary", null, credentials, "canary-key"), Client(), verbose: false, CancellationToken.None));

        Assert.Empty(StateManager.Credentials);
        Assert.False(File.Exists(Path.Combine(TempDirectory, ".ssh", "canary-key")));
        Assert.False(File.Exists(Path.Combine(TempDirectory, ".ssh", "canary-key.pub")));
        Assert.DoesNotContain("Host 10.1.2.3", await File.ReadAllTextAsync(Path.Combine(TempDirectory, ".ssh", "config")));
    }

    [Fact]
    public async Task DeploySsh_RefreshConfirmFails_KeepsNewKeyAndStateConsistent()
    {
        using var _ = Isolate();
        SetupConfirmResponse(200);
        var first = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3");
        var deployed = await DeployCommand.DeploySshAsync(first, "canary-key", SshKeyOwnership.None,
            () => StateManager.AddSshCredential("canary", null, first, "canary-key"), Client(), verbose: false, CancellationToken.None);

        SetupConfirmResponse(500);
        var rotated = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", publicKey: Convert.ToBase64String("ROTATED"u8.ToArray()));
        var ownership = SshKeyOwnership.Owned(deployed.Path, deployed.SshPrivateKeySha256);

        await Assert.ThrowsAsync<HttpRequestException>(() => DeployCommand.DeploySshAsync(rotated, "canary-key", ownership,
            () => StateManager.AddSshCredential("canary", null, rotated, "canary-key"), Client(), verbose: false, CancellationToken.None));

        // Nothing to roll back to, so the new key stays and state describes it, ready for the next refresh
        Assert.Equal("ROTATED", await File.ReadAllTextAsync(Path.Combine(TempDirectory, ".ssh", "canary-key.pub")));
        Assert.Equal(SshKeyOwnership.FingerprintBase64(rotated.SshPrivateKey), StateManager.SshCredentials.Single().SshPrivateKeySha256);
    }

    [Fact]
    public async Task DeployAws_ConfirmSucceeds_StateRecordedBeforeConfirmation()
    {
        using var _ = Isolate();
        var stateRecorded = false;
        var stateRecordedWhenConfirmed = false;
        _fixture.Api
            .Given(Request.Create().WithPath(ConfirmPath).UsingPost())
            .RespondWith(Response.Create().WithCallback(_ =>
            {
                stateRecordedWhenConfirmed = stateRecorded;
                return new WireMock.ResponseMessage { StatusCode = 200 };
            }));
        var credentials = TestDataBuilder.BuildAwsCredentials();

        await DeployCommand.DeployAwsAsync(credentials, "canary", "eu-west-1", AwsProfileOwnership.None,
            () =>
            {
                stateRecorded = true;
                return StateManager.AddAwsCredential("canary", null, credentials, "canary", "eu-west-1");
            }, Client(), verbose: false, CancellationToken.None);

        Assert.True(stateRecordedWhenConfirmed, "state should already hold the credential when the API is asked to confirm it");
        Assert.Single(StateManager.AwsCredentials);
    }
}
