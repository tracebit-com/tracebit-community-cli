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
            "\tPasswordAuthentication no"
        ], lines);
    }

    [Fact]
    public async Task WriteSshKeyToConfig_RedeployWithNewKeyName_ReplacesHostAndDeletesOldKeys()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteSshKeyToConfig(Credentials(), "old-key", CancellationToken.None);

        await Deployer.WriteSshKeyToConfig(Credentials(), "new-key", CancellationToken.None);

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

        await Deployer.WriteSshKeyToConfig(refreshed, "canary-key", CancellationToken.None);

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

        var exception = await Assert.ThrowsAsync<Exception>(() => Deployer.WriteSshKeyToConfig(Credentials(), "id_rsa", CancellationToken.None));

        Assert.Contains("already exists", exception.Message);
        Assert.Equal("users real key", await File.ReadAllTextAsync(existingKey));
    }

    [Fact]
    public async Task WriteSshKeyToConfig_PublicKeyFileExists_Throws()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(SshDir);
        await File.WriteAllTextAsync(Path.Combine(SshDir, "canary-key.pub"), "existing public key");

        var exception = await Assert.ThrowsAsync<Exception>(() => Deployer.WriteSshKeyToConfig(Credentials(), "canary-key", CancellationToken.None));

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
}
