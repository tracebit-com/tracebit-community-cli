using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Spectre.Console;

using Labels = System.Collections.Generic.List<Tracebit.Cli.API.Label>;

namespace Tracebit.Cli.State;

public interface IPrettyNamedType
{
    static abstract string PrettyTypeName { get; }
}

[JsonConverter(typeof(CredentialDataConverter))]
public abstract class CredentialData
{
    [JsonPropertyName("type")]
    [ShowInTable(false)]
    public abstract string TypeName { get; init; }

    [ShowInTable("Type")]
    [JsonIgnore]
    public abstract string PrettyTypeNameInstance { get; }

    public required string Name { get; set; }
    // NOTE: The timestamps can currently diverge from those values in the Database
    public required DateTime CreatedAt { get; set; }
    public required DateTime? ExpiresAt { get; set; }
    public required string Path { get; set; }
    public Labels? Labels { get; set; }
    public string? Target { get; set; }

    [ShowInTable(false)]
    public DateTime? CredentialTriggerableAfter { get; set; }

    public FormattableString Markup() => $"[silver]{Name.EscapeMarkup()}[/] [darkgoldenrod]({CultureInfo.InvariantCulture.TextInfo.ToTitleCase(PrettyTypeNameInstance).EscapeMarkup()})[/]";

    // For forward-compatibility, retain unknown fields
    [ShowInTable(false)]
    [JsonExtensionData]
    public IDictionary<string, JsonElement> ExtraFields { get; set; } = new Dictionary<string, JsonElement>();
}

public class AwsCredentialData : CredentialData, IPrettyNamedType
{
    public const string TypeDiscriminator = "aws";
    [JsonPropertyName("type")]
    public override string TypeName { get; init; } = TypeDiscriminator;
    public static string PrettyTypeName => "AWS";
    public override string PrettyTypeNameInstance => PrettyTypeName;

    public string? AwsProfile { get; set; }
    public string? AwsRegion { get; set; }
}

public class SshCredentialData : CredentialData, IPrettyNamedType
{
    public const string TypeDiscriminator = "ssh";
    [JsonPropertyName("type")]
    public override string TypeName { get; init; } = TypeDiscriminator;
    public static string PrettyTypeName => "SSH";
    public override string PrettyTypeNameInstance => PrettyTypeName;
}

public abstract class HttpCredentialData : CredentialData { }

public class EmailCredentialData : HttpCredentialData, IPrettyNamedType
{
    public const string TypeDiscriminator = "email";
    [JsonPropertyName("type")]
    public override string TypeName { get; init; } = TypeDiscriminator;
    public static string PrettyTypeName => "email";
    public override string PrettyTypeNameInstance => PrettyTypeName;

    public string? EmailFrom { get; set; }
    public string? EmailSubject { get; set; }
}

public class GitlabCookieCredentialData : HttpCredentialData, IPrettyNamedType
{
    public const string TypeDiscriminator = "gitlab-cookie";
    [JsonPropertyName("type")]
    public override string TypeName { get; init; } = TypeDiscriminator;
    public static string PrettyTypeName => "cookie";
    public override string PrettyTypeNameInstance => PrettyTypeName;
}

public class GitlabUsernamePasswordCredentialData : HttpCredentialData, IPrettyNamedType
{
    public const string TypeDiscriminator = "gitlab-username-password";
    [JsonPropertyName("type")]
    public override string TypeName { get; init; } = TypeDiscriminator;
    public static string PrettyTypeName => "username/password";
    public override string PrettyTypeNameInstance => PrettyTypeName;
}

public class UnknownCredentialData : CredentialData, IPrettyNamedType
{
    [JsonPropertyName("type")]
    public override string TypeName { get; init; } = "unknown";
    public static string PrettyTypeName => "unknown";
    public override string PrettyTypeNameInstance => TypeName;
}

