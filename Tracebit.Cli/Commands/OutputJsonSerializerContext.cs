using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracebit.Cli.Commands;


public class UsernamePasswordOutput
{
    [JsonPropertyName("domain")]
    public required string Domain { get; set; }
    [JsonPropertyName("suggestedName")]
    public required string SuggestedName { get; set; }
    [JsonPropertyName("username")]
    public required string Username { get; set; }
    [JsonPropertyName("password")]
    public required string Password { get; set; }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, OutputJsonSerializerContext.Default.UsernamePasswordOutput);
    }
}


[JsonSourceGenerationOptions(WriteIndented = true, AllowTrailingCommas = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UsernamePasswordOutput))]
internal partial class OutputJsonSerializerContext : JsonSerializerContext;
