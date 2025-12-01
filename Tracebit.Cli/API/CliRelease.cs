using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracebit.Cli.API;

public class GetLatestCliReleaseResponse
{
    [JsonPropertyName("htmlUrl")]
    public required string HtmlUrl { get; set; }

    [JsonPropertyName("tagName")]
    public required string TagName { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("prerelease")]
    public required bool Prerelease { get; set; }
}

public class CliReleaseClient(HttpClient httpClient) : UnauthenticatedApiClient(httpClient)
{
    public async Task<GetLatestCliReleaseResponse> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var response = await HttpClient.GetAsync("_internal/v1/cli/latest-release", cancellationToken);

        response.EnsureSuccessStatusCode();

        GetLatestCliReleaseResponse? result;
        try
        {
            result = JsonSerializer.Deserialize(
                await response.Content.ReadAsStringAsync(cancellationToken),
                ApiJsonSerializerContext.Default.GetLatestCliReleaseResponse
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
