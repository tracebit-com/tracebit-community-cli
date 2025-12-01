using Tracebit.Cli.API;
using Tracebit.Cli.Integration.Tests.Helpers;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tracebit.Cli.Integration.Tests.API;

public class TracebitClientIntegrationTests : ApiTestBase
{
    [Fact]
    public async Task IssueCredentials_ValidAwsRequest_ReturnsAwsCredentials()
    {
        var responseBody = @"{
            ""aws"": {
                ""awsAccessKeyId"": ""AKIAIOSFODNN7EXAMPLE"",
                ""awsSecretAccessKey"": ""wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"",
                ""awsSessionToken"": ""FQoDYXdzEJr...<truncated>"",
                ""awsExpiration"": ""2025-12-31T23:59:59Z"",
                ""awsConfirmationId"": ""550e8400-e29b-41d4-a716-446655440000""
            }
        }";

        MockServer
            .Given(Request.Create()
                .WithPath("/v1/credentials/issue-credentials")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));

        var client = new TracebitClient(HttpClient);

        var result = await client.IssueCredentials(
            "test-credential",
            new List<string> { "aws" },
            null
        );

        Assert.NotNull(result);
        Assert.NotNull(result.Aws);
        Assert.Equal("AKIAIOSFODNN7EXAMPLE", result.Aws!.AwsAccessKeyId);
        Assert.Equal("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", result.Aws.AwsSecretAccessKey);
    }

    [Fact]
    public async Task IssueCredentials_QuotaExceeded_ThrowsQuotaExceededException()
    {
        MockServer
            .Given(Request.Create()
                .WithPath("/v1/credentials/issue-credentials")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(400)
                .WithHeader("Content-Type", "application/json")
                .WithBody(@"{""error"": ""quota exceeded""}"));

        var client = new TracebitClient(HttpClient);

        await Assert.ThrowsAsync<QuotaExceededException>(async () =>
            await client.IssueCredentials("test-credential", new List<string> { "aws" }, null)
        );
    }

    [Fact]
    public async Task IssueCredentials_WithLabels_SendsLabelsInRequest()
    {
        var responseBody = @"{
            ""ssh"": {
                ""sshPrivateKey"": ""-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----"",
                ""sshPublicKey"": ""ssh-rsa AAAAB3NzaC1..."",
                ""sshExpiration"": ""2025-12-31T23:59:59Z"",
                ""sshConfirmationId"": ""660e8400-e29b-41d4-a716-446655440001"",
                ""sshIp"": ""203.0.113.1""
            }
        }";

        MockServer
            .Given(Request.Create()
                .WithPath("/v1/credentials/issue-credentials")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));

        var client = new TracebitClient(HttpClient);

        var labels = new List<Label>
        {
            new() { Name = "environment", Value = "production" },
            new() { Name = "team", Value = "security" }
        };

        var result = await client.IssueCredentials(
            "test-credential",
            new List<string> { "ssh" },
            labels
        );

        Assert.NotNull(result);
        Assert.NotNull(result.Ssh);

        var requests = MockServer.LogEntries.ToList();
        Assert.Single(requests);
        Assert.Contains("environment", requests[0].RequestMessage.Body);
        Assert.Contains("production", requests[0].RequestMessage.Body);
    }

    [Fact]
    public async Task IssueCredentials_MultipleCredentialTypes_ReturnsAllTypes()
    {
        var responseBody = @"{
            ""aws"": {
                ""awsAccessKeyId"": ""AKIAIOSFODNN7EXAMPLE"",
                ""awsSecretAccessKey"": ""wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"",
                ""awsSessionToken"": ""FQoDYXdzEJr...<truncated>"",
                ""awsExpiration"": ""2025-12-31T23:59:59Z"",
                ""awsConfirmationId"": ""550e8400-e29b-41d4-a716-446655440000""
            },
            ""ssh"": {
                ""sshPrivateKey"": ""-----BEGIN OPENSSH PRIVATE KEY-----\ntest\n-----END OPENSSH PRIVATE KEY-----"",
                ""sshPublicKey"": ""ssh-rsa AAAAB3NzaC1..."",
                ""sshExpiration"": ""2025-12-31T23:59:59Z"",
                ""sshConfirmationId"": ""660e8400-e29b-41d4-a716-446655440001"",
                ""sshIp"": ""203.0.113.1""
            }
        }";

        MockServer
            .Given(Request.Create()
                .WithPath("/v1/credentials/issue-credentials")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));

        var client = new TracebitClient(HttpClient);

        var result = await client.IssueCredentials(
            "test-multi",
            new List<string> { "aws", "ssh" },
            null
        );

        Assert.NotNull(result);
        Assert.NotNull(result.Aws);
        Assert.NotNull(result.Ssh);
        Assert.Contains("BEGIN OPENSSH PRIVATE KEY", result.Ssh!.SshPrivateKey);
        Assert.Equal("203.0.113.1", result.Ssh.SshIp);
    }

    [Fact]
    public async Task ConfirmCredentialsAsync_SuccessfulRequest_Completes()
    {
        var confirmationId = Guid.NewGuid();

        MockServer
            .Given(Request.Create()
                .WithPath("/v1/credentials/confirm-credentials")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200));

        var client = new TracebitClient(HttpClient);

        var exception = await Record.ExceptionAsync(async () =>
            await client.ConfirmCredentialsAsync(confirmationId, CancellationToken.None)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task GenerateAndSendCanaryEmailAsync_SuccessfulRequest_ReturnsEmailDetails()
    {
        var responseBody = @"{
            ""emailFrom"": ""canary@test.com"",
            ""emailTo"": ""target@example.com"",
            ""emailSubject"": ""Important Document"",
            ""credentialExpiresAt"": ""2025-12-31T23:59:59Z"",
            ""credentialTriggerableAfter"": ""2025-01-01T00:05:00Z""
        }";

        MockServer
            .Given(Request.Create()
                .WithPath("/_internal/v1/cli/email")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));

        var client = new TracebitClient(HttpClient);

        var result = await client.GenerateAndSendCanaryEmailAsync(null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("canary@test.com", result.EmailFrom);
        Assert.Equal("target@example.com", result.EmailTo);
        Assert.Equal("Important Document", result.EmailSubject);
    }

    [Fact]
    public async Task GenerateAndSendCanaryEmailAsync_TooManyEmailCanaries_ThrowsException()
    {
        MockServer
            .Given(Request.Create()
                .WithPath("/_internal/v1/cli/email")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(400)
                .WithBody(@"{""error"": ""Too many email canaries deployed""}"));

        var client = new TracebitClient(HttpClient);

        await Assert.ThrowsAsync<TooManyEmailCanariesDeployed>(async () =>
            await client.GenerateAndSendCanaryEmailAsync(null, CancellationToken.None)
        );
    }

    [Fact]
    public async Task UpdateStatusAsync_SuccessfulRequest_Completes()
    {
        MockServer
            .Given(Request.Create()
                .WithPath("/_internal/v1/cli/status")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(204));

        var client = new TracebitClient(HttpClient);

        var exception = await Record.ExceptionAsync(async () =>
            await client.UpdateStatusAsync("my-device", CancellationToken.None)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task RemoveAsync_SuccessfulRequest_Completes()
    {
        MockServer
            .Given(Request.Create()
                .WithPath("/_internal/v1/cli/remove")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200));

        var client = new TracebitClient(HttpClient);

        var exception = await Record.ExceptionAsync(async () =>
            await client.RemoveAsync("test-cred", "aws", CancellationToken.None)
        );

        Assert.Null(exception);
    }
}
