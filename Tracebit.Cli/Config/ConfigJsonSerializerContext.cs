using System.Text.Json.Serialization;

namespace Tracebit.Cli.Config;

[JsonSourceGenerationOptions(WriteIndented = true, AllowTrailingCommas = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Credentials))]
internal partial class ConfigJsonSerializerContext : JsonSerializerContext
{ }
