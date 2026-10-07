using System.Net;
using System.Text.RegularExpressions;

using Tracebit.Cli.API.CanaryCredentials;

namespace Tracebit.Cli.Deploy;

/// <summary>
/// Thrown when a value destined for an SSH or AWS configuration file is not in the expected format.
/// </summary>
public class InvalidDeploymentInputException(string message) : Exception(message);

/// <summary>
/// Validates every value before it is rendered into ~/.ssh/config, ~/.aws/config or ~/.aws/credentials.
///
/// Values reach the deployer from the command line, the state file and the Tracebit API (issued credentials and
/// metadata defaults). Command-line validators only cover the first of those, so this is the single boundary that
/// guarantees nothing written to a config file can introduce new directives, sections or paths.
/// </summary>
public static partial class DeploymentInputValidator
{
    // AWS: "Profile names can contain ... letters, numbers, hyphens, underscores". Also allow '.', '@' and '+' which
    // are commonly used (e.g. SSO profiles). Never allow whitespace, '[', ']', '#', ';' or '=' as they alter INI parsing.
    [GeneratedRegex(@"^[A-Za-z0-9_.@+\-]{1,128}\z")]
    private static partial Regex AwsProfileFormat();

    // e.g. us-east-1, eu-west-2, us-gov-west-1, cn-north-1, us-iso-east-1, us-isob-east-1
    [GeneratedRegex(@"^[a-z]{2}(-gov|-iso[a-z]?)?-[a-z]+-[0-9]{1,2}\z")]
    private static partial Regex AwsRegionFormat();

    // Access key IDs are alphanumeric; secret access keys and session tokens are base64-ish. Restrict all three to
    // printable ASCII without whitespace or INI metacharacters so a value can never span lines or be confused for a
    // key or section header. Format beyond that is the API's concern, not ours.
    [GeneratedRegex(@"^[A-Za-z0-9+/=_\-]{1,4096}\z")]
    private static partial Regex AwsCredentialValueFormat();

    public static void ValidateSsh(SshCanaryCredentials credentials, string sshKeyFileName)
    {
        ValidateSshIp(credentials.SshIp);
        ValidateSshKeyFileName(sshKeyFileName);
        ValidateBase64("SSH private key", credentials.SshPrivateKey);
        ValidateBase64("SSH public key", credentials.SshPublicKey);
    }

    public static void ValidateAws(AwsCanaryCredentials credentials, string profile, string region)
    {
        ValidateAwsProfile(profile);
        ValidateAwsRegion(region);
        if (!AwsCredentialValueFormat().IsMatch(credentials.AwsAccessKeyId))
            throw new InvalidDeploymentInputException("Issued AWS access key ID is not in the expected format");
        if (!AwsCredentialValueFormat().IsMatch(credentials.AwsSecretAccessKey))
            throw new InvalidDeploymentInputException("Issued AWS secret access key is not in the expected format");
        if (!AwsCredentialValueFormat().IsMatch(credentials.AwsSessionToken))
            throw new InvalidDeploymentInputException("Issued AWS session token is not in the expected format");
    }

    public static void ValidateSshIp(string sshIp)
    {
        // Require a literal IP address whose canonical form matches the input exactly. This rejects hostnames,
        // control characters, whitespace, zone IDs, ports and legacy shorthand like "10.1" or "0x0A.1.2.3".
        if (string.IsNullOrEmpty(sshIp)
            || !IPAddress.TryParse(sshIp, out var address)
            || !string.Equals(address.ToString(), sshIp, StringComparison.Ordinal))
            throw new InvalidDeploymentInputException("Issued SSH target is not a valid IP address");
    }

    // Files OpenSSH itself reads from ~/.ssh. Writing issued key bytes to any of these would change ssh/sshd behaviour.
    // Compared case-insensitively because macOS and Windows file systems are.
    private static readonly HashSet<string> ReservedSshFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "config",
        "authorized_keys",
        "authorized_keys2",
        "known_hosts",
        "known_hosts2",
        "rc",
        "environment",
    };

    public static void ValidateSshKeyFileName(string sshKeyFileName)
    {
        if (ReservedSshFileNames.Contains(sshKeyFileName))
            throw new InvalidDeploymentInputException($"Invalid SSH key file name: '{sshKeyFileName}' is a file OpenSSH reads from ~/.ssh");
        if (!Commands.Utils.IsValidSshKeyFileName(sshKeyFileName)
            || sshKeyFileName is "." or ".."
            || sshKeyFileName.Any(char.IsControl)
            || Path.IsPathRooted(sshKeyFileName)
            || !string.Equals(Path.GetFileName(sshKeyFileName), sshKeyFileName, StringComparison.Ordinal))
            throw new InvalidDeploymentInputException("Invalid SSH key file name: provide a file name (not a path) with no spaces or special characters");
    }

    public static void ValidateAwsProfile(string profile)
    {
        if (!AwsProfileFormat().IsMatch(profile))
            throw new InvalidDeploymentInputException("Invalid AWS profile name: use letters, numbers and - _ . @ + only");
        // According to official AWS docs: Do not use the word profile when creating an entry in the credentials file.
        if (profile.Equals("profile", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDeploymentInputException("AWS profile name cannot be 'profile'");
    }

    public static void ValidateAwsRegion(string region)
    {
        if (!AwsRegionFormat().IsMatch(region))
            throw new InvalidDeploymentInputException("Invalid AWS region");
    }

    private static void ValidateBase64(string what, string value)
    {
        if (string.IsNullOrEmpty(value) || !Convert.TryFromBase64String(value, new byte[(value.Length * 3 / 4) + 3], out _))
            throw new InvalidDeploymentInputException($"Issued {what} is not valid base64");
    }
}
