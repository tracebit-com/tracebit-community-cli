using System.IdentityModel.Tokens.Jwt;
using System.Net;

using Moq;
using Moq.Protected;

using Tracebit.Cli.API;
using Tracebit.Cli.Commands;
using Tracebit.Cli.Config;

namespace Tracebit.Cli.Tests.API;

public class TracebitApiAuthFailureHandlerTests
{
    [Fact]
    public async Task SendAsync_SuccessfulResponse_PassesThrough()
    {
        var client = CreateClient(HttpStatusCode.OK, new Credentials { Token = "irrelevant" });

        var response = await client.GetAsync("v1/anything");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_Unauthorized_WithMalformedToken_ReportsInvalidToken()
    {
        var client = CreateClient(HttpStatusCode.Unauthorized, new Credentials { Token = "not-a-jwt" });

        var exception = await Assert.ThrowsAsync<MarkupException>(() => client.GetAsync("v1/anything"));

        Assert.Contains("token is invalid", exception.Message);
        Assert.Contains("tracebit auth", exception.Message);
    }

    [Fact]
    public async Task SendAsync_Unauthorized_WithExpiredToken_TellsUserToLogBackIn()
    {
        var token = CreateToken(expires: DateTime.UtcNow.AddHours(-1));
        var client = CreateClient(HttpStatusCode.Unauthorized, new Credentials { Token = token });

        var exception = await Assert.ThrowsAsync<MarkupException>(() => client.GetAsync("v1/anything"));

        Assert.Contains("log back in", exception.Message);
    }

    [Fact]
    public async Task SendAsync_Unauthorized_WithWrongIssuer_ReportsInvalidToken()
    {
        var token = CreateToken(expires: DateTime.UtcNow.AddHours(1), issuer: "evil.example.com");
        var client = CreateClient(HttpStatusCode.Unauthorized, new Credentials { Token = token });

        var exception = await Assert.ThrowsAsync<MarkupException>(() => client.GetAsync("v1/anything"));

        Assert.Contains("token is invalid", exception.Message);
    }

    [Fact]
    public async Task SendAsync_Unauthorized_WithValidToken_ReportsServerProblem()
    {
        var token = CreateToken(expires: DateTime.UtcNow.AddHours(1));
        var client = CreateClient(HttpStatusCode.Unauthorized, new Credentials { Token = token });

        var exception = await Assert.ThrowsAsync<MarkupException>(() => client.GetAsync("v1/anything"));

        Assert.Contains("problem authenticating with the Tracebit server", exception.Message);
    }

    private static string CreateToken(DateTime expires, string? issuer = null)
    {
        var jwt = new JwtSecurityToken(
            issuer: issuer ?? Constants.TracebitHost,
            audience: Constants.TracebitHost,
            notBefore: expires.AddHours(-2),
            expires: expires);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static HttpClient CreateClient(HttpStatusCode statusCode, Credentials credentials)
    {
        var inner = new Mock<HttpMessageHandler>();
        inner.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = statusCode, Content = new StringContent("") });

        var handler = new TracebitApiAuthFailureHandler(credentials) { InnerHandler = inner.Object };
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.test.com/") };
    }
}
