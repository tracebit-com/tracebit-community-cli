using Tracebit.Cli.API.CanaryCredentials;

namespace Tracebit.Cli.Deploy;

public static class Deployer
{
    private static readonly string AwsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aws");
    private static readonly string AwsConfig = Path.Combine(AwsDir, "config");
    public static readonly string AwsCredentials = Path.Combine(AwsDir, "credentials");
    private static readonly string SshDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
    private static readonly string SshConfig = Path.Combine(SshDir, "config");

    public static string SshPrivateKeyFile(string path) => Path.Combine(SshDir, path);

    public static async Task WriteSshKeyToConfig(SshCanaryCredentials sshCanaryCredentials, string sshKeyFileName, CancellationToken cancellationToken)
    {
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

        // Remove existing host definition from config
        var (filteredLines, oldSshKeyFile) = RemoveHostEntry(configLines, sshCanaryCredentials.SshIp);

        // Find indentation style from config
        var indent = GetConfigIndentation(filteredLines);

        // The new host configuration
        List<string> hostConfig = [
            $"Host {sshCanaryCredentials.SshIp}",
            $"{indent}IdentityFile {sshPrivateKeyFile}",
            $"{indent}PasswordAuthentication no"
        ];

        filteredLines.AddRange(hostConfig);

        var oldSshPrivateKeyFile = oldSshKeyFile;
        var oldSshPublicKeyFile = oldSshKeyFile != null ? $"{oldSshPrivateKeyFile}.pub" : null;

        if (File.Exists(sshPrivateKeyFile) && sshPrivateKeyFile != oldSshPrivateKeyFile)
            throw new Exception($"SSH key {sshPrivateKeyFile} already exists");
        if (File.Exists(sshPublicKeyFile) && sshPublicKeyFile != oldSshPublicKeyFile)
            throw new Exception($"SSH key {sshPublicKeyFile} already exists");

        // Clean up old SSH key files. The old files are guaranteed to be a canary as they're for a Tracebit IP
        if (oldSshPrivateKeyFile != null)
        {
            DeleteSshKeyFiles(oldSshPrivateKeyFile);
        }

        var privateKeyBytes = Convert.FromBase64String(sshCanaryCredentials.SshPrivateKey);
        var publicKeyBytes = Convert.FromBase64String(sshCanaryCredentials.SshPublicKey);

        await File.WriteAllBytesAsync(sshPrivateKeyFile, privateKeyBytes, CancellationToken.None);
        await File.WriteAllBytesAsync(sshPublicKeyFile, publicKeyBytes, CancellationToken.None);

        // Set Unix file permissions
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(sshPrivateKeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(sshPublicKeyFile,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        await WriteLinesToFileAsync(sshConfigFileStream, filteredLines);
    }

    private static (List<string>, string?) RemoveHostEntry(List<string> lines, string targetHost)
    {
        var result = new List<string>();
        string? identityFile = null;
        var skip = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.TrimStart();

            // Split on any whitespace character
            var parts = trimmedLine.Split(null);
            if (parts.Length != 0 && parts[0].Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                // Start skipping if this is our target host (and only has one hostname)
                if (parts.Length == 2 && parts[1] == targetHost)
                {
                    skip = true;
                    continue;
                }

                // Stop skipping when we find any other Host line
                skip = false;
            }

            if (skip)
            {
                if (parts.Length >= 2 && parts[0].Equals("identityfile", StringComparison.OrdinalIgnoreCase))
                {
                    identityFile = parts[1];
                }
            }
            else
            {
                result.Add(line);
            }
        }

        return (result, identityFile);
    }

    private static string GetConfigIndentation(List<string> configLines)
    {
        foreach (var line in configLines)
        {
            if (line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                var indent = "";
                foreach (var ch in line)
                {
                    if (char.IsWhiteSpace(ch))
                        indent += ch;
                    else
                        break;
                }
                return indent;
            }
        }

        return "";
    }

    public static async Task WriteAwsKeyToConfigAsync(AwsCanaryCredentials awsCanaryCredentials, string profile, string region, CancellationToken cancellationToken)
    {
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

        // Remove existing profile definition from config
        var filteredConfigLines = RemoveAwsProfileFromLines(configLines, profile, hasProfilePrefix: true);

        // The new profile configuration
        List<string> profileConfig = [
            $"[profile {profile}]",
            $"region = {region}\n"
        ];

        filteredConfigLines.AddRange(profileConfig);

        await WriteLinesToFileAsync(awsConfigFileStream, filteredConfigLines);

        var credentialsLines = await ReadLinesFromFileAsync(awsCredentialsFileStream, cancellationToken);

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

    private static List<string> RemoveAwsProfileFromLines(List<string> lines, string targetProfile, bool hasProfilePrefix)
    {
        /*
         * This method is used to remove an AWS profile from both the config and credentials files.
         * Those 2 files have different section format, so we need to handle both cases.
         *
         * In config the format is:
         * [profile <ProfileName>]
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
         */
        var result = new List<string>();
        var skip = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith('#'))
            {
                // Always keep comments
                result.Add(line);
                continue;
            }

            if (!(trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']')))
            {
                // Not a profile line, so just add it to the result
                if (!skip)
                {
                    result.Add(line);
                }
                continue;
            }

            // Remove the brackets
            var sectionLine = trimmedLine.Substring(1, trimmedLine.Length - 2);

            // Split on any whitespace character
            var parts = sectionLine.Split(null);

            bool match;
            int indexForTarget;
            if (hasProfilePrefix)
            {
                match = parts.Length == 2 && parts[0].Equals("profile", StringComparison.OrdinalIgnoreCase);
                indexForTarget = 1;
            }
            else
            {
                match = parts.Length == 1;
                indexForTarget = 0;
            }

            if (match)
            {
                // This is a profile line
                // Start skipping if this is our target profile
                if (parts[indexForTarget] == targetProfile)
                {
                    skip = true;
                    continue;
                }

                // Stop skipping when we find any other profile line
                skip = false;
            }

            if (!skip)
            {
                result.Add(line);
            }
        }

        return result;
    }

    public static async Task RemoveAwsCredential(string profile, CancellationToken cancellationToken)
    {
        await RemoveCredentialFromAwsFiles(profile, cancellationToken);
    }

    public static async Task RemoveSshCredential(string sshIp, string sshKeyPath, CancellationToken cancellationToken)
    {
        await RemoveCredentialFromSshConfig(sshIp, cancellationToken);
        DeleteSshKeyFiles(sshKeyPath);
    }

    private static async Task RemoveCredentialFromAwsFiles(string profile, CancellationToken cancellationToken)
    {
        // NOTE: this will keep the file open to prevent other processes from writing to it. Open both files in the same time
        var awsConfigFileInfo = new FileInfo(AwsConfig);
        FileStream? awsConfigFileStream = null;
        try
        {
            awsConfigFileStream = awsConfigFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (FileNotFoundException) { }

        var awsCredentialsFileInfo = new FileInfo(AwsCredentials);
        FileStream? awsCredentialsFileStream = null;
        try
        {
            awsCredentialsFileStream = awsCredentialsFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (FileNotFoundException) { }

        try
        {
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

    private static async Task RemoveCredentialFromSshConfig(string sshIp, CancellationToken cancellationToken)
    {
        var sshConfigFileInfo = new FileInfo(SshConfig);
        FileStream sshConfigFileStream;
        try
        {
            // NOTE: this will keep the file open to prevent other processes from writing to it
            sshConfigFileStream = sshConfigFileInfo.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (FileNotFoundException)
        {
            // File not found, nothing to remove
            return;
        }

        await using (sshConfigFileStream)
        {
            var configLines = await ReadLinesFromFileAsync(sshConfigFileStream, cancellationToken);
            // Remove existing host definition from config
            var (filteredLines, _) = RemoveHostEntry(configLines, sshIp);
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

    private static async Task WriteLinesToFileAsync(FileStream fileStream, List<string> lines)
    {
        fileStream.Seek(0, SeekOrigin.Begin);
        await using var writer = new StreamWriter(fileStream);
        foreach (var line in lines)
        {
            await writer.WriteLineAsync(line);
        }
        await writer.FlushAsync(CancellationToken.None);
        fileStream.SetLength(fileStream.Position);
    }

    private static void DeleteSshKeyFiles(string sshCredentialPath)
    {
        File.Delete(sshCredentialPath);
        File.Delete($"{sshCredentialPath}.pub");
    }
}
