using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;

using Tracebit.Cli.Commands;
using Tracebit.Cli.Config;

namespace Tracebit.Cli.API;

/// <summary>
/// Base client for authenticated Tracebit API calls
/// </summary>
public class AuthenticatedApiClient(HttpClient httpClient)
{
    protected HttpClient HttpClient { get; } = httpClient;

    public void SetAuthorizationHeader(string token)
    {
        HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }
}

/// <summary>
/// Base client for unauthenticated Tracebit API calls
/// </summary>
public class UnauthenticatedApiClient(HttpClient httpClient)
{
    protected HttpClient HttpClient { get; } = httpClient;
}

public class TracebitApiAuthFailureHandler(Credentials credentials) : DelegatingHandler
{
    private static readonly FormattableString CliLoginInvocation = $"[purple]tracebit auth[/]";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var credentialsResult = await Auth.TokenHandler.ValidateTokenAsync(credentials.Token, Auth.TokenValidationParameters);
            if (!credentialsResult.IsValid)
                throw new MarkupException($"Your Tracebit API token is invalid. Run {CliLoginInvocation} to log back in", credentialsResult.Exception);

            var expiryClaim = credentialsResult.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
            if (expiryClaim is not null && int.TryParse(expiryClaim, out var expiryUnixTime) && DateTimeOffset.FromUnixTimeSeconds(expiryUnixTime) < DateTime.UtcNow)
            {
                throw new MarkupException($"Your Tracebit API token has expired. Run {CliLoginInvocation} to log back in");
            }

            throw new MarkupException($"There was a problem authenticating with the Tracebit server. Try again later or run {CliLoginInvocation} to log in with a new API token");
        }

        return response;
    }
}
