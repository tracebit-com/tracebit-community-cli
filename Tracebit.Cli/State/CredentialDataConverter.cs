using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracebit.Cli.State;

public class CredentialDataConverter : JsonConverter<CredentialData>
{
    public override CredentialData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProperty))
        {
            throw new JsonException("Missing 'type' property in CredentialData");
        }

        var typeDiscriminator = typeProperty.GetString();
        var rawJson = root.GetRawText();

        return typeDiscriminator switch
        {
            AwsCredentialData.TypeDiscriminator => JsonSerializer.Deserialize(rawJson, StateJsonSerializerContext.Default.AwsCredentialData),
            SshCredentialData.TypeDiscriminator => JsonSerializer.Deserialize(rawJson, StateJsonSerializerContext.Default.SshCredentialData),
            EmailCredentialData.TypeDiscriminator => JsonSerializer.Deserialize(rawJson, StateJsonSerializerContext.Default.EmailCredentialData),
            GitlabCookieCredentialData.TypeDiscriminator => JsonSerializer.Deserialize(rawJson, StateJsonSerializerContext.Default.GitlabCookieCredentialData),
            GitlabUsernamePasswordCredentialData.TypeDiscriminator => JsonSerializer.Deserialize(rawJson, StateJsonSerializerContext.Default.GitlabUsernamePasswordCredentialData),
            _ => JsonSerializer.Deserialize(rawJson, StateJsonSerializerContext.Default.UnknownCredentialData)
        };
    }

    public override void Write(Utf8JsonWriter writer, CredentialData value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case AwsCredentialData aws:
                JsonSerializer.Serialize(writer, aws, StateJsonSerializerContext.Default.AwsCredentialData);
                break;
            case SshCredentialData ssh:
                JsonSerializer.Serialize(writer, ssh, StateJsonSerializerContext.Default.SshCredentialData);
                break;
            case EmailCredentialData email:
                JsonSerializer.Serialize(writer, email, StateJsonSerializerContext.Default.EmailCredentialData);
                break;
            case GitlabCookieCredentialData cookie:
                JsonSerializer.Serialize(writer, cookie, StateJsonSerializerContext.Default.GitlabCookieCredentialData);
                break;
            case GitlabUsernamePasswordCredentialData userPass:
                JsonSerializer.Serialize(writer, userPass, StateJsonSerializerContext.Default.GitlabUsernamePasswordCredentialData);
                break;
            case UnknownCredentialData unknown:
                JsonSerializer.Serialize(writer, unknown, StateJsonSerializerContext.Default.UnknownCredentialData);
                break;
            default:
                throw new NotImplementedException($"Serialization of credential type {value.GetType().Name} is not implemented");
        }
    }
}
