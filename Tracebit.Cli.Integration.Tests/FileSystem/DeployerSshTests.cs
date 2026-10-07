using Tracebit.Cli.API.CanaryCredentials;
using Tracebit.Cli.Deploy;
using Tracebit.Cli.Integration.Tests.Helpers;
using Tracebit.Cli.Tests.Helpers;

namespace Tracebit.Cli.Integration.Tests.FileSystem;

/// <summary>
/// Tests for SSH deployment against an isolated home directory
/// </summary>
public class DeployerSshTests : BaseIntegrationTest
{
    private string SshDir => Path.Combine(TempDirectory, ".ssh");
    private string SshConfig => Path.Combine(SshDir, "config");

    private static SshCanaryCredentials Credentials(string ip = "10.1.2.3") =>
        TestDataBuilder.BuildSshCredentials(ip: ip, privateKey: Base64("PRIVATE"), publicKey: Base64("PUBLIC"));

    private static string Base64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task WriteSshKeyToConfig_NoSshDirectory_CreatesDirectoryConfigAndKeys()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);

        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);

        Assert.True(Directory.Exists(SshDir));
        var config = await File.ReadAllTextAsync(SshConfig);
        Assert.Contains("Host 10.1.2.3", config);
        Assert.Contains($"IdentityFile {Path.Combine(SshDir, "canary-key")}", config);
        Assert.Contains("IdentitiesOnly yes", config);
        Assert.Contains("PasswordAuthentication no", config);

        Assert.Equal("PRIVATE", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key")));
        Assert.Equal("PUBLIC", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key.pub")));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_OnUnix_SetsRestrictivePermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);

        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(SshDir));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SshConfig));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(SshDir, "canary-key")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            File.GetUnixFileMode(Path.Combine(SshDir, "canary-key.pub")));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_ExistingConfig_PreservesOtherHostsAndIndentation()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        FileSystemHelper.CreateSshConfigFile(SshConfig, "Host github.com\n\tUser git\n\tIdentityFile ~/.ssh/id_ed25519\n");

        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(SshConfig);
        Assert.Equal(
        [
            "Host github.com",
            "\tUser git",
            "\tIdentityFile ~/.ssh/id_ed25519",
            "Host 10.1.2.3",
            $"\tIdentityFile {Path.Combine(SshDir, "canary-key")}",
            "\tIdentitiesOnly yes",
            "\tPasswordAuthentication no"
        ], lines);
    }

    [Fact]
    public async Task WriteSshKeyToConfig_RedeployWithNewKeyName_ReplacesHostAndDeletesOldKeys()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "old-key", CancellationToken.None);
        var owned = SshKeyOwnership.Owned(Path.Combine(SshDir, "old-key"), SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")));

        await Deployer.WriteSshKeyToConfig(Credentials(), "new-key", owned, CancellationToken.None);

        var config = await File.ReadAllTextAsync(SshConfig);
        Assert.Equal(1, config.Split("Host 10.1.2.3").Length - 1);
        Assert.Contains("new-key", config);
        Assert.DoesNotContain("old-key", config);
        Assert.False(File.Exists(Path.Combine(SshDir, "old-key")));
        Assert.False(File.Exists(Path.Combine(SshDir, "old-key.pub")));
        Assert.True(File.Exists(Path.Combine(SshDir, "new-key")));
        Assert.True(File.Exists(Path.Combine(SshDir, "new-key.pub")));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_RedeployWithSameKeyName_OverwritesKeyContents()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);
        var refreshed = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: Base64("PRIVATE2"), publicKey: Base64("PUBLIC2"));
        var owned = SshKeyOwnership.Owned(Path.Combine(SshDir, "canary-key"), SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")));

        await Deployer.WriteSshKeyToConfig(refreshed, "canary-key", owned, CancellationToken.None);

        Assert.Equal("PRIVATE2", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key")));
        Assert.Equal("PUBLIC2", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key.pub")));
        var config = await File.ReadAllTextAsync(SshConfig);
        Assert.Equal(1, config.Split("Host 10.1.2.3").Length - 1);
    }

    [Fact]
    public async Task WriteSshKeyToConfig_KeyFileBelongsToAnotherHost_ThrowsAndLeavesFilesUntouched()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(SshDir);
        var existingKey = Path.Combine(SshDir, "id_rsa");
        await File.WriteAllTextAsync(existingKey, "users real key");

        var exception = await Assert.ThrowsAsync<UnmanagedConfigEntryException>(() => Deployer.WriteSshKeyToConfig(Credentials(), "id_rsa", CancellationToken.None));

        Assert.Contains("already exists", exception.Message);
        Assert.Equal("users real key", await File.ReadAllTextAsync(existingKey));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_PublicKeyFileExists_Throws()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(SshDir);
        await File.WriteAllTextAsync(Path.Combine(SshDir, "canary-key.pub"), "existing public key");

        var exception = await Assert.ThrowsAsync<UnmanagedConfigEntryException>(() => Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None));

        Assert.Contains("canary-key.pub", exception.Message);
        Assert.False(File.Exists(Path.Combine(SshDir, "canary-key")));
    }

    [Fact]
    public async Task RemoveSshCredential_RemovesHostEntryAndKeyFiles()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        FileSystemHelper.CreateSshConfigFile(SshConfig, "Host github.com\n  User git\n");
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);
        var keyPath = Path.Combine(SshDir, "canary-key");

        await Deployer.RemoveSshCredential("10.1.2.3", keyPath, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(SshConfig);
        Assert.Equal(["Host github.com", "  User git"], lines);
        Assert.False(File.Exists(keyPath));
        Assert.False(File.Exists($"{keyPath}.pub"));
    }

    [Fact]
    public async Task RemoveSshCredential_NoConfigFile_DoesNotThrow()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);

        await Deployer.RemoveSshCredential("10.1.2.3", Path.Combine(SshDir, "missing-key"), CancellationToken.None);

        Assert.False(File.Exists(SshConfig));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_MultipleCanaries_AllPresentInConfig()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);

        await Deployer.WriteSshKeyToConfig(Credentials("10.0.0.1"), "key-1", CancellationToken.None);
        await Deployer.WriteSshKeyToConfig(Credentials("10.0.0.2"), "key-2", CancellationToken.None);

        var config = await File.ReadAllTextAsync(SshConfig);
        Assert.Contains("Host 10.0.0.1", config);
        Assert.Contains("Host 10.0.0.2", config);
        Assert.True(File.Exists(Path.Combine(SshDir, "key-1")));
        Assert.True(File.Exists(Path.Combine(SshDir, "key-2")));
    }

    [Theory]
    [InlineData("10.1.2.3\nMatch exec \"touch /tmp/pwned\"")]
    [InlineData("evil.example.com")]
    public async Task WriteSshKeyToConfig_InjectedSshIp_ThrowsAndWritesNothing(string ip)
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        FileSystemHelper.CreateSshConfigFile(SshConfig, "Host github.com\n\tUser git\n");

        await Assert.ThrowsAsync<InvalidDeploymentInputException>(() =>
            Deployer.WriteSshKeyToConfig(Credentials(ip), "canary-key", CancellationToken.None));

        Assert.Equal("Host github.com\n\tUser git\n", (await File.ReadAllTextAsync(SshConfig)).ReplaceLineEndings("\n"));
        Assert.False(File.Exists(Path.Combine(SshDir, "canary-key")));
    }

    [Theory]
    [InlineData("../escaped-key")]
    [InlineData("key\nIdentityFile /tmp/x")]
    public async Task WriteSshKeyToConfig_KeyFileNameFromUntrustedSource_ThrowsAndWritesNothing(string fileName)
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);

        await Assert.ThrowsAsync<InvalidDeploymentInputException>(() =>
            Deployer.WriteSshKeyToConfig(Credentials(), fileName, CancellationToken.None));

        Assert.False(Directory.Exists(SshDir));
        Assert.False(File.Exists(Path.Combine(TempDirectory, "escaped-key")));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_InvalidKeyMaterial_DoesNotDeleteExistingKey()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);

        var refreshed = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: "not base64!", publicKey: Base64("PUBLIC"));
        await Assert.ThrowsAsync<InvalidDeploymentInputException>(() =>
            Deployer.WriteSshKeyToConfig(refreshed, "canary-key", CancellationToken.None));

        Assert.Equal("PRIVATE", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key")));
        Assert.Contains("Host 10.1.2.3", await File.ReadAllTextAsync(SshConfig));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_DanglingSymlinkAtKeyPath_ThrowsAndDoesNotFollowIt()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(SshDir);
        var target = Path.Combine(TempDirectory, "planted-target");
        File.CreateSymbolicLink(Path.Combine(SshDir, "canary-key"), target);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None));

        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_FreshDeployOverUsersHostEntry_AddsOurLineAndLeavesTheirsAlone()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        var usersKey = Path.Combine(SshDir, "id_work");
        FileSystemHelper.CreateSshConfigFile(SshConfig, $"Host 10.1.2.3\n\tUser me\n\tIdentityFile {usersKey}\n\tProxyJump bastion\n");
        await File.WriteAllTextAsync(usersKey, "users real key");

        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);

        Assert.Equal(
        [
            "Host 10.1.2.3",
            "\tUser me",
            $"\tIdentityFile {usersKey}",
            $"\tIdentityFile {Path.Combine(SshDir, "canary-key")}",
            "\tProxyJump bastion",
            "\tIdentitiesOnly yes",
            "\tPasswordAuthentication no"
        ], await File.ReadAllLinesAsync(SshConfig));
        Assert.Equal("users real key", await File.ReadAllTextAsync(usersKey));
        Assert.Equal("PRIVATE", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key")));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_RefreshWhenHostAlsoListsAnotherKey_RotatesOursAndLeavesTheirs()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);
        var usersKey = Path.Combine(SshDir, "id_work");
        await File.WriteAllTextAsync(usersKey, "users real key");
        // User added their own key to our block
        var config = await File.ReadAllTextAsync(SshConfig);
        await File.WriteAllTextAsync(SshConfig, config.Replace("IdentitiesOnly yes", $"IdentityFile {usersKey}\nIdentitiesOnly yes"));
        var rotated = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: Base64("PRIVATE2"), publicKey: Base64("PUBLIC2"));
        var owned = SshKeyOwnership.Owned(Path.Combine(SshDir, "canary-key"), SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")));

        await Deployer.WriteSshKeyToConfig(rotated, "canary-key", owned, CancellationToken.None);

        Assert.Equal("PRIVATE2", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key")));
        Assert.Equal("users real key", await File.ReadAllTextAsync(usersKey));
        var lines = await File.ReadAllLinesAsync(SshConfig);
        Assert.Single(lines, l => l.Contains("Host 10.1.2.3"));
        Assert.Single(lines, l => l.Contains($"IdentityFile {usersKey}"));
        Assert.Single(lines, l => l.Contains($"IdentityFile {Path.Combine(SshDir, "canary-key")}"));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_RefreshButKeyFingerprintChanged_ThrowsAndDoesNotDeleteIt()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);
        // Somebody put a real private key at our path
        await File.WriteAllTextAsync(Path.Combine(SshDir, "canary-key"), "somebody elses private key");
        var owned = SshKeyOwnership.Owned(Path.Combine(SshDir, "canary-key"), SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")));

        await Assert.ThrowsAsync<UnmanagedConfigEntryException>(() =>
            Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", owned, CancellationToken.None));

        Assert.Equal("somebody elses private key", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key")));
        Assert.Equal("PUBLIC", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key.pub")));
    }

    [Fact]
    public async Task RemoveSshCredential_KeyFingerprintChanged_ThrowsAndLeavesKey()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);
        var keyPath = Path.Combine(SshDir, "canary-key");
        await File.WriteAllTextAsync(keyPath, "somebody elses private key");

        await Assert.ThrowsAsync<UnmanagedConfigEntryException>(() =>
            Deployer.RemoveSshCredential("10.1.2.3", keyPath, SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")), CancellationToken.None));

        Assert.True(File.Exists(keyPath));
        Assert.Contains("Host 10.1.2.3", await File.ReadAllTextAsync(SshConfig));
    }

    [Fact]
    public async Task RemoveSshCredential_KeyFingerprintMatches_DeletesKey()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);
        var keyPath = Path.Combine(SshDir, "canary-key");

        await Deployer.RemoveSshCredential("10.1.2.3", keyPath, SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")), CancellationToken.None);

        Assert.False(File.Exists(keyPath));
        Assert.False(File.Exists($"{keyPath}.pub"));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_StagedKeysFromInterruptedRun_AreReplacedAndCleanedUp()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(SshDir);
        await File.WriteAllTextAsync(Path.Combine(SshDir, "canary-key.tracebit-new"), "stale");
        await File.WriteAllTextAsync(Path.Combine(SshDir, "canary-key.pub.tracebit-new"), "stale");

        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);

        Assert.Equal("PRIVATE", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key")));
        Assert.Empty(Directory.GetFiles(SshDir, "*.tracebit-new"));
        Assert.Empty(Directory.GetFiles(SshDir, "*.tracebit-tmp"));
    }

    // Every SSH canary for an account shares one host IP, so one Host block carries one IdentityFile per canary

    [Fact]
    public async Task WriteSshKeyToConfig_SecondCanarySameHost_AppendsKeyLineKeepsFirstKey()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        FileSystemHelper.CreateSshConfigFile(SshConfig, "Host github.com\n\tUser git\n");
        await Deployer.WriteSshKeyToConfig(Credentials(), "key-a", CancellationToken.None);
        var second = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: Base64("PRIVATE-B"), publicKey: Base64("PUBLIC-B"));

        await Deployer.WriteSshKeyToConfig(second, "key-b", SshKeyOwnership.None, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(SshConfig);
        Assert.Equal(
        [
            "Host github.com",
            "\tUser git",
            "Host 10.1.2.3",
            $"\tIdentityFile {Path.Combine(SshDir, "key-a")}",
            $"\tIdentityFile {Path.Combine(SshDir, "key-b")}",
            "\tIdentitiesOnly yes",
            "\tPasswordAuthentication no"
        ], lines);
        Assert.Equal("PRIVATE", await File.ReadAllTextAsync(Path.Combine(SshDir, "key-a")));
        Assert.Equal("PRIVATE-B", await File.ReadAllTextAsync(Path.Combine(SshDir, "key-b")));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_RefreshOneOfTwoCanaries_RotatesOnlyItsKey()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        var keyA = Path.Combine(SshDir, "key-a");
        var keyB = Path.Combine(SshDir, "key-b");
        await Deployer.WriteSshKeyToConfig(Credentials(), "key-a", CancellationToken.None);
        var second = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: Base64("PRIVATE-B"), publicKey: Base64("PUBLIC-B"));
        await Deployer.WriteSshKeyToConfig(second, "key-b", SshKeyOwnership.None, CancellationToken.None);

        var rotated = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: Base64("PRIVATE-A2"), publicKey: Base64("PUBLIC-A2"));
        var ownership = SshKeyOwnership.Owned(keyA, SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")));
        await Deployer.WriteSshKeyToConfig(rotated, "key-a", ownership, CancellationToken.None);

        Assert.Equal("PRIVATE-A2", await File.ReadAllTextAsync(keyA));
        Assert.Equal("PRIVATE-B", await File.ReadAllTextAsync(keyB));
        var config = await File.ReadAllTextAsync(SshConfig);
        Assert.Equal(1, config.Split("Host 10.1.2.3").Length - 1);
        Assert.Equal(2, config.Split("IdentityFile ").Length - 1);
        Assert.Contains($"IdentityFile {keyA}", config);
        Assert.Contains($"IdentityFile {keyB}", config);
    }

    [Fact]
    public async Task RemoveSshCredential_OneOfTwoCanaries_DropsOnlyItsLineAndKey()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        var keyA = Path.Combine(SshDir, "key-a");
        var keyB = Path.Combine(SshDir, "key-b");
        FileSystemHelper.CreateSshConfigFile(SshConfig, "Host github.com\n\tUser git\n");
        await Deployer.WriteSshKeyToConfig(Credentials(), "key-a", CancellationToken.None);
        var second = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: Base64("PRIVATE-B"), publicKey: Base64("PUBLIC-B"));
        await Deployer.WriteSshKeyToConfig(second, "key-b", SshKeyOwnership.None, CancellationToken.None);

        await Deployer.RemoveSshCredential("10.1.2.3", keyA, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(SshConfig);
        Assert.Equal(["Host github.com", "\tUser git", "Host 10.1.2.3", $"\tIdentityFile {keyB}", "\tIdentitiesOnly yes", "\tPasswordAuthentication no"], lines);
        Assert.False(File.Exists(keyA));
        Assert.True(File.Exists(keyB));

        await Deployer.RemoveSshCredential("10.1.2.3", keyB, CancellationToken.None);

        Assert.Equal(["Host github.com", "\tUser git"], await File.ReadAllLinesAsync(SshConfig));
        Assert.False(File.Exists(keyB));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_ExistingBlock_IsRebuiltInPlaceNotMovedToEnd()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        FileSystemHelper.CreateSshConfigFile(SshConfig, "Host first\n\tUser a\nHost 10.1.2.3\n\tIdentityFile " + Path.Combine(SshDir, "key-a") + "\nHost last\n\tUser z\n");
        await File.WriteAllTextAsync(Path.Combine(SshDir, "key-a"), "existing");

        await Deployer.WriteSshKeyToConfig(Credentials(), "key-b", SshKeyOwnership.None, CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(SshConfig);
        Assert.Equal("Host first", lines[0]);
        Assert.Equal("Host 10.1.2.3", lines[2]);
        Assert.Equal("Host last", lines[^2]);
    }

    [Fact]
    public async Task WriteSshKeyToConfig_RefreshWithEditedPublicKey_StillRotates()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);
        // The .pub is a convenience copy; a user regenerating or annotating it must not block rotation
        await File.WriteAllTextAsync(Path.Combine(SshDir, "canary-key.pub"), "PUBLIC with a comment");
        var rotated = TestDataBuilder.BuildSshCredentials(ip: "10.1.2.3", privateKey: Base64("PRIVATE2"), publicKey: Base64("PUBLIC2"));
        var owned = SshKeyOwnership.Owned(Path.Combine(SshDir, "canary-key"), SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")));

        await Deployer.WriteSshKeyToConfig(rotated, "canary-key", owned, CancellationToken.None);

        Assert.Equal("PRIVATE2", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key")));
        Assert.Equal("PUBLIC2", await File.ReadAllTextAsync(Path.Combine(SshDir, "canary-key.pub")));
    }

    [Fact]
    public async Task RemoveSshCredential_PublicKeyMissing_StillRemoves()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None);
        var keyPath = Path.Combine(SshDir, "canary-key");
        File.Delete($"{keyPath}.pub");

        await Deployer.RemoveSshCredential("10.1.2.3", keyPath, SshKeyOwnership.FingerprintBase64(Base64("PRIVATE")), CancellationToken.None);

        Assert.False(File.Exists(keyPath));
    }
}
