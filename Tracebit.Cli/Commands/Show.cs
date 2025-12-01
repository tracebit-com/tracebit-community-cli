using System.CommandLine;
using System.Diagnostics.CodeAnalysis;

using Tracebit.Cli.State;

namespace Tracebit.Cli.Commands;

static class Show
{
    public static Command Command()
    {
        var command = new Command("show", "Show canaries deployed via the CLI");
        command.Subcommands.Add(ShowAllCommand());
        command.Subcommands.Add(ShowTypeCommand<AwsCredentialData>("aws"));
        command.Subcommands.Add(ShowTypeCommand<SshCredentialData>("ssh"));
        command.Subcommands.Add(ShowTypeCommand<GitlabCookieCredentialData>("cookie"));
        command.Subcommands.Add(ShowTypeCommand<GitlabUsernamePasswordCredentialData>("username-password"));
        command.Subcommands.Add(ShowTypeCommand<EmailCredentialData>("email"));

        command.SetAction(ShowAllCredentialsAction);
        return command;
    }

    private static Command ShowAllCommand()
    {
        var command = new Command("all", "Show all canaries");
        command.SetAction(ShowAllCredentialsAction);
        return command;
    }

    private static Command ShowTypeCommand<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string name) where T : CredentialData, IPrettyNamedType
    {
        var command = new Command(name, $"Show {T.PrettyTypeName} canaries");
        command.SetAction(ShowCredentialsAction<T>);
        return command;
    }

    private static int ShowAllCredentialsAction(ParseResult parseResult)
    {
        var credentials = StateManager.Credentials.ToList();

        if (credentials.Count == 0)
        {
            Utils.PrintNoCanaryCredentialsFoundMessage(parseResult);
            return 0;
        }

        Utils.ShowTable(credentials);

        return 0;
    }

    private static int ShowCredentialsAction<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(ParseResult parseResult) where T : CredentialData, IPrettyNamedType
    {
        var credentials = StateManager.Credentials.OfType<T>().ToList();

        if (credentials.Count == 0)
        {
            Utils.PrintNoCanaryCredentialsOfTypeFoundMessage<T>(parseResult);
            return 0;
        }

        Utils.ShowTable(credentials);

        return 0;
    }
}
