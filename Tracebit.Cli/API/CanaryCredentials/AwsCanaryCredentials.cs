using System.Text.Json.Serialization;

namespace Tracebit.Cli.API.CanaryCredentials;

public class AwsCanaryCredentials
{
    [JsonPropertyName("awsConfirmationId")]
    public required Guid AwsConfirmationId { get; set; }
    [JsonPropertyName("awsAccessKeyId")]
    public required string AwsAccessKeyId { get; set; }
    [JsonPropertyName("awsExpiration")]
    public required DateTime AwsExpiration { get; set; }
    [JsonPropertyName("awsSecretAccessKey")]
    public required string AwsSecretAccessKey { get; set; }
    [JsonPropertyName("awsSessionToken")]
    public required string AwsSessionToken { get; set; }
}
