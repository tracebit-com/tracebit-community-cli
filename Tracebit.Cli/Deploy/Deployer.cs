using Tracebit.Cli.API.CanaryCredentials;

namespace Tracebit.Cli.Deploy;

public static class Deployer
{
    private static readonly AsyncLocal<string?> _homeDirectoryOverride = new();

    private static string HomeDirectory =>
        _homeDirectoryOverride.Value ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Overrides the home directory used for AWS and SSH files for the current async context. Used for testing.
    /// </summary>
    internal static IDisposable SetHomeDirectoryForTest(string path)
    {
        _homeDirectoryOverride.Value = path;
        return new HomeDirectoryScope();
    }

    private sealed class HomeDirectoryScope : IDisposable
    {
        public void Dispose() => _homeDirectoryOverride.Value = null;
    }

    private static string AwsDir => Path.Combine(HomeDirectory, ".aws");
    private static string AwsConfig => Path.Combine(AwsDir, "config");
    public static string AwsCredentials => Path.Combine(AwsDir, "credentials");
    private static string SshDir => Path.Combine(HomeDirectory, ".ssh");
    private static string SshConfig => Path.Combine(SshDir, "config");

    public static string SshPrivateKeyFile(string path) => Path.Combine(SshDir, path);

    private const string StagingSuffix = ".tracebit-new";

    public static Task WriteSshKeyToConfig(SshCanaryCredentials sshCanaryCredentials, string sshKeyFileName, CancellationToken cancellationToken) =>
        WriteSshKeyToConfig(sshCanaryCredentials, sshKeyFileName, SshKeyOwnership.None, cancellationToken);

    /// <param name="sshCanaryCredentials">The SSH canary credentials to write to <c>~/.ssh</c>.</param>
    /// <param name="sshKeyFileName">File name of the private key under <c>~/.ssh</c>.</param>
    /// <param name="ownership">
    /// What the CLI already owns for this host. With <see cref="SshKeyOwnership.None"/> any existing host entry or key
    /// file is treated as somebody else's and the deployment is refused. With an owned entry, only the recorded key
    /// path is deleted, and only if its public key still matches the recorded fingerprint.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteSshKeyToConfig(SshCanaryCredentials sshCanaryCredentials, string sshKeyFileName, SshKeyOwnership ownership, CancellationToken cancellationToken)
    {
        // Validate everything that ends up in ~/.ssh before touching the filesystem. Values may come from the API or
        // state file, which the command-line validators never see.
        DeploymentInputValidator.ValidateSsh(sshCanaryCredentials, sshKeyFileName);
        var privateKeyBytes = Convert.FromBase64String(sshCanaryCredentials.SshPrivateKey);
        var publicKeyBytes = Convert.FromBase64String(sshCanaryCredentials.SshPublicKey);

        var sshPrivateKeyFile = SshPrivateKeyFile(sshKeyFileName);
        var sshPublicKeyFile = $"{sshPrivateKeyFile}.pub";

        // Create the ssh config directory if it doesn't exist
        if (!Directory.Exists(SshDir))
        {
            Directory.CreateDirectory(SshDir);
            // Set Unix directory permissions (700)
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(SshDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        // Create ssh config if it doesn't exist
        if (!File.Exists(SshConfig))
        {
            await File.Create(SshConfig).DisposeAsync();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(SshConfig, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        var sshConfigFileInfo = new FileInfo(SshConfig);
        // NOTE: this will keep the file open to prevent other processes from writing to it
        await using var sshConfigFileStream = sshConfigFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var configLines = await ReadLinesFromFileAsync(sshConfigFileStream, cancellationToken);

        // Every SSH canary for an account shares one host IP, so a Host block may hold one IdentityFile per canary and
        // may also hold lines we did not write. We only ever add or swap our own IdentityFile line and leave every
        // other line, including IdentityFile lines for keys we do not own, exactly as we found them.
        var block = FindHostBlock(configLines, sshCanaryCredentials.SshIp);
        var existingIdentityFiles = block is null ? [] : IdentityFilesIn(configLines, block.Value);

        // The key this canary wrote last time, if any. It is rotated out below. Never inferred from the config alone.
        string? oldSshPrivateKeyFile = null;
        if (ownership.IsOwned && (existingIdentityFiles.Any(f => PathsEqual(f, ownership.KeyPath)) || File.Exists(ownership.KeyPath!)))
            oldSshPrivateKeyFile = ownership.KeyPath;
        var oldSshPublicKeyFile = oldSshPrivateKeyFile != null ? $"{oldSshPrivateKeyFile}.pub" : null;

        if (File.Exists(sshPrivateKeyFile) && !PathsEqual(sshPrivateKeyFile, oldSshPrivateKeyFile))
            throw new UnmanagedConfigEntryException($"SSH key {sshPrivateKeyFile} already exists and was not deployed by Tracebit");
        if (File.Exists(sshPublicKeyFile) && !PathsEqual(sshPublicKeyFile, oldSshPublicKeyFile))
            throw new UnmanagedConfigEntryException($"SSH key {sshPublicKeyFile} already exists and was not deployed by Tracebit");

        if (oldSshPrivateKeyFile != null)
            EnsureSshKeyFingerprintMatches(oldSshPrivateKeyFile, ownership.PrivateKeySha256);

        // Stage the new key pair under temporary names first so the old pair is only deleted once the new one is
        // durable on disk. A crash between the two steps leaves either the old pair or the staged pair, never neither.
        var stagedPrivateKeyFile = $"{sshPrivateKeyFile}{StagingSuffix}";
        var stagedPublicKeyFile = $"{sshPublicKeyFile}{StagingSuffix}";
        File.Delete(stagedPrivateKeyFile); // leftovers from an interrupted earlier run are ours by naming convention
        File.Delete(stagedPublicKeyFile);
        await CreateNewFileAsync(stagedPrivateKeyFile, privateKeyBytes, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await CreateNewFileAsync(stagedPublicKeyFile, publicKeyBytes,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        // Clean up the old key files (fingerprint verified above)
        if (oldSshPrivateKeyFile != null)
        {
            DeleteSshKeyFiles(oldSshPrivateKeyFile);
        }

        File.Move(stagedPrivateKeyFile, sshPrivateKeyFile);
        File.Move(stagedPublicKeyFile, sshPublicKeyFile);

        var filteredLines = UpsertIdentityFile(configLines, sshCanaryCredentials.SshIp, sshPrivateKeyFile, oldSshPrivateKeyFile);

        await WriteLinesToFileAsync(sshConfigFileStream, filteredLines);
    }

    /// <summary>
    /// Creates a file that must not already exist, with its final permissions set at creation time.
    /// FileMode.CreateNew maps to O_CREAT|O_EXCL, so this also refuses to write through a symlink (even a dangling
    /// one) planted at the path, and there is no window where the file exists with default umask permissions.
    /// </summary>
    private static async Task CreateNewFileAsync(string path, byte[] contents, UnixFileMode mode)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = mode;

        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(contents, CancellationToken.None);
    }

    /// <summary>
    /// Finds the block for <paramref name="targetHost"/>: a "Host" line with exactly that one pattern, up to but not
    /// including the next Host or Match line. Returns null if there is none.
    /// </summary>
    internal static (int Start, int End)? FindHostBlock(List<string> lines, string targetHost)
    {
        int? start = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var (keyword, args) = ParseSshConfigLine(lines[i]);
            var isBlockStart = keyword is not null &&
                (keyword.Equals("host", StringComparison.OrdinalIgnoreCase) || keyword.Equals("match", StringComparison.OrdinalIgnoreCase));
            if (!isBlockStart)
                continue;
            if (start is not null)
                return (start.Value, i);
            if (keyword!.Equals("host", StringComparison.OrdinalIgnoreCase) && args.Length == 1 && args[0] == targetHost)
                start = i;
        }
        return start is null ? null : (start.Value, lines.Count);
    }

    internal static List<string> IdentityFilesIn(List<string> lines, (int Start, int End) block)
    {
        var result = new List<string>();
        for (var i = block.Start + 1; i < block.End; i++)
        {
            var (keyword, args) = ParseSshConfigLine(lines[i]);
            if (keyword is not null && args.Length >= 1 && keyword.Equals("identityfile", StringComparison.OrdinalIgnoreCase))
                result.Add(args[0]);
        }
        return result;
    }

    /// <summary>
    /// Returns a copy of <paramref name="lines"/> where the block for <paramref name="host"/> lists
    /// <paramref name="newKeyPath"/>. An existing line for <paramref name="oldKeyPath"/> (or for the new path) is
    /// replaced in place; otherwise the new line goes after the block's last IdentityFile, or straight after the Host
    /// line. "IdentitiesOnly yes" and "PasswordAuthentication no" are added only if the block says nothing about them.
    /// Every other line in the block is left untouched. No block at all: a fresh one is appended.
    /// </summary>
    internal static List<string> UpsertIdentityFile(List<string> lines, string host, string newKeyPath, string? oldKeyPath)
    {
        var result = new List<string>(lines);
        var block = FindHostBlock(result, host);
        if (block is null)
        {
            result.AddRange(BuildHostEntry(host, [newKeyPath], GetConfigIndentation(lines)));
            return result;
        }

        var (start, end) = block.Value;
        var indent = GetBlockIndentation(result, block.Value) ?? GetConfigIndentation(lines);
        var newLine = $"{indent}IdentityFile {newKeyPath}";

        var ourLines = new List<int>();
        var lastIdentityFile = start;
        var hasIdentitiesOnly = false;
        var hasPasswordAuthentication = false;
        for (var i = start + 1; i < end; i++)
        {
            var (keyword, args) = ParseSshConfigLine(result[i]);
            if (keyword is null)
                continue;
            if (keyword.Equals("identityfile", StringComparison.OrdinalIgnoreCase))
            {
                lastIdentityFile = i;
                if (args.Length >= 1 && (PathsEqual(args[0], oldKeyPath) || PathsEqual(args[0], newKeyPath)))
                    ourLines.Add(i);
            }
            hasIdentitiesOnly |= keyword.Equals("identitiesonly", StringComparison.OrdinalIgnoreCase);
            hasPasswordAuthentication |= keyword.Equals("passwordauthentication", StringComparison.OrdinalIgnoreCase);
        }

        if (ourLines.Count > 0)
        {
            result[ourLines[0]] = newLine;
            // A block should only ever list our key once; drop any duplicates, from the bottom up
            for (var i = ourLines.Count - 1; i >= 1; i--)
            {
                result.RemoveAt(ourLines[i]);
                end--;
            }
        }
        else
        {
            result.Insert(lastIdentityFile + 1, newLine);
            end++;
        }

        if (!hasPasswordAuthentication)
            result.Insert(end, $"{indent}PasswordAuthentication no");
        if (!hasIdentitiesOnly)
            result.Insert(end, $"{indent}IdentitiesOnly yes");
        return result;
    }

    /// <summary>
    /// Returns a copy of <paramref name="lines"/> without the IdentityFile line for <paramref name="keyPath"/> in the
    /// block for <paramref name="host"/>. The block itself is removed only when nothing is left in it except lines we
    /// write ourselves (IdentitiesOnly, PasswordAuthentication), blanks and comments.
    /// </summary>
    internal static List<string> RemoveIdentityFile(List<string> lines, string host, string keyPath)
    {
        var result = new List<string>(lines);
        var block = FindHostBlock(result, host);
        if (block is null)
            return result;

        var (start, end) = block.Value;
        for (var i = end - 1; i > start; i--)
        {
            var (keyword, args) = ParseSshConfigLine(result[i]);
            if (keyword is not null && args.Length >= 1 && keyword.Equals("identityfile", StringComparison.OrdinalIgnoreCase) && PathsEqual(args[0], keyPath))
            {
                result.RemoveAt(i);
                end--;
            }
        }

        var onlyOurs = true;
        for (var i = start + 1; i < end && onlyOurs; i++)
        {
            var (keyword, _) = ParseSshConfigLine(result[i]);
            onlyOurs = keyword is null
                || keyword.Equals("identitiesonly", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("passwordauthentication", StringComparison.OrdinalIgnoreCase);
        }
        if (onlyOurs)
            result.RemoveRange(start, end - start);
        return result;
    }

    private static string? GetBlockIndentation(List<string> lines, (int Start, int End) block)
    {
        for (var i = block.Start + 1; i < block.End; i++)
        {
            if (lines[i].Length > 0 && char.IsWhiteSpace(lines[i][0]))
                return new string(lines[i].TakeWhile(char.IsWhiteSpace).ToArray());
        }
        return null;
    }

    internal static List<string> BuildHostEntry(string host, IEnumerable<string> identityFiles, string indent)
    {
        List<string> block = [$"Host {host}"];
        block.AddRange(identityFiles.Select(f => $"{indent}IdentityFile {f}"));
        // Only offer the canary keys to this host, never the user's agent or default keys
        block.Add($"{indent}IdentitiesOnly yes");
        block.Add($"{indent}PasswordAuthentication no");
        return block;
    }

    /// <summary>
    /// Splits an ssh_config line into keyword and arguments. Handles "Keyword value", "Keyword=value" and
    /// "Keyword = value", strips surrounding double quotes from arguments, and returns a null keyword for blank and
    /// comment lines.
    /// </summary>
    internal static (string? Keyword, string[] Args) ParseSshConfigLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            return (null, []);

        // Keyword ends at whitespace or '='
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]) && trimmed[end] != '=')
            end++;
        var keyword = trimmed[..end];
        var rest = trimmed[end..].TrimStart();
        if (rest.StartsWith('='))
            rest = rest[1..].TrimStart();

        var args = new List<string>();
        var i = 0;
        while (i < rest.Length)
        {
            if (char.IsWhiteSpace(rest[i]))
            { i++; continue; }
            if (rest[i] == '"')
            {
                var close = rest.IndexOf('"', i + 1);
                if (close < 0)
                    close = rest.Length;
                args.Add(rest[(i + 1)..close]);
                i = close + 1;
            }
            else
            {
                var start = i;
                while (i < rest.Length && !char.IsWhiteSpace(rest[i]))
                    i++;
                args.Add(rest[start..i]);
            }
        }

        return (keyword, args.ToArray());
    }

    internal static string GetConfigIndentation(List<string> configLines)
    {
        foreach (var line in configLines)
        {
            if (line.Length > 0 && char.IsWhiteSpace(line[0]))
                return new string(line.TakeWhile(char.IsWhiteSpace).ToArray());
        }

        return "";
    }

    public static Task WriteAwsKeyToConfigAsync(AwsCanaryCredentials awsCanaryCredentials, string profile, string region, CancellationToken cancellationToken) =>
        WriteAwsKeyToConfigAsync(awsCanaryCredentials, profile, region, AwsProfileOwnership.None, cancellationToken);

    /// <param name="awsCanaryCredentials">The AWS canary credentials to write to <c>~/.aws</c>.</param>
    /// <param name="profile">Name of the AWS profile to create or replace.</param>
    /// <param name="region">AWS region written to the profile.</param>
    /// <param name="ownership">
    /// What the CLI already owns at this profile. With <see cref="AwsProfileOwnership.None"/> an existing profile in
    /// either file is treated as somebody else's and the deployment is refused. With an owned profile, the existing
    /// access key ID must match the recorded one before it is replaced.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteAwsKeyToConfigAsync(AwsCanaryCredentials awsCanaryCredentials, string profile, string region, AwsProfileOwnership ownership, CancellationToken cancellationToken)
    {
        // Validate everything that ends up in ~/.aws before touching the filesystem. Values may come from the API or
        // state file, which the command-line validators never see.
        DeploymentInputValidator.ValidateAws(awsCanaryCredentials, profile, region);

        // Create the aws directory if it doesn't exist
        if (!Directory.Exists(AwsDir))
        {
            Directory.CreateDirectory(AwsDir);
            // Set Unix directory permissions (700)
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(AwsDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        // Create aws config if it doesn't exist
        if (!File.Exists(AwsConfig))
        {
            await File.Create(AwsConfig).DisposeAsync();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(AwsConfig, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        // Create aws credentials if it doesn't exist
        if (!File.Exists(AwsCredentials))
        {
            await File.Create(AwsCredentials).DisposeAsync();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(AwsCredentials, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        // NOTE: this will keep the file open to prevent other processes from writing to it. Open both files in the same time
        var awsConfigFileInfo = new FileInfo(AwsConfig);
        await using var awsConfigFileStream = awsConfigFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var awsCredentialsFileInfo = new FileInfo(AwsCredentials);
        await using var awsCredentialsFileStream = awsCredentialsFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var configLines = await ReadLinesFromFileAsync(awsConfigFileStream, cancellationToken);
        var credentialsLines = await ReadLinesFromFileAsync(awsCredentialsFileStream, cancellationToken);

        EnsureAwsProfileIsOurs(configLines, credentialsLines, profile, ownership);

        // Remove existing profile definition from config
        var filteredConfigLines = RemoveAwsProfileFromLines(configLines, profile, hasProfilePrefix: true);

        // The new profile configuration
        List<string> profileConfig = [
            $"[profile {profile}]",
            $"region = {region}\n"
        ];

        filteredConfigLines.AddRange(profileConfig);

        await WriteLinesToFileAsync(awsConfigFileStream, filteredConfigLines);

        // Remove existing profile definition from credentials
        var filteredCredentialsLines = RemoveAwsProfileFromLines(credentialsLines, profile, hasProfilePrefix: false);

        // The new credentials configuration
        List<string> profileCredentials = [
            $"[{profile}]",
            $"aws_access_key_id = {awsCanaryCredentials.AwsAccessKeyId}",
            $"aws_secret_access_key = {awsCanaryCredentials.AwsSecretAccessKey}",
            $"aws_session_token = {awsCanaryCredentials.AwsSessionToken}"
        ];

        filteredCredentialsLines.AddRange(profileCredentials);

        await WriteLinesToFileAsync(awsCredentialsFileStream, filteredCredentialsLines);
    }

    /// <summary>
    /// Pre-flight check for a fresh deployment: throws if the profile already exists in either AWS file. Run before
    /// asking the API to issue a canary so a refusal does not leave an issued but undeployed credential behind.
    /// </summary>
    public static async Task EnsureAwsProfileIsFreeAsync(string profile, CancellationToken cancellationToken)
    {
        DeploymentInputValidator.ValidateAwsProfile(profile);
        var configLines = await ReadLinesIfExistsAsync(AwsConfig, cancellationToken);
        var credentialsLines = await ReadLinesIfExistsAsync(AwsCredentials, cancellationToken);
        EnsureAwsProfileIsOurs(configLines, credentialsLines, profile, AwsProfileOwnership.None);
    }

    /// <summary>
    /// Pre-flight check for a fresh deployment: throws if the key file (or its .pub) already exists in ~/.ssh.
    /// The host entry cannot be checked here because the IP is only known once the canary is issued.
    /// </summary>
    public static void EnsureSshKeyFileIsFree(string sshKeyFileName)
    {
        DeploymentInputValidator.ValidateSshKeyFileName(sshKeyFileName);
        var privateKeyFile = SshPrivateKeyFile(sshKeyFileName);
        if (File.Exists(privateKeyFile))
            throw new UnmanagedConfigEntryException($"SSH key {privateKeyFile} already exists and was not deployed by Tracebit");
        if (File.Exists($"{privateKeyFile}.pub"))
            throw new UnmanagedConfigEntryException($"SSH key {privateKeyFile}.pub already exists and was not deployed by Tracebit");
    }

    private static async Task<List<string>> ReadLinesIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return [];
        return (await File.ReadAllLinesAsync(path, cancellationToken)).ToList();
    }

    private static void EnsureAwsProfileIsOurs(List<string> configLines, List<string> credentialsLines, string profile, AwsProfileOwnership ownership)
    {
        var inConfig = AwsProfileExists(configLines, profile, hasProfilePrefix: true);
        var existingAccessKeyId = FindAwsProfileAccessKeyId(credentialsLines, profile);
        var inCredentials = existingAccessKeyId != null || AwsProfileExists(credentialsLines, profile, hasProfilePrefix: false);

        if (!inConfig && !inCredentials)
            return;

        if (!ownership.IsOwned)
            throw new UnmanagedConfigEntryException(
                $"AWS profile '{profile}' already exists and was not deployed by Tracebit. Choose a different profile with --aws-profile");

        if (ownership.AccessKeyId != null && existingAccessKeyId != null && existingAccessKeyId != ownership.AccessKeyId)
            throw new UnmanagedConfigEntryException(
                $"AWS profile '{profile}' no longer holds the Tracebit canary key (found {existingAccessKeyId}). Refusing to replace it");
    }

    internal static bool AwsProfileExists(List<string> lines, string targetProfile, bool hasProfilePrefix) =>
        RemoveAwsProfileFromLines(lines, targetProfile, hasProfilePrefix).Count != lines.Count;

    /// <summary>Returns the aws_access_key_id inside the given credentials-file section, or null if absent.</summary>
    internal static string? FindAwsProfileAccessKeyId(List<string> credentialsLines, string targetProfile)
    {
        var inSection = false;
        foreach (var line in credentialsLines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                continue;
            var header = ParseAwsSectionHeader(trimmed);
            if (header is not null)
            {
                inSection = IsTargetAwsSection(header, targetProfile, hasProfilePrefix: false);
                continue;
            }
            if (!inSection)
                continue;
            var eq = trimmed.IndexOf('=');
            if (eq > 0 && trimmed[..eq].Trim().Equals("aws_access_key_id", StringComparison.OrdinalIgnoreCase))
                return StripIniComment(trimmed[(eq + 1)..]).Trim();
        }
        return null;
    }

    /// <summary>
    /// The private key is the canary and the only thing worth protecting, so it is the file we fingerprint before
    /// deleting it. The .pub next to it is a convenience copy and is replaced or removed along with it regardless.
    /// </summary>
    private static void EnsureSshKeyFingerprintMatches(string privateKeyPath, string? expectedPrivateKeySha256)
    {
        if (expectedPrivateKeySha256 == null)
            return; // Recorded before fingerprints were tracked; ownership rests on the state file alone
        if (!File.Exists(privateKeyPath))
            return; // Nothing to protect
        var actual = SshKeyOwnership.Fingerprint(File.ReadAllBytes(privateKeyPath));
        if (actual != expectedPrivateKeySha256)
            throw new UnmanagedConfigEntryException(
                $"SSH key {privateKeyPath} no longer matches the key Tracebit deployed. Refusing to delete it");
    }

    internal static bool PathsEqual(string? a, string? b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal);

    internal static List<string> RemoveAwsProfileFromLines(List<string> lines, string targetProfile, bool hasProfilePrefix)
    {
        /*
         * This method is used to remove an AWS profile from both the config and credentials files.
         * Those 2 files have different section format, so we need to handle both cases.
         *
         * In config the format is:
         * [profile <ProfileName>]      (or [default] for the default profile)
         * region = <Region>
         *
         * In credentials the format is:
         * [<ProfileName>]
         * aws_access_key_id = <AccessKeyId>
         * aws_secret_access_key = <SecretAccessKey>
         * aws_session_token = <SessionToken>
         *
         * Use the hasProfilePrefix parameter to determine which format to use.
         *
         * Any section header ends the section being removed, including ones we do not model such as
         * [sso-session x], [services x] or [default]. Headers may carry a trailing comment: "[profile x] # work".
         */
        var result = new List<string>();
        var skip = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith('#') || trimmedLine.StartsWith(';'))
            {
                // Always keep comments
                result.Add(line);
                continue;
            }

            var header = ParseAwsSectionHeader(trimmedLine);
            if (header is not null)
            {
                // Every header starts a new section, so it always ends any section being skipped
                skip = IsTargetAwsSection(header, targetProfile, hasProfilePrefix);
                if (skip)
                    continue;
            }

            if (!skip)
            {
                result.Add(line);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the whitespace-normalised name inside a section header such as "[profile  work] # comment", or null
    /// if the line is not a header.
    /// </summary>
    internal static string? ParseAwsSectionHeader(string trimmedLine)
    {
        if (!trimmedLine.StartsWith('['))
            return null;
        var close = trimmedLine.IndexOf(']');
        if (close < 0)
            return null;
        // Only whitespace and an optional comment may follow the closing bracket
        var after = trimmedLine[(close + 1)..].TrimStart();
        if (after.Length != 0 && !after.StartsWith('#') && !after.StartsWith(';'))
            return null;
        return string.Join(' ', trimmedLine[1..close].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsTargetAwsSection(string sectionName, string targetProfile, bool hasProfilePrefix)
    {
        if (hasProfilePrefix)
        {
            // In the config file the default profile is "[default]", every other profile is "[profile name]"
            return sectionName == $"profile {targetProfile}" || (targetProfile == "default" && sectionName == "default");
        }
        return sectionName == targetProfile;
    }

    private static string StripIniComment(string value)
    {
        var i = value.IndexOfAny(['#', ';']);
        return i < 0 ? value : value[..i];
    }

    public static Task RemoveAwsCredential(string profile, CancellationToken cancellationToken) =>
        RemoveAwsCredential(profile, ownedAccessKeyId: null, cancellationToken);

    /// <summary>
    /// Removes the profile from both AWS files. If <paramref name="ownedAccessKeyId"/> is given and the credentials
    /// file holds a different key under that profile, the files are left alone and an exception is thrown.
    /// </summary>
    public static async Task RemoveAwsCredential(string profile, string? ownedAccessKeyId, CancellationToken cancellationToken)
    {
        await RemoveCredentialFromAwsFiles(profile, ownedAccessKeyId, cancellationToken);
    }

    public static Task RemoveSshCredential(string sshIp, string sshKeyPath, CancellationToken cancellationToken) =>
        RemoveSshCredential(sshIp, sshKeyPath, ownedPrivateKeySha256: null, cancellationToken);

    /// <summary>
    /// Removes the host entry and deletes the key files. If <paramref name="ownedPrivateKeySha256"/> is given and the
    /// private key on disk no longer matches, the key files are left alone and an exception is thrown.
    /// </summary>
    public static async Task RemoveSshCredential(string sshIp, string sshKeyPath, string? ownedPrivateKeySha256, CancellationToken cancellationToken)
    {
        EnsureSshKeyFingerprintMatches(sshKeyPath, ownedPrivateKeySha256);
        await RemoveCredentialFromSshConfig(sshIp, sshKeyPath, cancellationToken);
        DeleteSshKeyFiles(sshKeyPath);
    }

    private static async Task RemoveCredentialFromAwsFiles(string profile, string? ownedAccessKeyId, CancellationToken cancellationToken)
    {
        // NOTE: this will keep the file open to prevent other processes from writing to it. Open both files in the same time
        var awsConfigFileInfo = new FileInfo(AwsConfig);
        FileStream? awsConfigFileStream = null;
        try
        {
            awsConfigFileStream = awsConfigFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException) { }

        var awsCredentialsFileInfo = new FileInfo(AwsCredentials);
        FileStream? awsCredentialsFileStream = null;
        try
        {
            awsCredentialsFileStream = awsCredentialsFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException) { }

        try
        {
            if (ownedAccessKeyId != null && awsCredentialsFileStream != null)
            {
                var existing = FindAwsProfileAccessKeyId(await ReadLinesFromFileAsync(awsCredentialsFileStream, cancellationToken), profile);
                if (existing != null && existing != ownedAccessKeyId)
                    throw new UnmanagedConfigEntryException(
                        $"AWS profile '{profile}' no longer holds the Tracebit canary key (found {existing}). Leaving it in place");
            }

            await RemoveCredentialFromAwsFileIfExists(profile, awsConfigFileStream, hasProfilePrefix: true, cancellationToken);
            await RemoveCredentialFromAwsFileIfExists(profile, awsCredentialsFileStream, hasProfilePrefix: false, cancellationToken);
        }
        finally
        {
            awsConfigFileStream?.Dispose();
            awsCredentialsFileStream?.Dispose();
        }
    }

    private static async Task RemoveCredentialFromAwsFileIfExists(string profile, FileStream? fileStream, bool hasProfilePrefix, CancellationToken cancellationToken)
    {
        if (fileStream == null)
        {
            // File not found, nothing to remove
            return;
        }

        var credentialsLines = await ReadLinesFromFileAsync(fileStream, cancellationToken);
        var filteredCredentialsLines = RemoveAwsProfileFromLines(credentialsLines, profile, hasProfilePrefix);
        await WriteLinesToFileAsync(fileStream, filteredCredentialsLines);
    }

    private static async Task RemoveCredentialFromSshConfig(string sshIp, string sshKeyPath, CancellationToken cancellationToken)
    {
        var sshConfigFileInfo = new FileInfo(SshConfig);
        FileStream sshConfigFileStream;
        try
        {
            // NOTE: this will keep the file open to prevent other processes from writing to it
            sshConfigFileStream = sshConfigFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // File not found, nothing to remove
            return;
        }

        await using (sshConfigFileStream)
        {
            var configLines = await ReadLinesFromFileAsync(sshConfigFileStream, cancellationToken);
            // Other canaries, or the user, may share this host block: only our own key line goes
            var filteredLines = RemoveIdentityFile(configLines, sshIp, sshKeyPath);
            if (filteredLines.Count != configLines.Count)
                await WriteLinesToFileAsync(sshConfigFileStream, filteredLines);
        }
    }

    private static async Task<List<string>> ReadLinesFromFileAsync(FileStream fileStream, CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        fileStream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(fileStream, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static Task WriteLinesToFileAsync(FileStream fileStream, List<string> lines) =>
        AtomicFileWriter.ReplaceContentsAsync(fileStream, async stream =>
        {
            await using var writer = new StreamWriter(stream, leaveOpen: true);
            foreach (var line in lines)
            {
                await writer.WriteLineAsync(line);
            }
            await writer.FlushAsync(CancellationToken.None);
        });

    private static void DeleteSshKeyFiles(string sshCredentialPath)
    {
        // File.Delete is a no-op for a missing file, but throws if the containing directory is gone
        if (!Directory.Exists(Path.GetDirectoryName(sshCredentialPath)))
            return;

        File.Delete(sshCredentialPath);
        File.Delete($"{sshCredentialPath}.pub");
    }
}
