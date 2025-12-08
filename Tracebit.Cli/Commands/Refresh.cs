using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Tracebit.Cli.API;
using Tracebit.Cli.State;

namespace Tracebit.Cli.Commands;

public class Refresh
{
    private static readonly TimeSpan AwsRefreshTimeBeforeExpiry = TimeSpan.FromHours(2);
    private static readonly TimeSpan SshRefreshTimeAfterCreation = TimeSpan.FromDays(1);
    private static readonly TimeSpan HttpRefreshTimeBeforeExpiry = TimeSpan.FromHours(2);

    public static readonly Command BaseCommand = new("refresh", "Refresh canaries, usually run automatically by a task scheduler");
    public static Command Command(IServiceProvider services)
    {
        BaseCommand.SetAction((parseResult, cancellationToken) => RunRefreshActionAsync(services, parseResult, cancellationToken));
        return BaseCommand;
    }

    public static async Task<int> RunRefreshActionAsync(IServiceProvider services, ParseResult parseResult, CancellationToken cancellationToken)
    {
        {
            var verbose = parseResult.GetValue(GlobalOptions.Verbose);

            var tracebitClient = services.GetRequiredService<TracebitClient>();

            var credentials = StateManager.Credentials;
            if (credentials.Count == 0)
            {
                Utils.PrintNoCanaryCredentialsFoundMessage(parseResult);
                return 0;
            }

            var now = DateTime.UtcNow;
            var credentialsToRefresh = credentials.Where(credential => credential switch
                {
                    AwsCredentialData aws => AwsShouldRefresh(aws, now),
                    SshCredentialData ssh => SshShouldRefresh(ssh, now),
                    HttpCredentialData http => HttpShouldRefresh(http, now),
                    UnknownCredentialData => false,
                    _ => throw new Exception($"Unknown credential type: {credential.PrettyTypeNameInstance}")
                }
            ).ToList();

            if (credentialsToRefresh.Count == 0)
            {
                AnsiConsole.MarkupLine("All credentials are already up to date.");
                return 0;
            }

            List<Exception> exceptions = [];
            foreach (var credential in credentialsToRefresh)
            {
                AnsiConsole.MarkupLineInterpolated($"Refreshing {credential.Markup()}");

                try
                {
                    IssueCredentialsResponse response;
                    switch (credential)
                    {
                        case AwsCredentialData aws:
                            response = await Deploy.IssueCredentialsAsync(tracebitClient, credential.Name, canaryTypes: [credential.TypeName], credential.Labels, verbose);
                            await RefreshAwsAsync(response, tracebitClient, aws, verbose, cancellationToken);
                            break;
                        case SshCredentialData ssh:
                            response = await Deploy.IssueCredentialsAsync(tracebitClient, credential.Name, canaryTypes: [credential.TypeName], credential.Labels, verbose);
                            await RefreshSshAsync(response, tracebitClient, ssh, verbose, cancellationToken);
                            break;
                        case GitlabCookieCredentialData cookie:
                            response = await Deploy.IssueCredentialsAsync(tracebitClient, credential.Name, canaryTypes: [credential.TypeName], credential.Labels, verbose);
                            await RefreshBrowserCookieAsync(response, tracebitClient, cookie, verbose, cancellationToken);
                            break;
                        case GitlabUsernamePasswordCredentialData:
                            // This shouldn't happen as username/password canaries don't have an expiration
                            AnsiConsole.MarkupLine($"[yellow]Username/password can't be refreshed automatically. Remove and deploy manually.[/]");
                            break;
                        // response = await Deploy.IssueCredentialsAsync(credentialIssuer, credential.Name, canaryTypes: [credential.TypeName], credential.Labels, verbose);
                        // await RefreshUsernamePasswordAsync(response, credentialIssuer, credential, verbose, cancellationToken);
                        // break;
                        case EmailCredentialData email:
                            await RefreshEmailCanaryAsync(tracebitClient, email, verbose, cancellationToken);
                            break;
                        default:
                            throw new NotImplementedException($"Refresh is not yet implemented for ${credential.PrettyTypeNameInstance} credentials");
                    }
                }
                catch (Exception e)
                {
                    // Don't fail the whole refresh if one credential fails
                    exceptions.Add(new MarkupException($"Credential {credential.Markup()} could not be refreshed: {e.Message}", e));
                }
            }
            if (exceptions.Count != 0)
                throw new AggregateException(exceptions);

            return 0;
        }
    }

    private static bool AwsShouldRefresh(CredentialData awsCredential, DateTime now)
    {
        if (awsCredential.ExpiresAt is null)
            return false;
        var refreshTime = awsCredential.ExpiresAt - AwsRefreshTimeBeforeExpiry;
        return now >= refreshTime;
    }

    private static bool SshShouldRefresh(CredentialData sshCredential, DateTime now)
    {
        var refreshTime = sshCredential.CreatedAt + SshRefreshTimeAfterCreation;
        return now >= refreshTime;
    }

    private static bool HttpShouldRefresh(CredentialData httpCredential, DateTime now)
    {
        if (httpCredential.ExpiresAt is null)
            return false;
        var refreshTime = httpCredential.ExpiresAt - HttpRefreshTimeBeforeExpiry;
        return now >= refreshTime;
    }

    private static async Task RefreshAwsAsync(IssueCredentialsResponse response, TracebitClient tracebitClient, AwsCredentialData awsCredential, bool verbose, CancellationToken cancellationToken)
    {
        if (response.Aws is null)
        {
            AnsiConsole.MarkupLine("[red]AWS credentials could not be refreshed.[/]");
            return;
        }

        // This could happen if the credentials in the state file are from before the profile and region were added.
        // Just set them to the default values as if that command was ran with no arguments now.
        awsCredential.AwsProfile ??= (string)Deploy.AwsProfileOption.GetDefaultValue()!;
        awsCredential.AwsRegion ??= (string)Deploy.AwsRegionOption.GetDefaultValue()!;

        await Deploy.DeployAwsAsync(response.Aws, awsCredential.AwsProfile, awsCredential.AwsRegion, tracebitClient, verbose, cancellationToken);
        StateManager.AddAwsCredential(awsCredential.Name, awsCredential.Labels, response.Aws, awsCredential.AwsProfile, awsCredential.AwsRegion);
    }

    private static async Task RefreshSshAsync(IssueCredentialsResponse response, TracebitClient tracebitClient, SshCredentialData sshCredential, bool verbose, CancellationToken cancellationToken)
    {
        if (response.Ssh is null)
        {
            AnsiConsole.MarkupLine("[red]SSH credentials could not be refreshed.[/]");
            return;
        }

        await Deploy.DeploySshAsync(response.Ssh, tracebitClient, sshCredential.Path, verbose, cancellationToken);
        StateManager.AddSshCredential(sshCredential.Name, sshCredential.Labels, response.Ssh, sshCredential.Path);
    }

    private static async Task RefreshBrowserCookieAsync(IssueCredentialsResponse response, TracebitClient tracebitClient, GitlabCookieCredentialData credential, bool verbose, CancellationToken cancellationToken)
    {
        var newCredential = response.Http?.GetValueOrDefault(Constants.GitlabCookieInstanceId);
        if (newCredential is null)
        {
            AnsiConsole.MarkupLine("[red]Browser cookie credentials could not be refreshed.[/]");
            return;
        }

        await Deploy.DeployBrowserCookieAsync(newCredential, tracebitClient, verbose, cancellationToken);
        StateManager.AddBrowserCookieCredential(credential.Name, credential.Labels, newCredential);
    }

    // ReSharper disable once UnusedMember.Local
    private static async Task RefreshUsernamePasswordAsync(IssueCredentialsResponse response, TracebitClient tracebitClient, EmailCredentialData credential, bool verbose, CancellationToken cancellationToken)
    {
        var newCredential = response.Http?.GetValueOrDefault(Constants.GitlabUsernamePasswordInstanceId);
        if (newCredential is null)
        {
            AnsiConsole.MarkupLine("[red]Username/password canary could not be refreshed.[/]");
            return;
        }

        await Deploy.DeployUsernamePasswordAsync(newCredential, tracebitClient, verbose, jsonOutput: false, cancellationToken);
        StateManager.AddUsernamePasswordCredential(credential.Name, credential.Labels, newCredential);
    }

    private static async Task RefreshEmailCanaryAsync(TracebitClient tracebitClient, EmailCredentialData credential, bool verbose, CancellationToken cancellationToken)
    {
        var emailResponse = await Deploy.GenerateAndSendEmailAsync(tracebitClient, credential.Labels, verbose, cancellationToken);
        StateManager.AddEmailCredential(credential.Name, credential.Labels, emailResponse);
    }
}
