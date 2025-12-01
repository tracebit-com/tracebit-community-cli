using Tracebit.Cli.API;
using Tracebit.Cli.API.CanaryCredentials;

namespace Tracebit.Cli.Tests.Helpers;

/// <summary>
/// Builder class for creating test data
/// </summary>
public static class TestDataBuilder
{
    public static AwsCanaryCredentials BuildAwsCredentials(
        string? accessKeyId = null,
        string? secretAccessKey = null,
        string? sessionToken = null,
        DateTime? expiration = null,
        Guid? confirmationId = null)
    {
        return new AwsCanaryCredentials
        {
            AwsAccessKeyId = accessKeyId ?? "AKIA" + GenerateRandomString(16),
            AwsSecretAccessKey = secretAccessKey ?? GenerateRandomString(40),
            AwsSessionToken = sessionToken ?? GenerateRandomString(40),
            AwsExpiration = expiration ?? DateTime.UtcNow.AddYears(1),
            AwsConfirmationId = confirmationId ?? Guid.NewGuid()
        };
    }

    public static SshCanaryCredentials BuildSshCredentials(
        string? ip = null,
        string? privateKey = null,
        string? publicKey = null,
        DateTime? expiration = null,
        Guid? confirmationId = null)
    {
        return new SshCanaryCredentials
        {
            SshIp = ip ?? $"192.168.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}",
            SshPrivateKey = privateKey ?? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("fake-private-key")),
            SshPublicKey = publicKey ?? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("fake-public-key")),
            SshExpiration = expiration ?? DateTime.UtcNow.AddYears(1),
            SshConfirmationId = confirmationId ?? Guid.NewGuid()
        };
    }

    public static List<Label> BuildLabels(params (string name, string value)[] labels)
    {
        return labels.Select(l => new Label { Name = l.name, Value = l.value }).ToList();
    }

    public static IssueCredentialsResponse BuildIssueCredentialsResponse(
        AwsCanaryCredentials? aws = null,
        SshCanaryCredentials? ssh = null)
    {
        return new IssueCredentialsResponse
        {
            Aws = aws,
            Ssh = ssh
        };
    }

    private static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, length)
            .Select(_ => chars[Random.Shared.Next(chars.Length)])
            .ToArray());
    }
}
