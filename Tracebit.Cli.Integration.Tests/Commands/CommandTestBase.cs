using System.CommandLine;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;
using Spectre.Console.Testing;

using Tracebit.Cli.API;
using Tracebit.Cli.API.CanaryCredentials;
using Tracebit.Cli.Commands;
using Tracebit.Cli.Deploy;
using Tracebit.Cli.Integration.Tests.Helpers;
using Tracebit.Cli.State;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tracebit.Cli.Integration.Tests.Commands;

/// <summary>
/// Command tests swap the global AnsiConsole and share statically built commands, so they must not run in parallel
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CommandCollection : ICollectionFixture<CommandFixture>
{
    public const string Name = "Commands";
}

public sealed class CommandFixture : IDisposable
{
    public WireMockServer Api { get; } = WireMockServer.Start();
    public RootCommand Root { get; }

    private readonly ServiceProvider _services;

    public CommandFixture()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TracebitClient(new HttpClient { BaseAddress = new Uri(Api.Url!) }));
        _services = services.BuildServiceProvider();

        // Remove.Command and Trigger.Command mutate static commands, so they must only be built once per test run
        Root = Program.RootCommand();
        Root.Subcommands.Add(Remove.Command(_services));
        Root.Subcommands.Add(Trigger.Command());
    }

    public void Dispose()
    {
        _services.Dispose();
        Api.Stop();
        Api.Dispose();
    }
}

public abstract class CommandTestBase : BaseIntegrationTest
{
    private const string RemovePath = "/_internal/v1/cli/remove";

    private readonly CommandFixture _fixture;
    private readonly IAnsiConsole _originalConsole;

    protected TestConsole Console { get; }
    protected string StateFile => Path.Combine(TempDirectory, "state.json");

    protected CommandTestBase(CommandFixture fixture)
    {
        _fixture = fixture;
        _fixture.Api.Reset();

        _originalConsole = AnsiConsole.Console;
        Console = new TestConsole().Interactive();
        Console.Profile.Width = 500;
        AnsiConsole.Console = Console;
    }

    /// <summary>
    /// Points state and home directory at the test's temp directory for the current async context
    /// </summary>
    protected IDisposable Isolate() => new Scope(StateManager.SetStateFileForTest(StateFile), Deployer.SetHomeDirectoryForTest(TempDirectory));

    protected async Task<int> RunAsync(string commandLine)
    {
        var parseResult = _fixture.Root.Parse(commandLine);
        Assert.Empty(parseResult.Errors);
        parseResult.InvocationConfiguration.EnableDefaultExceptionHandler = false;
        return await parseResult.InvokeAsync(null, CancellationToken.None);
    }

    protected void SetupRemoveResponse(int statusCode = 200)
    {
        _fixture.Api
            .Given(Request.Create().WithPath(RemovePath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(statusCode));
    }

    protected List<(string Name, string Type)> RemoveRequests() => _fixture.Api
        .FindLogEntries(Request.Create().WithPath(RemovePath).UsingPost())
        .Select(e => JsonDocument.Parse(e.RequestMessage.Body!).RootElement)
        .Select(b => (b.GetProperty("name").GetString()!, b.GetProperty("type").GetString()!))
        .ToList();

    protected static HttpCanaryCredentials HttpCredentials(string host = "gitlab.example.com") => new()
    {
        ConfirmationId = Guid.NewGuid(),
        BrowserDeploymentId = Guid.NewGuid(),
        HostNames = [host],
        ExpiresAt = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Credentials = JsonSerializer.SerializeToElement(new { })
    };

    protected static GenerateAndSendCanaryEmailResponse EmailResponse(DateTime? triggerableAfter = null) => new()
    {
        EmailFrom = "alerts@example.com",
        EmailTo = "me@example.com",
        EmailSubject = "Your backup codes",
        CredentialExpiresAt = null,
        CredentialTriggerableAfter = triggerableAfter
    };

    public override void Dispose()
    {
        AnsiConsole.Console = _originalConsole;
        Console.Dispose();
        base.Dispose();
    }

    private sealed class Scope(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var scope in scopes)
                scope.Dispose();
        }
    }
}
