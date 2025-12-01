using System.Text.Json.Serialization;

namespace Tracebit.Cli.State;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    AllowTrailingCommas = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(CredentialDataConverter)])]
[JsonSerializable(typeof(State))]
[JsonSerializable(typeof(CredentialData))]
// Make sure to add new credential types to CredentialDataConverter
[JsonSerializable(typeof(AwsCredentialData))]
[JsonSerializable(typeof(SshCredentialData))]
[JsonSerializable(typeof(EmailCredentialData))]
[JsonSerializable(typeof(GitlabCookieCredentialData))]
[JsonSerializable(typeof(GitlabUsernamePasswordCredentialData))]
[JsonSerializable(typeof(UnknownCredentialData))]
internal partial class StateJsonSerializerContext : JsonSerializerContext
{ }

