using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracebit.Cli.Integration.Tests.Helpers;

/// <summary>
/// Base class for API tests using WireMock
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    protected WireMockServer MockServer { get; private set; }
    protected HttpClient HttpClient { get; private set; }

    protected ApiTestBase()
    {
        // Start WireMock server
        MockServer = WireMockServer.Start();

        // Create HttpClient pointing to mock server
        HttpClient = new HttpClient
        {
            BaseAddress = new Uri(MockServer.Url!)
        };
    }

    /// <summary>
    /// Setup a mock endpoint that returns JSON
    /// </summary>
    protected void SetupJsonResponse(string path, object responseBody, int statusCode = 200)
    {
        MockServer
            .Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(responseBody));
    }

    /// <summary>
    /// Setup a mock endpoint that returns an error
    /// </summary>
    protected void SetupErrorResponse(string path, int statusCode, string? errorMessage = null)
    {
        var response = Response.Create()
            .WithStatusCode(statusCode);

        if (errorMessage != null)
        {
            response = response.WithBody(errorMessage);
        }

        MockServer
            .Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(response);
    }

    /// <summary>
    /// Verify that a request was made to the specified path
    /// </summary>
    protected void VerifyRequestMade(string path, int expectedCount = 1)
    {
        var requests = MockServer.FindLogEntries(Request.Create().WithPath(path)).ToList();
        if (requests.Count != expectedCount)
        {
            throw new Exception($"Expected {expectedCount} request(s) to {path}, but found {requests.Count}");
        }
    }

    public virtual void Dispose()
    {
        HttpClient.Dispose();
        MockServer.Stop();
        MockServer.Dispose();
        GC.SuppressFinalize(this);
    }
}
