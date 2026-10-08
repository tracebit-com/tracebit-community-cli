using System.Text.Json;

namespace Tracebit.Cli.E2E.Tests.Helpers;

/// <summary>
/// Runs the CLI against a throwaway home and config directory so tests never read or change the real user's
/// <c>~/.aws</c>, <c>~/.ssh</c>, <c>credentials.json</c> or <c>state.json</c>.
/// </summary>
/// <remarks>
/// <c>TRACEBIT_CONFIG_DIR</c> sandboxes the config directory on every platform. <c>HOME</c> only sandboxes
/// <c>~/.aws</c> and <c>~/.ssh</c> on Linux and macOS, so anything that deploys canaries must skip on Windows
/// (see <see cref="SandboxedFactAttribute"/>).
/// </remarks>
public sealed class IsolatedCliEnvironment : IAsyncDisposable
{
    public string HomeDirectory { get; }
    public string ConfigDirectory { get; }
    public string AwsCredentialsFile => Path.Combine(HomeDirectory, ".aws", "credentials");
    public string AwsConfigFile => Path.Combine(HomeDirectory, ".aws", "config");
    public string SshDirectory => Path.Combine(HomeDirectory, ".ssh");
    public string SshConfigFile => Path.Combine(SshDirectory, "config");
    public string StateFile => Path.Combine(ConfigDirectory, "state.json");
    private string CredentialsFile => Path.Combine(ConfigDirectory, "credentials.json");

    private readonly Dictionary<string, string?> _environment;

    public IsolatedCliEnvironment(string tracebitEnvironment = "stage")
    {
        HomeDirectory = Path.Combine(Path.GetTempPath(), "tracebit-e2e-" + Guid.NewGuid().ToString("N")[..12]);
        ConfigDirectory = Path.Combine(HomeDirectory, "tracebit-config");
        Directory.CreateDirectory(ConfigDirectory);

        _environment = new Dictionary<string, string?>
        {
            ["HOME"] = HomeDirectory,
            ["USERPROFILE"] = HomeDirectory,
            [Config.ConfigDirectory.ConfigDirVariable] = ConfigDirectory,
            ["TRACEBIT_ENVIRONMENT"] = tracebitEnvironment,
            // Do not inherit anything that would point the CLI somewhere else
            ["TRACEBIT_URL"] = null,
            ["XDG_DATA_HOME"] = null,
            ["XDG_CONFIG_HOME"] = null,
            // OpenSSH and the AWS CLI do not reliably follow HOME, so point `tracebit trigger` at the sandboxed files
            ["TRACEBIT_SSH_CONFIG"] = SshConfigFile,
            ["AWS_CONFIG_FILE"] = AwsConfigFile,
            ["AWS_SHARED_CREDENTIALS_FILE"] = AwsCredentialsFile,
            ["AWS_PROFILE"] = null,
            ["AWS_DEFAULT_PROFILE"] = null,
            ["AWS_ACCESS_KEY_ID"] = null,
            ["AWS_SECRET_ACCESS_KEY"] = null,
            ["AWS_SESSION_TOKEN"] = null,
            ["SSH_AUTH_SOCK"] = null,
            ["DOTNET_ENVIRONMENT"] = null,
        };
    }

    public void SetEnvironmentVariable(string name, string? value) => _environment[name] = value;

    public async Task WriteTokenAsync(string token)
    {
        await File.WriteAllTextAsync(CredentialsFile,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["token"] = token }));
    }

    public Task<CommandResult> RunAsync(string arguments) =>
        CliTestHelper.ExecuteAsync(arguments, HomeDirectory, _environment);

    public Task<CommandResult> RunSuccessAsync(string arguments) =>
        CliTestHelper.ExecuteSuccessAsync(arguments, HomeDirectory, _environment);

    /// <summary>
    /// The canaries recorded in <c>state.json</c>, or none if the CLI has not written any yet.
    /// </summary>
    public List<StateCredential> ReadState()
    {
        var content = File.Exists(StateFile) ? File.ReadAllText(StateFile) : "";
        if (string.IsNullOrWhiteSpace(content))
            return [];

        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("credentials").EnumerateArray()
            .Select(c => new StateCredential(
                TypeName: c.GetProperty("type").GetString()!,
                Name: c.GetProperty("name").GetString()!,
                Path: c.GetProperty("path").GetString()!,
                Target: GetOptionalString(c, "target"),
                AwsProfile: GetOptionalString(c, "awsProfile"),
                Labels: c.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
                    ? labels.EnumerateArray().ToDictionary(l => l.GetProperty("name").GetString()!, l => l.GetProperty("value").GetString()!)
                    : []))
            .ToList();
    }

    private static string? GetOptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Expire anything a test left behind so staging does not accumulate live canaries. Checked without parsing,
            // so a test that failed on unexpected state still cleans up
            if (File.Exists(StateFile) && new FileInfo(StateFile).Length > 0)
                await RunAsync("remove all --force");
        }
        finally
        {
            try
            {
                Directory.Delete(HomeDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

public sealed record StateCredential(
    string TypeName,
    string Name,
    string Path,
    string? Target,
    string? AwsProfile,
    Dictionary<string, string> Labels);
