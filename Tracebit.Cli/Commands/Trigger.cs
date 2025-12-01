using System.CommandLine;
using System.Diagnostics;
using System.Globalization;

using Spectre.Console;

using Tracebit.Cli.Extensions;
using Tracebit.Cli.State;

namespace Tracebit.Cli.Commands;

public class Trigger
{
    private const string CloudTrailResearchLink = "https://tracebit.com/blog/how-fast-is-cloudtrail-today-investigating-cloudtrail-delays-using-athena";
    public static readonly Command BaseCommand = new("trigger", "Trigger a canary on this machine to create an alert");

    private static readonly Option<string> NameOption = new("--name")
    {
        Description = "The name of the canary credential to trigger"
    };

    public static Command Command()
    {
        BaseCommand.Subcommands.Add(TriggerAwsCommand());
        BaseCommand.Subcommands.Add(TriggerSshCommand());
        BaseCommand.Subcommands.Add(TriggerCookieCommand());
        BaseCommand.Subcommands.Add(TriggerUsernamePasswordCommand());
        BaseCommand.Subcommands.Add(TriggerEmailCommand());

        BaseCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var credentials = StateManager.Credentials;
            if (credentials.Count == 0)
            {
                Utils.PrintNoCanaryCredentialsFoundMessage(parseResult);
                return 1;
            }

            var credentialToTrigger = await PromptForCredentialToTriggerAsync(credentials, cancellationToken);
            if (credentialToTrigger is null)
                return 1;

            await TriggerCredentialByTypeAsync(credentialToTrigger, cancellationToken);

            return 0;
        });
        return BaseCommand;
    }

    private static Command TriggerAwsCommand()
    {
        var command = new Command("aws", "Trigger AWS canary") { NameOption };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var awsCredentials = StateManager.AwsCredentials;
            if (awsCredentials.Count == 0)
            {
                Utils.PrintNoCanaryCredentialsOfTypeFoundMessage<AwsCredentialData>(parseResult);
                return 1;
            }

            var credentialToTrigger = await SelectCredentialToTriggerAsync(awsCredentials, parseResult.GetValue(NameOption), cancellationToken);
            if (credentialToTrigger is null)
                return 1;

            await TriggerCredentialByTypeAsync(credentialToTrigger, cancellationToken);

            return 0;
        });

        return command;
    }

    private static async Task TriggerCredentialByTypeAsync(CredentialData credential, CancellationToken cancellationToken)
    {
        switch (credential)
        {
            case AwsCredentialData aws:
                await TriggerAwsCredentialAsync(aws, cancellationToken);
                AnsiConsole.MarkupLineInterpolated($"[green]Credential {credential.Markup()} triggered[/]");
                AnsiConsole.MarkupLineInterpolated($"[yellow]AWS canary alerts take ~5 minutes to come through[/]");
                AnsiConsole.MarkupLineInterpolated($"[yellow]To find out more about this delay, check out our research on this topic here:\n{CloudTrailResearchLink}[/]");
                break;
            case SshCredentialData ssh:
                await TriggerSshCredentialAsync(ssh, cancellationToken);
                AnsiConsole.MarkupLineInterpolated($"[green]Credential {credential.Markup()} triggered[/]");
                AnsiConsole.MarkupLineInterpolated($"[yellow]SSH canary alerts take ~30 seconds to come through[/]");
                break;
            case GitlabCookieCredentialData gitlab:
                TriggerBrowserCookie(gitlab);
                AnsiConsole.MarkupLineInterpolated($"[green]Credential {credential.Markup()} triggered[/]");
                AnsiConsole.MarkupLineInterpolated($"[yellow]Cookie canary alerts take only a few seconds to come through[/]");
                break;
            case GitlabUsernamePasswordCredentialData userPass:
                TriggerUsernamePasswordAsync(userPass);
                break;
            case EmailCredentialData email:
                TriggerEmailCanaryAsync(email);
                break;
            default:
                throw new NotImplementedException($"Triggering is not supported for {credential.PrettyTypeNameInstance} credentials");
        }
    }

    private static async Task<T?> PromptForCredentialToTriggerAsync<T>(List<T> credentials, CancellationToken cancellationToken) where T : CredentialData
    {
        var choice = await AnsiConsole.PromptAsync(
            Utils.SelectCredentialPrompt(credentials).Title("Select canary to trigger:"),
            cancellationToken
        );
        return choice.Value;
    }

    private static Command TriggerSshCommand()
    {
        var command = new Command("ssh", "Trigger SSH canary") { NameOption };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var sshCredentials = StateManager.SshCredentials;
            if (sshCredentials.Count == 0)
            {
                Utils.PrintNoCanaryCredentialsOfTypeFoundMessage<SshCredentialData>(parseResult);
                return 1;
            }

            var credentialToTrigger = await SelectCredentialToTriggerAsync(sshCredentials, parseResult.GetValue(NameOption), cancellationToken);
            if (credentialToTrigger is null)
                return 1;

            await TriggerCredentialByTypeAsync(credentialToTrigger, cancellationToken);

            return 0;
        });

        return command;
    }

    private static Command TriggerCookieCommand()
    {
        var command = new Command("cookie", "Trigger browser cookie canary") { NameOption };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var cookieCredentials = StateManager.BrowserCookieCredentials;
            if (cookieCredentials.Count == 0)
            {
                Utils.PrintNoCanaryCredentialsOfTypeFoundMessage<GitlabCookieCredentialData>(parseResult);
                return 1;
            }

            var credentialToTrigger = await SelectCredentialToTriggerAsync(cookieCredentials, parseResult.GetValue(NameOption), cancellationToken);
            if (credentialToTrigger is null)
                return 1;

            await TriggerCredentialByTypeAsync(credentialToTrigger, cancellationToken);

            return 0;
        });

        return command;
    }

    private static Command TriggerUsernamePasswordCommand()
    {
        var command = new Command("username-password", "Trigger username/password canary") { NameOption };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var usernamePasswordCanaries = StateManager.UsernamePasswordCanaries;
            if (usernamePasswordCanaries.Count == 0)
            {
                Utils.PrintNoCanaryCredentialsOfTypeFoundMessage<GitlabUsernamePasswordCredentialData>(parseResult);
                return 1;
            }

            var credentialToTrigger = await SelectCredentialToTriggerAsync(usernamePasswordCanaries, parseResult.GetValue(NameOption), cancellationToken);
            if (credentialToTrigger is null)
                return 1;

            await TriggerCredentialByTypeAsync(credentialToTrigger, cancellationToken);

            return 0;
        });

        return command;
    }

    private static Command TriggerEmailCommand()
    {
        var command = new Command("email", "Trigger email canary");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var emailCanaries = StateManager.EmailCanaries;
            if (emailCanaries.Count == 0)
            {
                Utils.PrintNoCanaryCredentialsOfTypeFoundMessage<EmailCredentialData>(parseResult);
                return 1;
            }

            var credentialToTrigger = emailCanaries.First();
            await TriggerCredentialByTypeAsync(credentialToTrigger, cancellationToken);

            return 0;
        });

        return command;
    }

    private static async Task<T?> SelectCredentialToTriggerAsync<T>(List<T> credentials, string? name, CancellationToken cancellationToken) where T : CredentialData, IPrettyNamedType
    {
        if (string.IsNullOrWhiteSpace(name))
            return await PromptForCredentialToTriggerAsync(credentials, cancellationToken);

        return credentials.FirstOrDefault(c => c.Name == name) ??
               throw new MarkupException($"[red]{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(T.PrettyTypeName).EscapeMarkup()} credential [blue]{name.EscapeMarkup()}[/] not found");
    }

    private static async Task TriggerAwsCredentialAsync(AwsCredentialData awsCredential, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(awsCredential.AwsProfile))
        {
            throw new Exception("AWS canary not triggered: AWS profile is empty");
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "aws",
            Arguments = $"--profile {awsCredential.AwsProfile} --cli-connect-timeout 5 --cli-read-timeout 5 sts get-caller-identity",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        AnsiConsole.MarkupLineInterpolated($"[italic gray]{process.StartInfo.FileName} {process.StartInfo.Arguments}[/]");
        process.Start();

        var (output, error) = await AnsiConsole.Status().DefaultSpinner().StartAsync("Triggering...", async _ =>
        {
            var readOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var readError = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll([
                readOutput,
                readError,
                process.WaitForExitAsync(cancellationToken)
            ]);

            return (readOutput.Result, readError.Result);
        });

        if (!string.IsNullOrEmpty(error))
        {
            throw new Exception($"AWS canary not triggered: {error}");
        }

        if (!output.Contains("arn:aws:sts::"))
        {
            throw new Exception($"AWS canary not triggered, no sts credentials found in output: {output}");
        }
    }

    private static async Task TriggerSshCredentialAsync(CredentialData credentialData, CancellationToken cancellationToken)
    {
        const string user = "admin";
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "ssh",
            Arguments = $"-o ConnectTimeout=5 -o StrictHostKeyChecking=no {user}@{credentialData.Target}",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        AnsiConsole.MarkupLineInterpolated($"[italic gray]{process.StartInfo.FileName} {process.StartInfo.Arguments}[/]");
        process.Start();

        var error = await AnsiConsole.Status().DefaultSpinner().StartAsync("Triggering...", async _ =>
        {
            var readError = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll([
                readError,
                process.WaitForExitAsync(cancellationToken)
            ]);

            return readError.Result;
        });

        if (string.IsNullOrEmpty(error) || !error.Contains("Permission denied"))
        {
            throw new Exception($"SSH canary not triggered: {error}");
        }
    }

    private static void TriggerBrowserCookie(GitlabCookieCredentialData credential)
    {
        var triggerUrl = new UriBuilder
        {
            Scheme = "https",
            Host = credential.Target,
        }.Uri;
        Process.Start(new ProcessStartInfo { FileName = triggerUrl.ToString(), UseShellExecute = true });
        try
        {
            AnsiConsole.MarkupLineInterpolated($"Opening [blue link]{triggerUrl}[/] in your browser");
        }
        catch
        {
            AnsiConsole.MarkupLineInterpolated($"Open [blue link]{triggerUrl}[/] in your browser to trigger the credential");
            AnsiConsole.WriteLine("Once you have opened this page, press any key to continue");
            Console.ReadKey(true);
        }
    }

    private static void TriggerUsernamePasswordAsync(GitlabUsernamePasswordCredentialData credentialData)
    {
        var triggerUrl = new UriBuilder { Scheme = "https", Host = credentialData.Target }.Uri;
        AnsiConsole.MarkupLineInterpolated(
            $"To trigger {credentialData.Markup()} go to [blue link]{triggerUrl}[/] and enter the canary [darkgoldenrod]username/password[/] stored in your password manager");
    }

    private static void TriggerEmailCanaryAsync(EmailCredentialData credentialData)
    {
        var triggerableAfter = credentialData.CredentialTriggerableAfter;
        if (triggerableAfter is { } ta && DateTime.Now < triggerableAfter)
        {
            throw new MarkupException($"{credentialData.Markup()} cannot be triggered until '{ta.ToString("u", CultureInfo.InvariantCulture)}' to prevent benign positives from email service provider processing.");
        }
        AnsiConsole.MarkupLineInterpolated($"To trigger {credentialData.Markup()} go to your [blue]'{credentialData.Target}'[/] email inbox and open the email from [blue]'{credentialData.EmailFrom}'[/] with the subject [blue]'{credentialData.EmailSubject}'[/].");
        AnsiConsole.MarkupLine("You must allow showing images in the email for the canary to trigger.");
    }
}
