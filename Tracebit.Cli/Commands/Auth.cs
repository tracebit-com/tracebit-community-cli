using System.CommandLine;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.IdentityModel.Tokens;

using Spectre.Console;

using Tracebit.Cli.API;
using Tracebit.Cli.Config;
using Tracebit.Cli.State;

namespace Tracebit.Cli.Commands;

static class Auth
{
    private static readonly Uri OAuthListenerBaseUri = new("http://localhost:5442");
    private const string OAuthCallbackPath = "/callback/";
    private static readonly Uri OAuthCallbackUri = new(OAuthListenerBaseUri, OAuthCallbackPath);
    private static readonly Uri OAuthSuccessUri = new(OAuthListenerBaseUri, "/success/");

    public static JwtSecurityTokenHandler TokenHandler = new();
    public static TokenValidationParameters TokenValidationParameters = new()
    {
        SignatureValidator = (t, _) => new JwtSecurityToken(t),
        ValidateIssuerSigningKey = false,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidIssuer = Constants.TracebitHost,
        ValidAudience = Constants.TracebitHost,
        ClockSkew = TimeSpan.Zero
    };

    private static readonly Option<bool> TokenOption = new("--token", "-t")
    {
        Description = "Use an API token to authenticate"
    };

    private static readonly Command BaseCommand = new("auth", "Authenticate with Tracebit") { TokenOption };

    public static Command Command(IServiceProvider services)
    {
        BaseCommand.Subcommands.Add(LoginCommand(services));
        BaseCommand.Subcommands.Add(StatusCommand());

        BaseCommand.SetAction(async (parsedResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            await LoginActionAsync(tracebitClient, parsedResult, cancellationToken);
        });
        return BaseCommand;
    }

    private static Command LoginCommand(IServiceProvider services)
    {
        Command command = new("login", "Login to Tracebit") { Hidden = true };
        command.SetAction(async (parsedResult, cancellationToken) =>
        {
            var tracebitClient = services.GetRequiredService<TracebitClient>();
            await LoginActionAsync(tracebitClient, parsedResult, cancellationToken);
        });
        return command;
    }

    private static Command StatusCommand()
    {
        Command command = new("status", "Check authentication status");
        command.SetAction((parseResult, cancellationToken) =>
        {
            var verbose = parseResult.GetValue(GlobalOptions.Verbose);
            return StatusActionAsync(parseResult, verbose, cancellationToken);
        });
        return command;
    }

    private static async Task LoginActionAsync(TracebitClient tracebitClient, ParseResult parseResult, CancellationToken cancellationToken)
    {
        var useToken = parseResult.GetValue(TokenOption);
        string token;
        if (useToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                token = await AnsiConsole.PromptAsync(new TextPrompt<string>("Enter your API token:")
                {
                    AllowEmpty = false,
                    IsSecret = true
                }, cancellationToken);

                var result = await AnsiConsole.Status().StartAsync("Validating API token", async _ =>
                {
                    return await TokenHandler.ValidateTokenAsync(token, TokenValidationParameters);
                });
                if (result.IsValid)
                    break;

                AnsiConsole.MarkupLineInterpolated($"[red]API token is not valid[/]");
            }
        }
        else
        {
            token = await FetchTokenWithBrowserAuthAsync(cancellationToken);
        }

        var credentialsFile = Credentials.File();
        credentialsFile.Directory?.Create();
        using var credentialsStream = credentialsFile.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        // Set secure permissions on Unix systems (0600 - only owner can read/write)
        if (!OperatingSystem.IsWindows() && credentialsFile.Exists)
        {
            File.SetUnixFileMode(credentialsFile.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Credentials? credentials;
        if (credentialsFile.Length == 0)
        {
            credentials = new();
        }
        else
        {
            credentials = await JsonSerializer.DeserializeAsync(credentialsStream, ConfigJsonSerializerContext.Default.Credentials, cancellationToken) ?? new();
        }

        credentials.Token = token;

        credentialsStream.Position = 0; // Jump to start of file before writing to overwrite content
        await JsonSerializer.SerializeAsync(credentialsStream, credentials, ConfigJsonSerializerContext.Default.Credentials, cancellationToken);
        credentialsStream.SetLength(credentialsStream.Position); // Truncate any non-overwritten bytes

        AnsiConsole.MarkupLine("[green]Successfully logged into Tracebit[/]");
        var state = await StateManager.GetStateAsync(cancellationToken);
        if (state.Credentials.Count == 0)
            AnsiConsole.MarkupLineInterpolated($"Get started by running [purple]{parseResult.RootCommandResult.IdentifierToken} {Deploy.BaseCommand.Name} {Deploy.DeployAllBaseCommand().Name}[/] to install all canary types");

        tracebitClient.SetAuthorizationHeader(credentials.Token);
        await tracebitClient.UpdateStatusAsync(Environment.MachineName, cancellationToken);
    }

    private static async Task StatusActionAsync(ParseResult parseResult, bool verbose, CancellationToken cancellationToken)
    {
        var credentials = await GetCredentialsAsync(parseResult, cancellationToken);

        if (verbose)
            AnsiConsole.MarkupLine("Found Tracebit API credentials");

        var result = await TokenHandler.ValidateTokenAsync(credentials.Token, TokenValidationParameters);
        if (!result.IsValid)
            throw new Exception($"API token is invalid", result.Exception);

        var expiryClaim = result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (expiryClaim is null || !int.TryParse(expiryClaim, out var expiryUnixTime))
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Expiry time of credentials is invalid or missing[/]");
        }
        else if (DateTimeOffset.FromUnixTimeSeconds(expiryUnixTime) < DateTime.UtcNow)
        {
            throw new MarkupException($"Credentials have expired, run [purple]{parseResult.RootCommandResult.IdentifierToken} {BaseCommand.Name}[/] to log back in");
        }
        else if (verbose)
        {
            AnsiConsole.WriteLine($"Credentials expire: {DateTimeOffset.FromUnixTimeSeconds(expiryUnixTime):u}");
        }

        AnsiConsole.MarkupLine("[green]Tracebit credentials are valid[/]");
    }

    // Get credentials that can be used to authenticate with the API.
    // Writes a warning message and returns null if credentials are not configured.
    // Note that this method does not check the credentials it returns are valid, so may return credentials that are invalid/expired.
    public static async Task<Credentials> GetCredentialsAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var credentialsFile = Credentials.File();
        credentialsFile.Directory?.Create();
        using var credentialsStream = credentialsFile.Open(FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
        Credentials? credentials;
        if (credentialsFile.Length == 0)
        {
            credentials = new();
        }
        else
        {
            credentials = await JsonSerializer.DeserializeAsync(credentialsStream, ConfigJsonSerializerContext.Default.Credentials, cancellationToken) ?? new();
        }

        if (string.IsNullOrEmpty(credentials.Token))
        {
            throw new MarkupException($"You are not yet logged into Tracebit, run [purple]{parseResult.RootCommandResult.IdentifierToken} {BaseCommand.Name}[/] to get started");
        }

        return credentials;
    }

    private static async Task<string> FetchTokenWithBrowserAuthAsync(CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(OAuthCallbackUri.ToString());
        listener.Prefixes.Add(OAuthSuccessUri.ToString());
        listener.Start();

        var cliLoginUrl = new Uri(Constants.TracebitUrl, "/cli-login");
        try
        {
            Process.Start(new ProcessStartInfo { FileName = cliLoginUrl.ToString(), UseShellExecute = true });
            AnsiConsole.MarkupLineInterpolated($"Opening {cliLoginUrl} in your browser");
        }
        catch
        {
            AnsiConsole.MarkupLineInterpolated($"Open {cliLoginUrl} in your browser on this device to continue");
        }

        string? token = null;
        while (token is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ctx = await listener.GetContextAsync().WaitAsync(cancellationToken);
            using var response = ctx.Response;
            token = ctx.Request.QueryString["token"];
            if (ctx.Request.Url is not null
                && ctx.Request.Url.AbsolutePath.StartsWith(OAuthCallbackPath)
                && !string.IsNullOrEmpty(token))
            {
                response.RedirectLocation = OAuthSuccessUri.ToString();
                response.StatusCode = (int)HttpStatusCode.Redirect;
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
            }
        }

        var longLivedToken = await ExchangeApiToken(token, cancellationToken);

        var successCtx = await listener.GetContextAsync().WaitAsync(cancellationToken);
        using var successResponse = successCtx.Response;
        successResponse.StatusCode = (int)HttpStatusCode.OK;
        const string successResponseText = """
            <!DOCTYPE html>
            <html>
            <head>
                <script>window.close()</script>
                <title>Tracebit CLI Login</title>
            </head>
            <body>
                <h1>Tracebit CLI Login Succeeded</h1>
                <p>You may now close this page</p>
            </body>
            </html>
            """;
        var successResponseBuffer = System.Text.Encoding.UTF8.GetBytes(successResponseText);
        await successResponse.OutputStream.WriteAsync(successResponseBuffer, cancellationToken);
        await successResponse.OutputStream.FlushAsync(cancellationToken);

        return longLivedToken;
    }

    private static async Task<string> ExchangeApiToken(string token, CancellationToken cancellationToken)
    {
        var response = await ApiTokens.ExchangeCliTokenAsync(token, cancellationToken);
        return response.Token;
    }
}
