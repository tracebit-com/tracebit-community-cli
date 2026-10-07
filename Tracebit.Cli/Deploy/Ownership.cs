using System.Security.Cryptography;

namespace Tracebit.Cli.Deploy;

/// <summary>
/// Thrown when a deployment would replace or delete an SSH host entry, AWS profile or key file that this CLI did not
/// create, or whose current contents no longer match what the CLI last wrote.
/// </summary>
public class UnmanagedConfigEntryException(string message) : Exception(message);

/// <summary>
/// Describes what, if anything, the CLI already owns at the destination of an AWS profile deployment.
/// </summary>
/// <param name="AccessKeyId">
/// The access key ID the CLI last wrote to the profile. Null for credentials recorded before this was tracked, in
/// which case ownership is asserted by the state file alone and the contents cannot be verified.
/// </param>
public sealed record AwsProfileOwnership(string? AccessKeyId)
{
    /// <summary>A fresh deployment: the profile must not already exist in either AWS file.</summary>
    public static readonly AwsProfileOwnership None = new((string?)null) { IsOwned = false };

    public bool IsOwned { get; private init; } = true;

    public static AwsProfileOwnership Owned(string? accessKeyId) => new(accessKeyId);
}

/// <summary>
/// Describes what the CLI owns around an SSH key deployment. The Host block is shared with other canaries and possibly
/// with lines the user wrote, so ownership is only ever about this canary's own key file and IdentityFile line.
/// </summary>
/// <param name="KeyPath">The absolute private key path the CLI last wrote for this canary, or null for a fresh deployment.</param>
/// <param name="PrivateKeySha256">
/// Hex SHA-256 of the private key file bytes last written. Used only to confirm the file at <paramref name="KeyPath"/>
/// is still the one we wrote before deleting it. Null for credentials recorded before this was tracked.
/// </param>
public sealed record SshKeyOwnership(string? KeyPath, string? PrivateKeySha256)
{
    /// <summary>A fresh deployment: no key of ours exists yet for this canary.</summary>
    public static readonly SshKeyOwnership None = new(null, null);

    public bool IsOwned => KeyPath is not null;

    public static SshKeyOwnership Owned(string keyPath, string? privateKeySha256) => new(keyPath, privateKeySha256);

    public static string Fingerprint(byte[] keyBytes) => Convert.ToHexStringLower(SHA256.HashData(keyBytes));

    public static string FingerprintBase64(string keyBase64) => Fingerprint(Convert.FromBase64String(keyBase64));
}
