using System.Text.Json;

using Tracebit.Cli.State;

namespace Tracebit.Cli.Tests.State;

public class CredentialDataTests
{
    [Fact]
    public void Markup_ContainsNameAndTitleCasedType()
    {
        var credential = new GitlabUsernamePasswordCredentialData { Name = "my-canary", CreatedAt = DateTime.Now, ExpiresAt = null, Path = "" };

        var markup = credential.Markup().ToString();

        Assert.Contains("my-canary", markup);
        Assert.Contains("(Username/Password)", markup);
    }

    [Fact]
    public void Markup_EscapesMarkupCharactersInName()
    {
        var credential = new SshCredentialData { Name = "[red]evil[/]", CreatedAt = DateTime.Now, ExpiresAt = null, Path = "" };

        var markup = credential.Markup().ToString();

        Assert.Contains("[[red]]evil[[/]]", markup);
    }

    [Theory]
    [InlineData("aws", typeof(AwsCredentialData))]
    [InlineData("ssh", typeof(SshCredentialData))]
    [InlineData("email", typeof(EmailCredentialData))]
    [InlineData("gitlab-cookie", typeof(GitlabCookieCredentialData))]
    [InlineData("gitlab-username-password", typeof(GitlabUsernamePasswordCredentialData))]
    [InlineData("something-new", typeof(UnknownCredentialData))]
    public void Deserialize_TypeDiscriminator_ProducesExpectedType(string type, Type expected)
    {
        var json = $$"""{"type": "{{type}}", "name": "n", "createdAt": "2025-12-25T03:00:00Z", "expiresAt": null, "path": ""}""";

        var credential = JsonSerializer.Deserialize(json, StateJsonSerializerContext.Default.CredentialData);

        Assert.IsType(expected, credential);
        Assert.Equal(type, credential.TypeName);
    }

    [Fact]
    public void RoundTrip_AwsCredential_PreservesTypeSpecificFields()
    {
        var credential = new AwsCredentialData
        {
            Name = "aws-canary",
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 2, 2, 3, 4, 5, DateTimeKind.Utc),
            Path = "/home/user/.aws/credentials",
            AwsProfile = "canary",
            AwsRegion = "eu-west-1",
            Labels = [new() { Name = "env", Value = "prod" }],
            Target = "target"
        };

        var json = JsonSerializer.Serialize<CredentialData>(credential, StateJsonSerializerContext.Default.CredentialData);
        var result = Assert.IsType<AwsCredentialData>(JsonSerializer.Deserialize(json, StateJsonSerializerContext.Default.CredentialData));

        Assert.Equal(credential.Name, result.Name);
        Assert.Equal(credential.CreatedAt, result.CreatedAt);
        Assert.Equal(credential.ExpiresAt, result.ExpiresAt);
        Assert.Equal(credential.Path, result.Path);
        Assert.Equal("canary", result.AwsProfile);
        Assert.Equal("eu-west-1", result.AwsRegion);
        Assert.Equal("target", result.Target);
        Assert.NotNull(result.Labels);
        Assert.Single(result.Labels);
        Assert.Equal("env", result.Labels[0].Name);
    }

    [Fact]
    public void RoundTrip_EmailCredential_PreservesEmailFields()
    {
        var credential = new EmailCredentialData
        {
            Name = "Backup Codes Email",
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ExpiresAt = null,
            Path = "",
            EmailFrom = "from@example.com",
            EmailSubject = "Your backup codes",
            CredentialTriggerableAfter = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize<CredentialData>(credential, StateJsonSerializerContext.Default.CredentialData);
        var result = Assert.IsType<EmailCredentialData>(JsonSerializer.Deserialize(json, StateJsonSerializerContext.Default.CredentialData));

        Assert.Equal("from@example.com", result.EmailFrom);
        Assert.Equal("Your backup codes", result.EmailSubject);
        Assert.Equal(credential.CredentialTriggerableAfter, result.CredentialTriggerableAfter);
    }

    [Fact]
    public void RoundTrip_UnknownCredential_PreservesTypeAndExtraFields()
    {
        var json = """{"type": "future-type", "name": "n", "createdAt": "2025-12-25T03:00:00Z", "expiresAt": null, "path": "", "futureField": {"nested": 1}}""";

        var credential = JsonSerializer.Deserialize(json, StateJsonSerializerContext.Default.CredentialData);
        var reserialized = JsonSerializer.Serialize(credential!, StateJsonSerializerContext.Default.CredentialData);

        using var doc = JsonDocument.Parse(reserialized);
        Assert.Equal("future-type", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("futureField").GetProperty("nested").GetInt32());
    }

    [Fact]
    public void Deserialize_MissingType_Throws()
    {
        var json = """{"name": "n", "createdAt": "2025-12-25T03:00:00Z", "expiresAt": null, "path": ""}""";

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, StateJsonSerializerContext.Default.CredentialData));
    }
}
