using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

using NuGet.Versioning;

using Spectre.Console;
using Spectre.Console.Rendering;

using Tracebit.Cli.API;
using Tracebit.Cli.Extensions;
using Tracebit.Cli.State;

namespace Tracebit.Cli.Commands;

static partial class Utils
{
    private static readonly Random Random = new();

    public static int TODO(ParseResult _)
    {
        throw new NotImplementedException();
    }

    // Used by background tasks to send errors to standard out
    public static readonly IAnsiConsole ErrorConsole = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error)
    });

    public static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var stringChars = new char[length];

        for (int i = 0; i < stringChars.Length; i++)
        {
            stringChars[i] = chars[Random.Next(chars.Length)];
        }

        return new string(stringChars);
    }

    public static void ShowTable<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(List<T> items)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);

        // Create a column for each property on the T class
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ShowInTableAttribute>()?.Show ?? true).ToList();

        foreach (var property in properties)
        {
            var showInTable = property.GetCustomAttribute<ShowInTableAttribute>();
            if (showInTable is null || showInTable.Show)
                table.AddColumn(showInTable?.ColumnName ?? property.Name);
        }

        // Add a row for each item
        foreach (var item in items)
        {
            var values = properties
                .Select<PropertyInfo, IRenderable>(p =>
                {
                    var value = p.GetValue(item);
                    return value switch
                    {
                        null => new Markup(""),
                        Markup fs => fs,
                        DateTime dt => Markup.FromInterpolated($"{dt.ToString("u", CultureInfo.InvariantCulture)}"),
                        DateTimeOffset dto => Markup.FromInterpolated($"{dto.ToString("u", CultureInfo.InvariantCulture)}"),
                        List<Label> list => Markup.FromInterpolated($"{string.Join(", ", list.Select(label => $"{label.Name}:{label.Value}"))}"),
                        _ => Markup.FromInterpolated($"{value}")
                    }
        ;
                })
                .ToArray();

            table.AddRow(values);
        }

        AnsiConsole.Write(table);
    }

    public static SelectionPrompt<OptionalSelection<T>> SelectCredentialPrompt<T>(ICollection<T> credentials) where T : CredentialData
    {
        var options = credentials
            .Select(c => new OptionalSelection<T>(c))
            .Append(new(null));

        var selectionPrompt = new SelectionPrompt<OptionalSelection<T>>()
            .PageSize(10)
            .MoreChoicesText("[grey](More)[/]")
            .WrapAround()
            .AddChoices(options);
        selectionPrompt.Converter = c => c.Value?.Markup().ToString() ?? "[red]cancel[/]";
        selectionPrompt.HighlightStyle(new Style(foreground: Color.Purple, decoration: Decoration.Bold));

        return selectionPrompt;
    }

    public static HashSet<string> AwsRegions() =>
    [
        "af-south-1", "ap-east-1", "ap-east-2", "ap-northeast-1", "ap-northeast-2", "ap-northeast-3", "ap-south-1",
        "ap-south-2", "ap-southeast-1", "ap-southeast-2", "ap-southeast-3", "ap-southeast-4", "ap-southeast-5",
        "ap-southeast-6", "ap-southeast-7", "ca-central-1", "ca-west-1", "eu-central-1", "eu-central-2", "eu-north-1",
        "eu-south-1", "eu-south-2", "eu-west-1", "eu-west-2", "eu-west-3", "il-central-1", "me-central-1", "me-south-1",
        "mx-central-1", "sa-east-1", "us-east-1", "us-east-2", "us-west-1", "us-west-2"
    ];

    public static void PrintNoCanaryCredentialsFoundMessage(ParseResult parseResult)
    {
        AnsiConsole.MarkupLineInterpolated($"No credentials are deployed on this device yet, get started by running [purple]{parseResult.RootCommandResult.IdentifierToken} {Deploy.BaseCommand.Name}[/]");
    }

    public static void PrintNoCanaryCredentialsOfTypeFoundMessage<T>(ParseResult parseResult) where T : CredentialData, IPrettyNamedType
    {
        AnsiConsole.MarkupLineInterpolated($"No {T.PrettyTypeName} credentials are deployed on this device yet, get started by running [purple]{parseResult.RootCommandResult.IdentifierToken} {Deploy.BaseCommand.Name}[/]");
    }

    public static void PrintCanaryCredentialSuccessfullyDeployedMessage<T>(T credential) where T : CredentialData, IPrettyNamedType
    {
        AnsiConsole.MarkupLineInterpolated($"[green]:check_mark_button: Credential {credential.Markup()} deployed{(string.IsNullOrEmpty(credential.Path) ? "" : $" to '{credential.Path}'")}[/]\n");
    }

    public static bool IsLabelFormat(string input)
    {
        return LabelFormat().IsMatch(input);
    }

    public static void PrintCanaryRemovalFailedUsingForce(string reason)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"[yellow]Credential could not be deactivated due to '{reason}', but will still be removed from your device. The credential can be deactivated manually at [blue link]{Constants.TracebitUrl}[/][/]"
        );
    }

    public static string InvalidLabelFormatMessage() => "Invalid label format, please use key1=value1,key2=value2";

    [GeneratedRegex("^([^,=]+=[^,=]+)(,[^,=]+=[^,=]+)*$")]
    private static partial Regex LabelFormat();

    public static string GenerateRandomPasswordManagerLoginName()
    {
        var examples = new List<string>
        {
            "GitLab - Production",
            "GitLab - Staging",
            "GitLab - Dev",
            "GitLab - Personal Projects",
            "GitLab - Work"
        };
        return examples[Random.Shared.Next(examples.Count)];
    }

    public static async Task NotifyForUpdatesAsync(ParseResult parseResult, Task<GetLatestCliReleaseResponse> getLatestCliRelease)
    {
        try
        {
            // Try and fetch latest release from API but skip if it takes longer than a second
            var response = await getLatestCliRelease;
            var latestVersion = SemanticVersion.Parse(response.TagName);
            // Release `Prerelease` value is based on the semver, but check both in case we have manually overridden it in the release
            if (!response.Prerelease && !latestVersion.IsPrerelease && GetCurrentVersion() < latestVersion)
                ErrorConsole.MarkupLineInterpolated($"[yellow]A new version of the Tracebit CLI is available\nUpgrade to the latest version ({response.TagName}) at [blue link]{response.HtmlUrl}[/][/]");
            else if (parseResult.GetRequiredValue(GlobalOptions.Verbose))
                ErrorConsole.WriteLine($"Tracebit CLI is up to date: currently {GetCurrentVersion()}, latest release is {latestVersion}");
        }
        catch (Exception e)
        {
            // By default, do not show errors from release check
            if (parseResult.GetRequiredValue(GlobalOptions.Verbose))
            {
                var errorDetail = parseResult.GetRequiredValue(GlobalOptions.ErrorDetail);
                var stacktrace = parseResult.GetRequiredValue(GlobalOptions.Stacktrace);
                ErrorConsole.WritePrettyException(new Exception("Failed to get latest release", e), errorDetail, stacktrace);
            }
        }
    }

    public static async Task ReportUpdateStatusFailures(ParseResult parseResult, Task updateStatus)
    {
        try
        {
            await updateStatus;
        }
        catch (Exception e)
        {
            if (parseResult.GetRequiredValue(GlobalOptions.Verbose))
            {
                var errorDetail = parseResult.GetRequiredValue(GlobalOptions.ErrorDetail);
                var stacktrace = parseResult.GetRequiredValue(GlobalOptions.Stacktrace);
                ErrorConsole.WritePrettyException(new Exception("Failed to update status", e), errorDetail, stacktrace);
            }
        }
    }

    public static SemanticVersion? GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false).OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault();
        if (version is null)
            return null;

        if (SemanticVersion.TryParse(version.InformationalVersion, out var semanticVersion))
            return semanticVersion;

        return null;
    }
}

public class MarkupException(FormattableString formattableMessage, Exception? innerException) : Exception(Markup.Remove(formattableMessage.ToString()), innerException)
{
    public readonly FormattableString FormattableMessage = formattableMessage;

    public MarkupException(FormattableString formattableMessage) : this(formattableMessage, null)
    {
    }
}

public class CredentialAlreadyExistsException : MarkupException
{
    public CredentialAlreadyExistsException(FormattableString formattableMessage)
        : base(formattableMessage)
    {
    }

    public CredentialAlreadyExistsException(ParseResult parseResult, CredentialData existingCanary, string nameOption)
        : base($"[red]Credential {existingCanary.Markup()} already exists.\nDeploy a new canary with [purple]{nameOption}[/] or remove the existing canary with [purple]{parseResult.RootCommandResult.IdentifierToken} {Remove.BaseCommand.Name}[/].[/]")
    {
    }
}

public class EmailCredentialAlreadyExistsException(ParseResult parseResult, CredentialData existingCanary)
    : CredentialAlreadyExistsException(
        $"[red]Credential {existingCanary.Markup()} already exists.\nRemove the existing canary with [purple]{parseResult.RootCommandResult.IdentifierToken} {Remove.BaseCommand.Name}[/].[/]");

public record OptionalSelection<T>(T? Value);

public static class YesNoNoDefaultPrompt
{
    public const char Yes = 'y';
    private const char No = 'n';

    public static readonly TextPrompt<char> Prompt = new TextPrompt<char>("Have you saved this in your password manager?")
        .ShowChoices(true)
        .AddChoice(Yes)
        .AddChoice(No);
}
