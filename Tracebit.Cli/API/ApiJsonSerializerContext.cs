using System.Text.Json.Serialization;

using Tracebit.Cli.API.CanaryCredentials;

namespace Tracebit.Cli.API;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    AllowTrailingCommas = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(ConfirmCredentialsRequest))]
[JsonSerializable(typeof(IssueCredentialsRequest))]
[JsonSerializable(typeof(IssueCredentialsResponse))]
[JsonSerializable(typeof(GenerateCredentialsMetadataResponse))]
[JsonSerializable(typeof(ExchangeCliTokenResponse))]
[JsonSerializable(typeof(GetLatestCliReleaseResponse))]
[JsonSerializable(typeof(GenerateAndSendCanaryEmailRequest))]
[JsonSerializable(typeof(GenerateAndSendCanaryEmailResponse))]
[JsonSerializable(typeof(ExpireCredentialsRequest))]
[JsonSerializable(typeof(UsernamePasswordData))]
[JsonSerializable(typeof(UpdateStatusRequest))]
internal partial class ApiJsonSerializerContext : JsonSerializerContext;
