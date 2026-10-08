using System.Text;
using System.Text.Json;

namespace Tracebit.Cli.E2E.Tests.Helpers;

/// <summary>
/// Builds JWTs shaped like Tracebit API tokens. The CLI checks issuer, audience and expiry locally but leaves the
/// signature to the server, so these pass local checks and are always rejected by the API.
/// </summary>
public static class TestTokens
{
    public const string StagingHost = "community.stage.tracebit.com";
    public const string ProductionHost = "community.tracebit.com";

    public static string Create(DateTimeOffset expiresAt, string issuer = StagingHost, string? audience = null)
    {
        var header = Base64Url(JsonSerializer.Serialize(new Dictionary<string, string> { ["alg"] = "ES256", ["typ"] = "JWT" }));
        var payload = Base64Url(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["scope"] = "canary-credentials:all:create",
            ["iss"] = issuer,
            ["aud"] = audience ?? issuer,
            ["nbf"] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(),
            ["iat"] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
        }));
        var signature = Base64Url("not-a-real-signature");
        return $"{header}.{payload}.{signature}";
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
