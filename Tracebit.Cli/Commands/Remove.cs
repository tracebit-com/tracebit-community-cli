using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Tracebit.Cli.API;
using Tracebit.Cli.Deploy;
using Tracebit.Cli.State;

namespace Tracebit.Cli.Commands;

public class Remove
{
    private static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Force remove canaries regardless of API responses",
        DefaultValueFactory = _ => false,
        Recursive = true
    };

    public static readonly Command BaseCommand = new("remove",
        "Remove canaries from this machine. Tracebit will no longer monitor them") { ForceOption };

    public static Command Command(IServiceProvider services)
    {
        BaseCommand.Subcommands.Add(RemoveAllCommand(services));
        BaseCommand.Subcommands.Add(RemoveAwsKeyCommand(services));
        BaseCommand.Subcommands.Add(RemoveSshKeyCommand(services));
        BaseCommand.Subcommands.Add(RemoveBrowserCookieCommand(services));
        BaseCommand.Subcommands.Add(RemoveUsernamePasswordCanaryCommand(services));
        BaseCommand.Subcommands.Add(RemoveEmailCanaryCommand(services));

        BaseCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var force = parseResult.GetRequiredValue(ForceOption);

            var credentials = StateManager.Credentials;
            if (!CredentialsCheck(credentials, parseResult))
                return 1;

            var credentialToDelete = await PromptForCredentialToDeleteAsync(credentials, cancellationToken);
            if (credentialToDelete is null)
                return 1;

            AnsiConsole.MarkupLineInterpolated($"Removing {credentialToDelete.Markup()}");
            await RemoveCredentialByTypeAsync(tracebitClient, credentialToDelete, force, cancellationToken);
            AnsiConsole.MarkupLineInterpolated($"[green]Credential {credentialToDelete.Markup()} removed[/]");
            return 0;
        });
        return BaseCommand;
    }

    private static Command RemoveAwsKeyCommand(IServiceProvider services)
    {
        var command = new Command("aws", "Remove AWS key canary from this machine");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var force = parseResult.GetRequiredValue(ForceOption);

            var awsCredentials = StateManager.AwsCredentials;
            if (!CredentialsCheck(awsCredentials, parseResult))
                return 1;

            var credentialToDelete = await PromptForCredentialToDeleteAsync(awsCredentials, cancellationToken);
            if (credentialToDelete is null)
                return 1;

            AnsiConsole.MarkupLineInterpolated($"Removing {credentialToDelete.Markup()}");
            await RemoveAwsCredentialAsync(tracebitClient, credentialToDelete, force, cancellationToken);
            AnsiConsole.MarkupLineInterpolated($"[green]Credential {credentialToDelete.Markup()} removed[/]");
            AnsiConsole.MarkupLineInterpolated($"[yellow]AWS credentials can still be used via the AWS CLI to assume a role but that activity won't be monitored by Tracebit[/]");

            return 0;
        });
        return command;
    }

    private static Command RemoveSshKeyCommand(IServiceProvider services)
    {
        var command = new Command("ssh", "Remove SSH key canary from this machine");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var force = parseResult.GetRequiredValue(ForceOption);

            var sshCredentials = StateManager.SshCredentials;
            if (!CredentialsCheck(sshCredentials, parseResult))
                return 1;

            var credentialToDelete = await PromptForCredentialToDeleteAsync(sshCredentials, cancellationToken);
            if (credentialToDelete is null)
                return 1;

            AnsiConsole.MarkupLineInterpolated($"Removing {credentialToDelete.Markup()}");
            await RemoveSshCredentialAsync(tracebitClient, credentialToDelete, force, cancellationToken);
            AnsiConsole.MarkupLineInterpolated($"[green]Credential {credentialToDelete.Markup()} removed[/]");

            return 0;
        });
        return command;
    }

    private static Command RemoveAllCommand(IServiceProvider services)
    {
        var command = new Command("all", "Remove all canaries from this machine");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var force = parseResult.GetRequiredValue(ForceOption);

            var credentials = StateManager.Credentials;
            if (!CredentialsCheck(credentials, parseResult))
                return 1;

            foreach (var credentialToDelete in credentials)
            {
                AnsiConsole.MarkupLineInterpolated($"Removing {credentialToDelete.Markup()}");
                try
                {
                    if (await RemoveCredentialByTypeAsync(tracebitClient, credentialToDelete, force, cancellationToken))
                    {
                        AnsiConsole.MarkupLineInterpolated($"[green]:check_mark_button: Credential {credentialToDelete.Markup()} removed[/]\n");
                    }
                    else
                    {
                        AnsiConsole.MarkupLineInterpolated($"[red]:cross_mark: Removal of {credentialToDelete.Markup()} cancelled[/]\n");
                    }
                }
                catch (HttpRequestException)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]:cross_mark: Credential {credentialToDelete.Markup()} not removed due to a network error[/]\n");
                }
            }

            return 0;
        });
        return command;
    }

    private static Command RemoveBrowserCookieCommand(IServiceProvider services)
    {
        Command command = new("cookie", "Remove browser cookie canary");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var force = parseResult.GetRequiredValue(ForceOption);

            var cookieCredentials = StateManager.BrowserCookieCredentials;
            if (!CredentialsCheck(cookieCredentials, parseResult))
                return 1;

            var credentialToDelete = await PromptForCredentialToDeleteAsync(cookieCredentials, cancellationToken);
            if (credentialToDelete is null)
                return 1;

            AnsiConsole.MarkupLineInterpolated($"Removing {credentialToDelete.Markup()}");
            if (await RemoveBrowserCookieCredentialAsync(tracebitClient, credentialToDelete, force, cancellationToken))
            {
                AnsiConsole.MarkupLineInterpolated($"[green]Credential {credentialToDelete.Markup()} removed[/]");
                return 0;
            }

            return 1;
        });
        return command;
    }

    private static Command RemoveUsernamePasswordCanaryCommand(IServiceProvider services)
    {
        Command command = new("username-password", "Remove username/password canary");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var force = parseResult.GetRequiredValue(ForceOption);

            var usernamePasswordCanaries = StateManager.UsernamePasswordCanaries;
            if (!CredentialsCheck(usernamePasswordCanaries, parseResult))
                return 1;

            var credentialToDelete = await PromptForCredentialToDeleteAsync(usernamePasswordCanaries, cancellationToken);
            if (credentialToDelete is null)
                return 1;

            AnsiConsole.MarkupLineInterpolated($"Removing {credentialToDelete.Markup()}");
            if (await RemoveUsernamePasswordCanaryAsync(tracebitClient, credentialToDelete, force, cancellationToken))
            {
                AnsiConsole.MarkupLineInterpolated($"[green]Credential {credentialToDelete.Markup()} removed[/]");
                return 0;
            }

            return 1;
        });
        return command;
    }

    private static Command RemoveEmailCanaryCommand(IServiceProvider services)
    {
        Command command = new("email", "Remove email canary");
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var force = parseResult.GetRequiredValue(ForceOption);

            var emailCanaries = StateManager.EmailCanaries;
            if (!CredentialsCheck(emailCanaries, parseResult))
                return 1;

            var credentialToDelete = await PromptForCredentialToDeleteAsync(emailCanaries, cancellationToken);
            if (credentialToDelete is null)
                return 1;

            AnsiConsole.MarkupLineInterpolated($"Removing {credentialToDelete.Markup()}");
            await RemoveEmailCanaryAsync(tracebitClient, credentialToDelete, force, cancellationToken);
            AnsiConsole.MarkupLineInterpolated($"[green]Credential {credentialToDelete.Markup()} removed[/]");

            return 0;
        });
        return command;
    }

    private static async Task RemoveAwsCredentialAsync(TracebitClient tracebitClient, AwsCredentialData awsCredential, bool force, CancellationToken cancellationToken)
    {
        if (awsCredential.AwsProfile is null)
            throw new Exception("AWS Profile is null, canary cannot be removed");

        await ExecuteWithForceErrorSuppression(() =>
            tracebitClient.RemoveAsync(awsCredential.Name, awsCredential.TypeName, cancellationToken),
            force
        );

        await Deployer.RemoveAwsCredential(awsCredential.AwsProfile, cancellationToken);

        StateManager.RemoveCredential(awsCredential);

    }

    private static async Task RemoveSshCredentialAsync(TracebitClient tracebitClient, SshCredentialData sshCredential, bool force, CancellationToken cancellationToken)
    {
        if (sshCredential.Target is null)
            throw new Exception("SSH IP is null, canary cannot be removed");

        await ExecuteWithForceErrorSuppression(() =>
            tracebitClient.RemoveAsync(sshCredential.Name, sshCredential.TypeName, cancellationToken),
            force
        );

        await Deployer.RemoveSshCredential(sshCredential.Target, sshCredential.Path, cancellationToken);

        StateManager.RemoveCredential(sshCredential);
    }

    private static async Task<bool> RemoveBrowserCookieCredentialAsync(TracebitClient tracebitClient, GitlabCookieCredentialData cookieCredential, bool force, CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine($"To remove the cookie from your browser, clear site data for the domain {cookieCredential.Target}");
        var remove = await AnsiConsole.PromptAsync(new ConfirmationPrompt($"Remove {cookieCredential.Markup()} from credentials state?"), cancellationToken);

        if (!remove)
            return false;

        await ExecuteWithForceErrorSuppression(() =>
            tracebitClient.RemoveAsync(cookieCredential.Name, cookieCredential.TypeName, cancellationToken),
            force
        );

        StateManager.RemoveCredential(cookieCredential);

        return true;
    }

    private static async Task<bool> RemoveUsernamePasswordCanaryAsync(TracebitClient tracebitClient, GitlabUsernamePasswordCredentialData usernamePasswordCanary, bool force, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLineInterpolated($"To remove the [darkgoldenrod]username/password[/] canary from your password manager, delete the login for the domain [blue]'{usernamePasswordCanary.Target}'[/]");
        var remove = await AnsiConsole.PromptAsync(new ConfirmationPrompt($"Remove {usernamePasswordCanary.Markup()} from credentials state?"), cancellationToken);

        if (!remove)
        {
            AnsiConsole.MarkupLine("[red]Username/password canary not removed[/]");
            return false;
        }

        await ExecuteWithForceErrorSuppression(() =>
            tracebitClient.RemoveAsync(usernamePasswordCanary.Name, usernamePasswordCanary.TypeName, cancellationToken),
            force
        );

        StateManager.RemoveCredential(usernamePasswordCanary);

        return true;
    }

    private static async Task<bool> RemoveEmailCanaryAsync(TracebitClient tracebitClient, EmailCredentialData emailCanary, bool force, CancellationToken cancellationToken)
    {
        await ExecuteWithForceErrorSuppression(() =>
            tracebitClient.RemoveAsync(emailCanary.Name, emailCanary.TypeName, cancellationToken),
            force
        );

        AnsiConsole.MarkupLine("[darkgoldenrod]Email[/] canary will no longer raise alerts. You can remove it from your inbox:");
        AnsiConsole.MarkupLineInterpolated($"[darkgoldenrod]Sender:[/] {emailCanary.EmailFrom}");
        AnsiConsole.MarkupLineInterpolated($"[darkgoldenrod]Subject:[/] {emailCanary.EmailSubject}");

        StateManager.RemoveCredential(emailCanary);
        return true;
    }

    private static async Task<CredentialData?> PromptForCredentialToDeleteAsync(List<CredentialData> credentials, CancellationToken cancellationToken)
    {
        var choice = await AnsiConsole.PromptAsync(
            Utils.SelectCredentialPrompt(credentials)
            .Title("Select a credential to remove:"),
            cancellationToken
        );
        return choice.Value;
    }

    private static async Task<T?> PromptForCredentialToDeleteAsync<T>(List<T> credentials, CancellationToken cancellationToken) where T : CredentialData, IPrettyNamedType
    {
        var choice = await AnsiConsole.PromptAsync(Utils.SelectCredentialPrompt(credentials)
            .Title($"Select {T.PrettyTypeName} credential to remove:"),
            cancellationToken
        );
        return choice.Value;
    }

    private static bool CredentialsCheck<T>(List<T> credentials, ParseResult parseResult) where T : CredentialData
    {
        if (credentials.Count == 0)
        {
            Utils.PrintNoCanaryCredentialsFoundMessage(parseResult);
            return false;
        }

        return true;
    }

    private static async Task<bool> RemoveCredentialByTypeAsync(TracebitClient tracebitClient, CredentialData credentialToDelete, bool force, CancellationToken cancellationToken)
    {
        switch (credentialToDelete)
        {
            case AwsCredentialData aws:
                await RemoveAwsCredentialAsync(tracebitClient, aws, force, cancellationToken);
                return true;
            case SshCredentialData ssh:
                await RemoveSshCredentialAsync(tracebitClient, ssh, force, cancellationToken);
                return true;
            case GitlabCookieCredentialData cookie:
                return await RemoveBrowserCookieCredentialAsync(tracebitClient, cookie, force, cancellationToken);
            case GitlabUsernamePasswordCredentialData userPass:
                return await RemoveUsernamePasswordCanaryAsync(tracebitClient, userPass, force, cancellationToken);
            case EmailCredentialData email:
                return await RemoveEmailCanaryAsync(tracebitClient, email, force, cancellationToken);
            default:
                throw new NotImplementedException($"Removal is not yet supported for {credentialToDelete.PrettyTypeNameInstance} credentials");
        }
    }

    private static async Task ExecuteWithForceErrorSuppression(Func<Task> operation, bool force)
    {
        try
        {
            await operation();
        }
        catch (HttpRequestException e) when (force)
        {
            var reason = e.StatusCode is not null ? e.StatusCode.Value.ToString() : "Http failure";
            Utils.PrintCanaryRemovalFailedUsingForce(reason);
        }
    }
}
