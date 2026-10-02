using System.Net;

using Moq;
using Moq.Protected;

using Tracebit.Cli.API;

namespace Tracebit.Cli.Tests.API;

public class CliReleaseClientTests
{
    [Fact]
    public async Task GetLatestReleaseAsync_ValidResponse_ReturnsRelease()
    {
        var client = CreateClient(HttpStatusCode.OK, """{"htmlUrl": "https://example.com/release", "tagName": "1.2.3", "name": "Release 1.2.3", "prerelease": false}""");

        var release = await client.GetLatestReleaseAsync(CancellationToken.None);

        Assert.Equal("https://example.com/release", release.HtmlUrl);
        Assert.Equal("1.2.3", release.TagName);
        Assert.Equal("Release 1.2.3", release.Name);
        Assert.False(release.Prerelease);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_ErrorStatus_ThrowsHttpRequestException()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError, "");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestReleaseAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetLatestReleaseAsync_InvalidJson_ThrowsWithJsonMessage()
    {
        var client = CreateClient(HttpStatusCode.OK, "not json");

        var exception = await Assert.ThrowsAsync<Exception>(() => client.GetLatestReleaseAsync(CancellationToken.None));

        Assert.Equal("Response body is not valid JSON.", exception.Message);
    }

    [Fact]
    public async Task GetLatestReleaseAsync_NullJson_ThrowsCouldNotParse()
    {
        var client = CreateClient(HttpStatusCode.OK, "null");

        var exception = await Assert.ThrowsAsync<Exception>(() => client.GetLatestReleaseAsync(CancellationToken.None));

        Assert.Equal("Could not parse response", exception.Message);
    }

    private static CliReleaseClient CreateClient(HttpStatusCode statusCode, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = statusCode, Content = new StringContent(body) });

        return new CliReleaseClient(new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/") });
    }
}
