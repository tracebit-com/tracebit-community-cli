using System.Net;
using System.Text.Json;

using Moq;
using Moq.Protected;

using Tracebit.Cli.API;
using Tracebit.Cli.Tests.Helpers;

namespace Tracebit.Cli.Tests.API;

public class TracebitClientTests
{
    [Fact]
    public async Task IssueCredentials_SuccessfulResponse_ReturnsCredentials()
    {
        var awsCredentials = TestDataBuilder.BuildAwsCredentials();
        var responseObj = TestDataBuilder.BuildIssueCredentialsResponse(aws: awsCredentials);
        var responseJson = JsonSerializer.Serialize(responseObj);

        var mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        var result = await client.IssueCredentials(
            "test-cred",
            new List<string> { "aws" },
            null
        );

        Assert.NotNull(result);
        Assert.NotNull(result.Aws);
        Assert.Equal(awsCredentials.AwsAccessKeyId, result.Aws!.AwsAccessKeyId);
    }

    [Fact]
    public async Task IssueCredentials_QuotaExceeded_ThrowsQuotaExceededException()
    {
        var mockHandler = CreateMockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"error\": \"quota exceeded\"}"
        );

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        await Assert.ThrowsAsync<QuotaExceededException>(async () =>
            await client.IssueCredentials("test-cred", new List<string> { "aws" }, null)
        );
    }

    [Fact]
    public async Task IssueCredentials_InvalidJson_ThrowsException()
    {
        var mockHandler = CreateMockHttpMessageHandler(
            HttpStatusCode.OK,
            "{ invalid json"
        );

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        var exception = await Assert.ThrowsAsync<Exception>(async () =>
            await client.IssueCredentials("test-cred", new List<string> { "aws" }, null)
        );

        Assert.Equal("Response body is not valid JSON.", exception.Message);
    }

    [Fact]
    public async Task IssueCredentials_WithLabels_SendsLabelsInRequest()
    {
        var labels = TestDataBuilder.BuildLabels(("env", "prod"), ("team", "security"));
        var responseObj = TestDataBuilder.BuildIssueCredentialsResponse();
        var responseJson = JsonSerializer.Serialize(responseObj);

        HttpRequestMessage? capturedRequest = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        await client.IssueCredentials("test-cred", new List<string> { "aws" }, labels);

        Assert.NotNull(capturedRequest);
        var requestBody = await capturedRequest!.Content!.ReadAsStringAsync();
        Assert.Contains("\"labels\"", requestBody);
        Assert.Contains("env", requestBody);
        Assert.Contains("prod", requestBody);
    }

    [Fact]
    public async Task ConfirmCredentialsAsync_SuccessfulRequest_Completes()
    {
        var confirmationId = Guid.NewGuid();
        var mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, "");

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        var exception = await Record.ExceptionAsync(async () =>
            await client.ConfirmCredentialsAsync(confirmationId, CancellationToken.None)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task ConfirmCredentialsAsync_FailedRequest_ThrowsException()
    {
        var confirmationId = Guid.NewGuid();
        var mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.BadRequest, "Error");

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.ConfirmCredentialsAsync(confirmationId, CancellationToken.None)
        );
    }

    [Fact]
    public async Task GenerateAndSendCanaryEmailAsync_SuccessfulRequest_ReturnsEmailDetails()
    {
        var responseJson = JsonSerializer.Serialize(new GenerateAndSendCanaryEmailResponse
        {
            EmailFrom = "canary@test.com",
            EmailTo = "target@example.com",
            EmailSubject = "Important Document",
            CredentialExpiresAt = DateTime.UtcNow.AddDays(30),
            CredentialTriggerableAfter = DateTime.UtcNow.AddMinutes(5)
        });

        var mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        var result = await client.GenerateAndSendCanaryEmailAsync(null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("canary@test.com", result.EmailFrom);
        Assert.Equal("target@example.com", result.EmailTo);
    }

    [Fact]
    public async Task GenerateAndSendCanaryEmailAsync_QuotaExceeded_ThrowsQuotaExceededException()
    {
        var mockHandler = CreateMockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"error\": \"quota exceeded\"}"
        );

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        await Assert.ThrowsAsync<QuotaExceededException>(async () =>
            await client.GenerateAndSendCanaryEmailAsync(null, CancellationToken.None)
        );
    }

    [Fact]
    public async Task GenerateAndSendCanaryEmailAsync_TooManyEmailCanaries_ThrowsTooManyEmailCanariesDeployed()
    {
        var mockHandler = CreateMockHttpMessageHandler(
            HttpStatusCode.BadRequest,
            "{\"error\": \"Too many email canaries deployed\"}"
        );

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        await Assert.ThrowsAsync<TooManyEmailCanariesDeployed>(async () =>
            await client.GenerateAndSendCanaryEmailAsync(null, CancellationToken.None)
        );
    }

    [Fact]
    public async Task UpdateStatusAsync_SuccessfulRequest_Completes()
    {
        var mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.NoContent, "");

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        var exception = await Record.ExceptionAsync(async () =>
            await client.UpdateStatusAsync("my-device", CancellationToken.None)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task UpdateStatusAsync_Non204Response_ThrowsException()
    {
        var mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, "");

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        var exception = await Assert.ThrowsAsync<Exception>(async () =>
            await client.UpdateStatusAsync("my-device", CancellationToken.None)
        );

        Assert.Equal("Expected '204 NoContent', Received '200 OK'", exception.Message);
    }

    [Fact]
    public async Task RemoveAsync_SuccessfulRequest_Completes()
    {
        var mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, "");

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        var exception = await Record.ExceptionAsync(async () =>
            await client.RemoveAsync("test-cred", "aws", CancellationToken.None)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task RemoveAsync_FailedRequest_ThrowsException()
    {
        var mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.BadRequest, "Error");

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com")
        };

        var client = new TracebitClient(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.RemoveAsync("test-cred", "aws", CancellationToken.None)
        );
    }

    private Mock<HttpMessageHandler> CreateMockHttpMessageHandler(
        HttpStatusCode statusCode,
        string responseContent)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        return mockHandler;
    }
}
