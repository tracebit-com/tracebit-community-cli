using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracebit.Cli.API.CanaryCredentials;

public class HttpCanaryCredentials
{
    [JsonPropertyName("confirmationId")]
    public required Guid ConfirmationId { get; set; }
    [JsonPropertyName("browserDeploymentId")]
    public required Guid BrowserDeploymentId { get; set; }
    [JsonPropertyName("hostNames")]
    public required List<string> HostNames { get; set; }
    [JsonPropertyName("expiresAt")]
    public required DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("credentials")]
    public required JsonElement Credentials { get; set; }

    public Uri DeployUrl()
    {
        return new UriBuilder
        {
            Scheme = "https",
            Host = HostNames[0],
            Path = "/_browserDeploy",
            Query = $"?id={BrowserDeploymentId}"
        }.Uri;
    }
}

public class UsernamePasswordData
{
    [JsonPropertyName("username")]
    public required string Username { get; set; }
    [JsonPropertyName("password")]
    public required string Password { get; set; }
}

