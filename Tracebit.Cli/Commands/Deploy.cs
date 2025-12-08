using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

using Tracebit.Cli.API;
using Tracebit.Cli.API.CanaryCredentials;
using Tracebit.Cli.Deploy;
using Tracebit.Cli.Extensions;
using Tracebit.Cli.State;

using Labels = System.Collections.Generic.List<Tracebit.Cli.API.Label>;

namespace Tracebit.Cli.Commands;

static class Deploy
{
    public static readonly Command BaseCommand = new("deploy", "Deploy canaries on this machine");
    private static readonly Option<string> NameOption = new("--name")
    {
        Description = "The name of the canary credentials",
        DefaultValueFactory = _ => Environment.MachineName
    };

    private static readonly Option<Labels> LabelsOption = new("--labels")
    {
        Description = "Additional labels as key=value pairs (comma-separated)",
        CustomParser = result =>
        {
            var token = result.Tokens.Single().Value;
            return _labelsParsing(token);
        }
    };

    private static Labels _labelsParsing(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(label => label.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => new Label
            {
                Name = parts[0],
                Value = parts[1]
            })
            .ToList();
    }

    public static readonly Option<string> AwsProfileOption = new("--aws-profile")
    {
        Description = "The AWS profile name of the canary credentials"
    };

    public static readonly Option<string> AwsRegionOption = new("--aws-region")
    {
        Description = "The AWS profile region of the canary credentials"
    };

    private static readonly Option<string> SshKeyFileOption = new("--ssh-key-file")
    {
        Description = "The name of the local SSH key file"
    };

    public static readonly Option<bool> JsonOutputOption = new("--json-output")
    {
        Description = "Return the output in json format",
        DefaultValueFactory = _ => false
    };

    public static Command Command(IServiceProvider services)
    {
        ConfigureAwsProfileValidation();
        ConfigureSshFileOptionValidation();
        ConfigureLabelsOptionValidation();

        BaseCommand.Subcommands.Add(DeployAllCommand(services));
        BaseCommand.Subcommands.Add(DeployAwsKeyCommand(services));
        BaseCommand.Subcommands.Add(DeploySshKeyCommand(services));
        BaseCommand.Subcommands.Add(DeployBrowserCookieCommand(services));
        BaseCommand.Subcommands.Add(DeployUsernamePasswordCommand(services));
        BaseCommand.Subcommands.Add(DeployEmailCommand(services));
        return BaseCommand;
    }

    private static Command DeployAwsKeyCommand(IServiceProvider services)
    {
        var command = DeployCanaryCredentialsCommand("aws", "Deploy AWS key canary");
        command.Add(AwsProfileOption);
        command.Add(AwsRegionOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var metadata = await tracebitClient.GenerateCredentialMetadataAsync(cancellationToken);

            var name = parseResult.GetRequiredValue(NameOption);
            var profile = parseResult.GetValue(AwsProfileOption) ?? metadata.AwsProfileName;
            var region = parseResult.GetValue(AwsRegionOption) ?? metadata.AwsRegion;
            var labels = parseResult.GetValue(LabelsOption);
            var verbose = parseResult.GetValue(GlobalOptions.Verbose);

            var interactive = parseResult.GetValue(GlobalOptions.Interactive);
            if (interactive)
            {
                var defaultNameExists = StateManager.CheckIfCredentialExists<AwsCredentialData>(name);
                (name, profile, region, labels) = await AwsPromptInteractiveAsync(name, defaultNameExists, profile, region, cancellationToken);
            }

            StateManager.EnsureCredentialDoesNotExist<AwsCredentialData>(name, parseResult, NameOption.Name);

            var credential = await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ =>
            {
                var response = await IssueCredentialsAsync(tracebitClient, name, canaryTypes: ["aws"], labels, verbose);

                if (response.Aws is null)
                    throw new Exception("AWS credentials were not found in issued credentials");

                await DeployAwsAsync(response.Aws, profile, region, tracebitClient, verbose, cancellationToken);

                var credential = StateManager.AddAwsCredential(name, labels, response.Aws, profile, region);
                return credential;
            });

            AnsiConsole.MarkupLineInterpolated($"[green]Credential {credential.Markup()} deployed to '{credential.Path}'[/]");

            return 0;
        });
        return command;
    }

    private static Command DeploySshKeyCommand(IServiceProvider services)
    {
        var command = DeployCanaryCredentialsCommand("ssh", "Deploy SSH key canary");
        command.Add(SshKeyFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var metadata = await tracebitClient.GenerateCredentialMetadataAsync(cancellationToken);

            var name = parseResult.GetRequiredValue(NameOption);
            var labels = parseResult.GetValue(LabelsOption);
            var sshKeyFileName = parseResult.GetValue(SshKeyFileOption) ?? metadata.SshKeyFileName;
            var verbose = parseResult.GetValue(GlobalOptions.Verbose);

            var interactive = parseResult.GetValue(GlobalOptions.Interactive);
            if (interactive)
            {
                var defaultNameExists = StateManager.CheckIfCredentialExists<SshCredentialData>(name);
                (name, labels, sshKeyFileName) = await SshPromptInteractiveAsync(name, defaultNameExists, sshKeyFileName, cancellationToken);
            }

            StateManager.EnsureCredentialDoesNotExist<SshCredentialData>(name, parseResult, NameOption.Name);

            var credential = await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ =>
            {
                var response = await IssueCredentialsAsync(tracebitClient, name, canaryTypes: ["ssh"], labels, verbose);

                if (response.Ssh is null)
                    throw new Exception("SSH credentials were not found in issued credentials");

                await DeploySshAsync(response.Ssh, tracebitClient, sshKeyFileName, verbose, cancellationToken);

                var credential = StateManager.AddSshCredential(name, labels, response.Ssh, sshKeyFileName);
                return credential;
            });

            AnsiConsole.MarkupLineInterpolated($"[green]Credential {credential.Markup()} deployed to '{credential.Path}'[/]");

            return 0;
        });
        return command;
    }

    public static Command DeployAllBaseCommand()
    {
        return DeployCanaryCredentialsCommand("all", "Deploy all canaries");
    }

    private static Command DeployAllCommand(IServiceProvider services)
    {
        var command = DeployAllBaseCommand();
        command.Add(AwsProfileOption);
        command.Add(AwsRegionOption);
        command.Add(SshKeyFileOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var metadata = await tracebitClient.GenerateCredentialMetadataAsync(cancellationToken);

            var name = parseResult.GetRequiredValue(NameOption);
            var profile = parseResult.GetValue(AwsProfileOption) ?? metadata.AwsProfileName;
            var region = parseResult.GetValue(AwsRegionOption) ?? metadata.AwsRegion;
            var labels = parseResult.GetValue(LabelsOption);
            var sshKeyFileName = parseResult.GetValue(SshKeyFileOption) ?? metadata.SshKeyFileName;
            var verbose = parseResult.GetValue(GlobalOptions.Verbose);

            var allCredentialTypes = new HashSet<string>
            {
                AwsCredentialData.TypeDiscriminator,
                SshCredentialData.TypeDiscriminator,
                GitlabCookieCredentialData.TypeDiscriminator,
                GitlabUsernamePasswordCredentialData.TypeDiscriminator,
                EmailCredentialData.TypeDiscriminator
            };
            // Credential types that are issued from the IssueCredentials endpoint
            var missingCredentialTypes = StateManager.FilterNotExistingCanaryTypesForName(name, allCredentialTypes);
            if (missingCredentialTypes.Count == 0)
            {
                AnsiConsole.MarkupLine("All canaries with this name already exist");
                return 0;
            }

            List<CredentialData> deployedCredentials = [];

            var missingIssueCredentialTypes = new HashSet<string>(missingCredentialTypes.Except(new HashSet<string> { "email" }));
            if (missingIssueCredentialTypes.Count > 0)
            {
                IssueCredentialsResponse? issueCredentialsResponse = null;
                try
                {
                    await AnsiConsole.Status().DefaultSpinner().StartAsync("Issuing...", async _ =>
                    {
                        issueCredentialsResponse = await IssueCredentialsAsync(tracebitClient, name, canaryTypes: missingIssueCredentialTypes.ToList(), labels, verbose);
                    });
                }
                catch (QuotaExceededException) { }

                if (issueCredentialsResponse is not null)
                {
                    if (issueCredentialsResponse.Aws is not null)
                    {
                        AnsiConsole.MarkupLine("Deploying [darkgoldenrod]AWS[/] credentials");
                        await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ => await Task.WhenAll(
                            DeployAwsAsync(issueCredentialsResponse.Aws, profile, region, tracebitClient, verbose, cancellationToken),
                            Task.Delay(400, cancellationToken)
                        ));
                        var deployedCredential = StateManager.AddAwsCredential(name, labels, issueCredentialsResponse.Aws, profile, region);
                        deployedCredentials.Add(deployedCredential);
                        Utils.PrintCanaryCredentialSuccessfullyDeployedMessage(deployedCredential);
                    }

                    if (issueCredentialsResponse.Ssh is not null)
                    {
                        AnsiConsole.MarkupLine("Deploying [darkgoldenrod]SSH[/] credentials");
                        await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ => await Task.WhenAll(
                            DeploySshAsync(issueCredentialsResponse.Ssh, tracebitClient, sshKeyFileName, verbose, cancellationToken),
                            Task.Delay(400, cancellationToken)
                        ));
                        var deployedCredential = StateManager.AddSshCredential(name, labels, issueCredentialsResponse.Ssh, sshKeyFileName);
                        deployedCredentials.Add(deployedCredential);
                        Utils.PrintCanaryCredentialSuccessfullyDeployedMessage(deployedCredential);
                    }

                    if (issueCredentialsResponse.Http is not null && issueCredentialsResponse.Http.TryGetValue("gitlab-cookie", out var browserCookieCredentials))
                    {
                        AnsiConsole.MarkupLine("Deploying [darkgoldenrod]username/password[/] credentials");
                        await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ => await Task.WhenAll(
                            DeployBrowserCookieAsync(browserCookieCredentials, tracebitClient, verbose, cancellationToken),
                            Task.Delay(400, cancellationToken)
                        ));
                        var deployedCredential = StateManager.AddBrowserCookieCredential(name, labels, browserCookieCredentials);
                        deployedCredentials.Add(deployedCredential);
                        Utils.PrintCanaryCredentialSuccessfullyDeployedMessage(deployedCredential);
                        await Task.Delay(400, cancellationToken);
                    }

                    if (issueCredentialsResponse.Http is not null && issueCredentialsResponse.Http.TryGetValue("gitlab-username-password", out var usernamePasswordCredentials))
                    {
                        AnsiConsole.MarkupLine("Deploying [darkgoldenrod]username/password[/] credentials");
                        await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ => await Task.WhenAll(
                            DeployUsernamePasswordAsync(usernamePasswordCredentials, tracebitClient, verbose, jsonOutput: false, cancellationToken),
                            Task.Delay(400, cancellationToken)
                        ));
                        var deployedCredential = StateManager.AddUsernamePasswordCredential(name, labels, usernamePasswordCredentials);
                        deployedCredentials.Add(deployedCredential);

                        if (await AnsiConsole.PromptAsync(YesNoNoDefaultPrompt.Prompt, cancellationToken) == YesNoNoDefaultPrompt.Yes)
                        {
                            Utils.PrintCanaryCredentialSuccessfullyDeployedMessage(deployedCredential);
                        }
                        else
                        {
                            await Remove.RemoveUsernamePasswordCanaryAsync(tracebitClient, deployedCredential, force: true, cancellationToken);
                            StateManager.RemoveCredential(deployedCredential);
                            AnsiConsole.MarkupLineInterpolated($"[red]:cross_mark: Credential {deployedCredential.Markup()} not deployed and marked as expired on Tracebit[/]\n");
                        }
                    }
                }
            }

            if (missingCredentialTypes.Contains("email"))
            {
                AnsiConsole.MarkupLine("Deploying [darkgoldenrod]email[/] canary");
                try
                {
                    var deployEmailTask = GenerateAndSendEmailAsync(tracebitClient, labels, verbose, cancellationToken);
                    await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ => await Task.WhenAll(
                        deployEmailTask,
                        Task.Delay(400, cancellationToken)
                    ));
                    var emailResponse = deployEmailTask.Result;
                    var emailCredential = StateManager.AddEmailCredential(Constants.EmailCanaryName, labels, emailResponse);
                    deployedCredentials.Add(emailCredential);
                    AnsiConsole.MarkupLineInterpolated(
                        $"[darkgoldenrod]Email[/] canary with subject: [blue]'{emailCredential.EmailSubject}'[/] sent to [blue]'{emailCredential.Target}'[/] from [blue]'{emailCredential.EmailFrom}'[/]");
                    AnsiConsole.MarkupLine("[yellow]The email may take a few minutes to arrive and could end up in your spam folder.[/]");
                    AnsiConsole.MarkupLine("[yellow]Make sure to allow showing images in emails from that domain.[/]");
                    Utils.PrintCanaryCredentialSuccessfullyDeployedMessage(emailCredential);
                }
                catch (QuotaExceededException) { }
            }
            else
            {
                if (verbose)
                    AnsiConsole.WriteLine("Email canary exists, skipping");
            }

            // Find out which credential types have been skipped
            var skippedTypes = allCredentialTypes.ToHashSet();
            foreach (var deployedCredential in deployedCredentials)
            {
                skippedTypes.Remove(deployedCredential.TypeName);
            }

            foreach (var skippedType in skippedTypes)
            {
                if (missingCredentialTypes.Contains(skippedType))
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Credential of type [darkgoldenrod]{skippedType}[/] for name [blue]{name}[/] not deployed due to quota limits[/]");
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Credential of type [darkgoldenrod]{skippedType}[/] for name [blue]{name}[/] not deployed as it exists already on this machine[/]");
                }
            }

            return 0;
        });
        return command;
    }

    private static Command DeployBrowserCookieCommand(IServiceProvider services)
    {
        Command command = DeployCanaryCredentialsCommand("cookie", "Deploy browser cookie canary");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var name = parseResult.GetRequiredValue(NameOption);
            var labels = parseResult.GetValue(LabelsOption);
            var verbose = parseResult.GetValue(GlobalOptions.Verbose);

            var interactive = parseResult.GetValue(GlobalOptions.Interactive);
            if (interactive)
            {
                var defaultNameExists = StateManager.CheckIfCredentialExists<GitlabCookieCredentialData>(name);
                (name, labels) = await CookiePromptInteractiveAsync(name, defaultNameExists, cancellationToken);
            }

            StateManager.EnsureCredentialDoesNotExist<GitlabCookieCredentialData>(name, parseResult, NameOption.Name);

            var credential = await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ =>
            {
                var tracebitClient = services.GetRequiredService<TracebitClient>();
                var response = await IssueCredentialsAsync(tracebitClient, name, canaryTypes: [Constants.GitlabCookieInstanceId], labels, verbose);

                if (response.Http is null || !response.Http.TryGetValue(Constants.GitlabCookieInstanceId, out HttpCanaryCredentials? cookieCredentials))
                    throw new Exception("Cookie credentials were not found in issued credentials");

                await DeployBrowserCookieAsync(cookieCredentials, tracebitClient, verbose, cancellationToken);

                var credential = StateManager.AddBrowserCookieCredential(name, labels, cookieCredentials);
                return credential;
            });

            AnsiConsole.MarkupLineInterpolated($"[green]Credential {credential.Markup()} deployed to your browser[/]");

            return 0;
        });
        return command;
    }

    private static Command DeployUsernamePasswordCommand(IServiceProvider services)
    {
        var command = DeployCanaryCredentialsCommand("username-password", "Deploy username/password canary");
        command.Add(JsonOutputOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var name = parseResult.GetRequiredValue(NameOption);
            var labels = parseResult.GetValue(LabelsOption);
            var verbose = parseResult.GetValue(GlobalOptions.Verbose);
            var jsonOutput = parseResult.GetValue(JsonOutputOption);

            var interactive = parseResult.GetValue(GlobalOptions.Interactive);
            if (interactive)
            {
                var defaultNameExists = StateManager.CheckIfCredentialExists<GitlabUsernamePasswordCredentialData>(name);
                (name, labels) = await UsernamePasswordPromptInteractiveAsync(name, defaultNameExists, cancellationToken);
            }

            StateManager.EnsureCredentialDoesNotExist<GitlabUsernamePasswordCredentialData>(name, parseResult, NameOption.Name);

            var tracebitClient = services.GetRequiredService<TracebitClient>();
            var issueCredentialsTask = IssueCredentialsAsync(tracebitClient, name, canaryTypes: [Constants.GitlabUsernamePasswordInstanceId], labels, verbose);
            if (!jsonOutput)
                // ReSharper disable once AccessToModifiedClosure
                issueCredentialsTask = AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ => await issueCredentialsTask);
            var response = await issueCredentialsTask;

            if (response.Http is null || !response.Http.TryGetValue(Constants.GitlabUsernamePasswordInstanceId, out HttpCanaryCredentials? usernamePasswordCredentials))
                throw new Exception("Username/password credentials were not found in issued credentials");

            await DeployUsernamePasswordAsync(usernamePasswordCredentials, tracebitClient, verbose, jsonOutput, cancellationToken);
            var deployedCredential = StateManager.AddUsernamePasswordCredential(name, labels, usernamePasswordCredentials);

            if (jsonOutput)
                return 0;

            if (await AnsiConsole.PromptAsync(YesNoNoDefaultPrompt.Prompt, cancellationToken) == YesNoNoDefaultPrompt.Yes)
            {
                Utils.PrintCanaryCredentialSuccessfullyDeployedMessage(deployedCredential);
            }
            else
            {
                await Remove.RemoveUsernamePasswordCanaryAsync(tracebitClient, deployedCredential, force: true, cancellationToken);
                StateManager.RemoveCredential(deployedCredential);
                AnsiConsole.MarkupLineInterpolated($"[red]:cross_mark: Credential {deployedCredential.Markup()} not deployed and marked as expired on Tracebit[/]\n");
            }

            return 0;
        });
        return command;
    }

    private static Command DeployEmailCommand(IServiceProvider services)
    {
        var command = new Command("email", "Deploy email canary")
        {
            LabelsOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            const string name = Constants.EmailCanaryName;
            var labels = parseResult.GetValue(LabelsOption) ?? [];
            var verbose = parseResult.GetValue(GlobalOptions.Verbose);

            StateManager.EnsureEmailCredentialDoesNotExist(name, parseResult);

            var interactive = parseResult.GetValue(GlobalOptions.Interactive);
            if (interactive)
            {
                labels = await EmailPromptInteractiveAsync(cancellationToken);
            }

            var credential = await AnsiConsole.Status().DefaultSpinner().StartAsync("Deploying...", async _ =>
            {
                var tracebitClient = services.GetRequiredService<TracebitClient>();
                var response = await GenerateAndSendEmailAsync(tracebitClient, labels, verbose, cancellationToken);

                // Email canaries aren't deployed to the existing machine so there is no deploy step

                var credential = StateManager.AddEmailCredential(name, labels, response);
                return credential;
            });

            AnsiConsole.MarkupLineInterpolated(
                $"[green]Email canary with subject: '{credential.EmailSubject}' sent to '{credential.Target}' from '{credential.EmailFrom}'[/]");
            AnsiConsole.MarkupLine("[yellow]The email may take a few minutes to arrive and could end up in your spam folder.[/]");
            AnsiConsole.MarkupLine("[yellow]Make sure to allow showing images in emails from that domain.[/]");

            var triggerableAfter = credential.CredentialTriggerableAfter;
            if (triggerableAfter is not { } ta)
                return 0;

            var now = DateTime.Now;
            var difference = ta - now;
            var minutesRoundedUp = (int)Math.Ceiling(difference.TotalMinutes);
            AnsiConsole.MarkupLineInterpolated(
                $"[yellow]Opening the email will create alerts only after {minutesRoundedUp} minutes ({ta.ToString("u", CultureInfo.InvariantCulture)}) to reduce false positives from the email processing.[/]");

            return 0;
        });
        return command;
    }

    public static async Task<IssueCredentialsResponse> IssueCredentialsAsync(TracebitClient tracebitClient, string name, List<string> canaryTypes, Labels? labels, bool verbose)
    {
        var canaryTypesStrings = string.Join(", ", canaryTypes.Select(c => c.ToUpper()));
        if (verbose)
            AnsiConsole.MarkupLineInterpolated($"Issuing {canaryTypesStrings} credentials");

        var issueCredentialsResponse = await tracebitClient.IssueCredentials(name, canaryTypes, labels);
        if (verbose)
            AnsiConsole.MarkupLineInterpolated($"{canaryTypesStrings} credentials issued successfully");
        return issueCredentialsResponse;
    }

    public static async Task<GenerateAndSendCanaryEmailResponse> GenerateAndSendEmailAsync(TracebitClient tracebitClient, Labels? labels, bool verbose, CancellationToken cancellationToken)
    {
        if (verbose)
            AnsiConsole.MarkupLineInterpolated($"Sending email canary");

        var emailResponse = await tracebitClient.GenerateAndSendCanaryEmailAsync(labels, cancellationToken);
        if (verbose)
            AnsiConsole.MarkupLine("Email canary sent successfully");
        return emailResponse;
    }

    public static async Task DeployAwsAsync(AwsCanaryCredentials awsCanaryCredentials, string profile, string region, TracebitClient tracebitClient, bool verbose, CancellationToken cancellationToken)
    {
        if (verbose)
            AnsiConsole.MarkupLine("Writing AWS credentials to config");
        await Deployer.WriteAwsKeyToConfigAsync(awsCanaryCredentials, profile, region, cancellationToken);

        if (verbose)
            AnsiConsole.MarkupLine("Confirming AWS credentials");
        var awsConfirmationId = awsCanaryCredentials.AwsConfirmationId;
        await tracebitClient.ConfirmCredentialsAsync(awsConfirmationId, cancellationToken);
        if (verbose)
            AnsiConsole.MarkupLine("AWS credentials confirmed");
    }

    public static async Task DeploySshAsync(SshCanaryCredentials sshCanaryCredentials, TracebitClient tracebitClient, string sshKeyFileName, bool verbose, CancellationToken cancellationToken)
    {
        if (verbose)
            AnsiConsole.MarkupLine("Writing SSH credentials to config");
        await Deployer.WriteSshKeyToConfig(sshCanaryCredentials, sshKeyFileName, cancellationToken);

        if (verbose)
            AnsiConsole.MarkupLine("Confirming SSH credentials");
        var sshConfirmationId = sshCanaryCredentials.SshConfirmationId;
        await tracebitClient.ConfirmCredentialsAsync(sshConfirmationId, cancellationToken);
        if (verbose)
            AnsiConsole.MarkupLine("SSH credentials confirmed");
    }

    public static async Task BrowserDeployHttpCredentialAsync(HttpCanaryCredentials httpCanaryCredentials)
    {
        if (httpCanaryCredentials.HostNames.Count == 0)
            throw new Exception("Browser cookie credentials could not be deployed: hostname is missing from issued credentials");

        var deployUrl = new UriBuilder
        {
            Scheme = "https",
            Host = httpCanaryCredentials.HostNames[0],
            Path = "/_browserDeploy",
            Query = $"?id={httpCanaryCredentials.BrowserDeploymentId}"
        }.Uri;

        AnsiConsole.MarkupLineInterpolated($"Opening [blue link]{deployUrl}[/] in your browser");
        await Task.Delay(750);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = deployUrl.ToString(), UseShellExecute = true });
        }
        catch
        {
            AnsiConsole.MarkupLineInterpolated($"Open [blue link]{deployUrl}[/] in your browser to deploy the credential");
            AnsiConsole.WriteLine("Once you have opened the page, press any key to continue");
            Console.ReadKey(true);
        }
    }


    public static async Task DeployBrowserCookieAsync(HttpCanaryCredentials httpCanaryCredentials, TracebitClient tracebitClient, bool verbose, CancellationToken cancellationToken)
    {
        await BrowserDeployHttpCredentialAsync(httpCanaryCredentials);

        if (verbose)
            AnsiConsole.MarkupLine("Confirming cookie credentials");
        var confirmationId = httpCanaryCredentials.ConfirmationId;
        await tracebitClient.ConfirmCredentialsAsync(confirmationId, cancellationToken);
        if (verbose)
            AnsiConsole.MarkupLine("Cookie credentials confirmed");
    }

    public static async Task DeployUsernamePasswordAsync(HttpCanaryCredentials httpCanaryCredentials, TracebitClient tracebitClient, bool verbose, bool jsonOutput, CancellationToken cancellationToken)
    {
        var usernamePassword = httpCanaryCredentials.Credentials.Deserialize(ApiJsonSerializerContext.Default.UsernamePasswordData);
        if (usernamePassword == null)
            throw new Exception("Username/password could not parsed from the response");

        var suggestedName = Utils.GenerateRandomPasswordManagerLoginName();
        if (jsonOutput)
        {
            AnsiConsole.MarkupLine(new UsernamePasswordOutput
            {
                Domain = httpCanaryCredentials.HostNames.First(),
                SuggestedName = suggestedName,
                Username = usernamePassword.Username,
                Password = usernamePassword.Password,
            }.Serialize()
            );
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated(
                $"To deploy the [darkgoldenrod]username/password[/] canary open your password manager and create a new login for domain [blue]'{httpCanaryCredentials.HostNames.First()}'[/] and insert:");
            AnsiConsole.MarkupLineInterpolated($"[darkgoldenrod]Suggested password manager login name:[/] {suggestedName}");
            AnsiConsole.MarkupLineInterpolated($"[darkgoldenrod]username:[/] {usernamePassword.Username}\n[darkgoldenrod]password:[/] {usernamePassword.Password}");
            AnsiConsole.MarkupLineInterpolated($"[italic gray]You can use '[purple]{Program.RootCommand().Name} {BaseCommand.Name} {DeployUsernamePasswordCommand(new ServiceCollection().BuildServiceProvider()).Name} {JsonOutputOption.Name}[/]' if you need the credentials in JSON form[/]");
        }

        if (verbose)
            AnsiConsole.MarkupLine("Confirming username/password credentials");
        var confirmationId = httpCanaryCredentials.ConfirmationId;
        await tracebitClient.ConfirmCredentialsAsync(confirmationId, cancellationToken);
        if (verbose)
            AnsiConsole.MarkupLine("Username/password canary confirmed");
    }

    private static void ConfigureAwsProfileValidation()
    {
        AwsProfileOption.Validators.Add(result =>
        {
            if (result.Tokens.Count == 0)
                return;
            var value = result.Tokens.Single().Value;
            if (value.Any(char.IsWhiteSpace))
            {
                result.AddError("Whitespace in AWS profile name not supported");
            }
            // According to official AWS docs: Do not use the word profile when creating an entry in the credentials file.
            if (value == "profile")
            {
                result.AddError("AWS profile name cannot be 'profile'");
            }
        });
    }

    private static void ConfigureSshFileOptionValidation()
    {
        SshKeyFileOption.AcceptLegalFileNamesOnly();
        SshKeyFileOption.Validators.Add(result =>
        {
            if (result.Tokens.Count == 0)
                return;
            var value = result.Tokens.Single().Value;
            if (value.Any(char.IsWhiteSpace))
            {
                result.AddError("Whitespace in SSH key file names not supported");
            }
        });
    }

    private static void ConfigureLabelsOptionValidation()
    {
        LabelsOption.Validators.Add(result =>
        {
            if (result.Tokens.Count == 0)
                return;
            var value = result.Tokens.Single().Value;
            if (!Utils.IsLabelFormat(value))
            {
                result.AddError(Utils.InvalidLabelFormatMessage());
            }
        });
    }

    private static Command DeployCanaryCredentialsCommand(string type, string description)
    {
        return new Command(type, description)
        {
            NameOption,
            LabelsOption
        };
    }

    private static async Task<(string, string, string, Labels)> AwsPromptInteractiveAsync(string defaultName, bool defaultNameExists, string defaultProfile, string defaultRegion, CancellationToken cancellationToken)
    {
        defaultName = defaultNameExists ? $"{defaultName}-{Utils.GenerateRandomString(5)}" : defaultName;
        var name = await AnsiConsole.PromptAsync(TextPrompts.NamePrompt(defaultName), cancellationToken);
        var profile = await AnsiConsole.PromptAsync(TextPrompts.ProfilePrompt(defaultProfile), cancellationToken);
        var region = await AnsiConsole.PromptAsync(TextPrompts.RegionPrompt(defaultRegion), cancellationToken);
        var labels = _labelsParsing(await AnsiConsole.PromptAsync(TextPrompts.LabelsPrompt, cancellationToken));
        AnsiConsole.WriteLine();
        return (name, profile, region, labels);
    }

    private static async Task<(string, Labels, string)> SshPromptInteractiveAsync(string defaultName, bool defaultNameExists, string defaultSshKeyFileName, CancellationToken cancellationToken)
    {
        defaultName = defaultNameExists ? $"{defaultName}-{Utils.GenerateRandomString(5)}" : defaultName;
        var name = await AnsiConsole.PromptAsync(TextPrompts.NamePrompt(defaultName), cancellationToken);
        var labels = _labelsParsing(await AnsiConsole.PromptAsync(TextPrompts.LabelsPrompt, cancellationToken));
        // NOTE: only does a whitespace check for now, not legal filename checks yet like how the option does AcceptLegalFileNamesOnly()
        var sshKeyFileName = await AnsiConsole.PromptAsync(TextPrompts.SshKeyFileNamePrompt(defaultSshKeyFileName), cancellationToken);
        AnsiConsole.WriteLine();
        return (name, labels, sshKeyFileName);
    }

    private static async Task<Labels> EmailPromptInteractiveAsync(CancellationToken cancellationToken)
    {
        var labels = _labelsParsing(await AnsiConsole.PromptAsync(TextPrompts.LabelsPrompt, cancellationToken));
        AnsiConsole.WriteLine();
        return labels;
    }

    private static async Task<(string, Labels)> CookiePromptInteractiveAsync(string defaultName, bool defaultNameExists, CancellationToken cancellationToken)
    {
        defaultName = defaultNameExists ? $"{defaultName}-{Utils.GenerateRandomString(5)}" : defaultName;
        var name = await AnsiConsole.PromptAsync(TextPrompts.NamePrompt(defaultName), cancellationToken);
        var labels = _labelsParsing(await AnsiConsole.PromptAsync(TextPrompts.LabelsPrompt, cancellationToken));
        AnsiConsole.WriteLine();
        return (name, labels);
    }

    private static async Task<(string, Labels)> UsernamePasswordPromptInteractiveAsync(string defaultName, bool defaultNameExists, CancellationToken cancellationToken)
    {
        defaultName = defaultNameExists ? $"{defaultName}-{Utils.GenerateRandomString(5)}" : defaultName;
        var name = await AnsiConsole.PromptAsync(TextPrompts.NamePrompt(defaultName), cancellationToken);
        var labels = _labelsParsing(await AnsiConsole.PromptAsync(TextPrompts.LabelsPrompt, cancellationToken));
        AnsiConsole.WriteLine();
        return (name, labels);
    }
}
