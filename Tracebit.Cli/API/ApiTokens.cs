using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracebit.Cli.API;

public class ApiTokens
{
    private const string ExchangeCliTokenPath = "v1/api-tokens/cli";
    public static async Task<ExchangeCliTokenResponse> ExchangeCliTokenAsync(string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ExchangeCliTokenPath);

        var httpClient = new HttpClient
        {
            BaseAddress = Constants.TracebitApiUrl
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        ExchangeCliTokenResponse? result;
        try
        {
            result = await JsonSerializer.DeserializeAsync(
                response.Content.ReadAsStream(cancellationToken),
                ApiJsonSerializerContext.Default.ExchangeCliTokenResponse,
                cancellationToken
            );
        }
        catch (JsonException e)
        {
            throw new Exception("Response body is not valid JSON.", e);
        }

        if (result is null)
        {
            throw new Exception("Could not parse response");
        }

        return result;
    }
}

public class ExchangeCliTokenResponse
{
    [JsonPropertyName("token")]
    public required string Token { get; set; }

    [JsonPropertyName("expiry")]
    public required DateTime Expiry { get; set; }

    [JsonPropertyName("scopes")]
    public required List<string> Scopes { get; set; }
}
