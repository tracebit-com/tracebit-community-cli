using System.Text.Json.Serialization;

namespace Tracebit.Cli.API.CanaryCredentials;

public class SshCanaryCredentials
{
    [JsonPropertyName("sshConfirmationId")]
    public required Guid SshConfirmationId { get; set; }
    [JsonPropertyName("sshIp")]
    public required string SshIp { get; set; }
    [JsonPropertyName("sshPrivateKey")]
    public required string SshPrivateKey { get; set; }
    [JsonPropertyName("sshPublicKey")]
    public required string SshPublicKey { get; set; }
    [JsonPropertyName("sshExpiration")]
    public required DateTime SshExpiration { get; set; }
}
