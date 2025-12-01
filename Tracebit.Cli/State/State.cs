using System.CommandLine;
using System.Text.Json;

using Tracebit.Cli.API;
using Tracebit.Cli.API.CanaryCredentials;
using Tracebit.Cli.Commands;
using Tracebit.Cli.Deploy;

using Labels = System.Collections.Generic.List<Tracebit.Cli.API.Label>;

namespace Tracebit.Cli.State;

public class State
{
    public List<CredentialData> Credentials { get; set; } = [];
}

public static class StateManager
{
    private const int FileLockMaxAttempts = 20;
    private static readonly TimeSpan FileLockRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly AsyncLocal<string?> _stateFileOverride = new();

    private static FileInfo StateFile => GetStateFile();

    private static FileInfo GetStateFile()
    {
        var overridePath = _stateFileOverride.Value;
        if (!string.IsNullOrEmpty(overridePath))
        {
            return new FileInfo(overridePath);
        }

        return new FileInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Program.AppName,
            "state.json"));
    }

    /// <summary>
    /// Sets the state file path for the current async context. Used for testing.
    /// </summary>
    internal static IDisposable SetStateFileForTest(string path)
    {
        _stateFileOverride.Value = path;
        return new StateFileScope();
    }

    private sealed class StateFileScope : IDisposable
    {
        public void Dispose() => _stateFileOverride.Value = null;
    }

    public static List<CredentialData> Credentials => GetState().Credentials.ToList();
    private static List<T> Find<T>(string name) where T : CredentialData => GetState().Credentials.OfType<T>().Where(c => c.Name == name).ToList();
    public static void RemoveCredential<T>(T credential) where T : CredentialData => UpdateState(state => state.Credentials.RemoveAll(c => c.TypeName == credential.TypeName && c.Name == credential.Name));

    public static List<string> FilterNotExistingCanaryTypesForName(string name, HashSet<string> allTypes) =>
        allTypes.Except(GetState().Credentials
                // The Email canary is stored with the hardcoded name instead, take that into account
                .Where(c => c.Name == name || c.Name == Constants.EmailCanaryName)
                .Select(c => c.TypeName))
                .ToList();

    public static List<SshCredentialData> SshCredentials => GetState().Credentials
        .OfType<SshCredentialData>()
        .ToList();

    public static List<AwsCredentialData> AwsCredentials => GetState().Credentials
        .OfType<AwsCredentialData>()
        .ToList();

    public static List<GitlabCookieCredentialData> BrowserCookieCredentials => GetState().Credentials
        .OfType<GitlabCookieCredentialData>()
        .ToList();

    public static List<GitlabUsernamePasswordCredentialData> UsernamePasswordCanaries => GetState().Credentials
        .OfType<GitlabUsernamePasswordCredentialData>()
        .ToList();

    public static List<EmailCredentialData> EmailCanaries => GetState().Credentials
        .OfType<EmailCredentialData>()
        .ToList();

    public static bool CheckIfCredentialExists<T>(string name) where T : CredentialData
    {
        return Find<T>(name).FirstOrDefault() is not null;
    }

    public static void EnsureCredentialDoesNotExist<T>(string name, ParseResult parseResult, string nameOption) where T : CredentialData
    {
        var existingCanary = Find<T>(name).FirstOrDefault();
        if (existingCanary is not null)
        {
            throw new CredentialAlreadyExistsException(parseResult, existingCanary, nameOption);
        }
    }

    public static void EnsureEmailCredentialDoesNotExist(string name, ParseResult parseResult)
    {
        var existingCanary = Find<EmailCredentialData>(name).FirstOrDefault();
        if (existingCanary is not null)
            throw new EmailCredentialAlreadyExistsException(parseResult, existingCanary);
    }

    private static State GetState()
    {
        return GetStateAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    // For read-only commands
    public static async Task<State> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return await ReadStateFromDiskAsync(cancellationToken).ConfigureAwait(false);
    }

    // for commands that write (opens file as read-write, holding lock throughout)
    private static async Task<TResult> UpdateStateAsync<TResult>(Func<State, Task<TResult>> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        await using var stream = await OpenStateFileAsync(FileAccess.ReadWrite, FileShare.None).ConfigureAwait(false);
        var state = await DeserializeStateAsync(stream).ConfigureAwait(false);
        var result = await updateAction(state).ConfigureAwait(false);
        await SerializeStateAsync(stream, state).ConfigureAwait(false);
        return result;
    }

    // NOTE: intentionally not providing it a cancellation token as writes to state shouldn't be stoppable
    private static T UpdateState<T>(Func<State, T> updateFunc)
    {
        return UpdateStateAsync(state =>
        {
            var result = updateFunc(state);
            return Task.FromResult(result);
        }).GetAwaiter().GetResult();
    }

    // Methods for adding credentials to state file
    public static AwsCredentialData AddAwsCredential(string name, Labels? labels, AwsCanaryCredentials awsCredentials, string profile, string region)
    {
        return UpdateState(state =>
        {
            var credential = new AwsCredentialData
            {
                Name = name,
                CreatedAt = DateTime.Now,
                ExpiresAt = awsCredentials.AwsExpiration,
                Path = Deployer.AwsCredentials,
                Labels = labels,
                AwsProfile = profile,
                AwsRegion = region
            };
            AddOrReplaceCredential(state, credential);
            return credential;
        });
    }

    public static SshCredentialData AddSshCredential(string name, Labels? labels, SshCanaryCredentials sshCredentials, string sshKeyFileName)
    {
        return UpdateState(state =>
        {
            var credential = new SshCredentialData
            {
                Name = name,
                CreatedAt = DateTime.Now,
                ExpiresAt = sshCredentials.SshExpiration,
                Path = Deployer.SshPrivateKeyFile(sshKeyFileName),
                Labels = labels,
                Target = sshCredentials.SshIp
            };
            AddOrReplaceCredential(state, credential);
            return credential;
        });
    }

    public static GitlabCookieCredentialData AddBrowserCookieCredential(string name, Labels? labels, HttpCanaryCredentials httpCredentials)
    {
        return UpdateState(state =>
        {
            var credential = new GitlabCookieCredentialData
            {
                Name = name,
                CreatedAt = DateTime.Now,
                ExpiresAt = httpCredentials.ExpiresAt,
                Labels = labels,
                Path = "",
                Target = httpCredentials.HostNames.FirstOrDefault()
            };
            AddOrReplaceCredential(state, credential);
            return credential;
        });
    }

    public static EmailCredentialData AddEmailCredential(string name, Labels? labels, GenerateAndSendCanaryEmailResponse emailResponse)
    {
        return UpdateState(state =>
        {
            var credential = new EmailCredentialData
            {
                Name = name,
                CreatedAt = DateTime.Now,
                Path = "",
                ExpiresAt = emailResponse.CredentialExpiresAt,
                Labels = labels,
                Target = emailResponse.EmailTo,
                EmailFrom = emailResponse.EmailFrom,
                EmailSubject = emailResponse.EmailSubject,
                CredentialTriggerableAfter = emailResponse.CredentialTriggerableAfter,
            };
            AddOrReplaceCredential(state, credential);
            return credential;
        });
    }

    public static GitlabUsernamePasswordCredentialData AddUsernamePasswordCredential(string name, Labels? labels, HttpCanaryCredentials httpCredentials)
    {
        return UpdateState(state =>
        {
            var credential = new GitlabUsernamePasswordCredentialData
            {
                Name = name,
                CreatedAt = DateTime.Now,
                ExpiresAt = httpCredentials.ExpiresAt,
                Labels = labels,
                Path = "",
                Target = httpCredentials.HostNames.FirstOrDefault()
            };
            AddOrReplaceCredential(state, credential);
            return credential;
        });
    }

    private static void AddOrReplaceCredential<T>(State state, T credential) where T : CredentialData
    {
        for (var i = 0; i < state.Credentials.Count; i++)
        {
            if (state.Credentials[i].Name != credential.Name || state.Credentials[i] is not T)
                continue;
            state.Credentials[i] = credential;
            return;
        }

        state.Credentials.Add(credential);
    }

    // Methods for reading/writing state file
    private static async Task<State> ReadStateFromDiskAsync(CancellationToken cancellationToken)
    {
        await using var stream = await OpenStateFileAsync(FileAccess.Read, FileShare.Read, cancellationToken).ConfigureAwait(false);
        return await DeserializeStateAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FileStream> OpenStateFileAsync(FileAccess access, FileShare share, CancellationToken cancellationToken = default)
    {
        StateFile.Directory?.Create();

        IOException? lastError = null;
        for (var attempt = 0; attempt < FileLockMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(StateFile.FullName, FileMode.OpenOrCreate, access, share);
            }
            catch (IOException ex)
            {
                lastError = ex;
                if (attempt == FileLockMaxAttempts - 1)
                {
                    break;
                }

                await Task.Delay(FileLockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"Failed to access state file at {StateFile.FullName}", lastError);
    }

    // Methods for serializing/deserializing state file
    private static async Task<State> DeserializeStateAsync(FileStream stream, CancellationToken cancellationToken = default)
    {
        if (stream.Length == 0)
        {
            return new State();
        }

        stream.Seek(0, SeekOrigin.Begin);
        try
        {
            return await JsonSerializer
                .DeserializeAsync(stream, StateJsonSerializerContext.Default.State, cancellationToken)
                .ConfigureAwait(false) ?? new State();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to load state from {StateFile.FullName}", ex);
        }
    }

    private static async Task SerializeStateAsync(FileStream stream, State state)
    {
        stream.Seek(0, SeekOrigin.Begin);
        await JsonSerializer.SerializeAsync(stream, state, StateJsonSerializerContext.Default.State).ConfigureAwait(false);
        stream.SetLength(stream.Position);
        await stream.FlushAsync().ConfigureAwait(false);
    }
}

