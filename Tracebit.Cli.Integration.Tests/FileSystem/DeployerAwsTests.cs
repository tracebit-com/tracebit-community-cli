using Tracebit.Cli.API.CanaryCredentials;
using Tracebit.Cli.Deploy;
using Tracebit.Cli.Integration.Tests.Helpers;
using Tracebit.Cli.Tests.Helpers;

namespace Tracebit.Cli.Integration.Tests.FileSystem;

/// <summary>
/// Tests for AWS deployment against an isolated home directory
/// </summary>
public class DeployerAwsTests : BaseIntegrationTest
{
    private string AwsDir => Path.Combine(TempDirectory, ".aws");
    private string AwsConfig => Path.Combine(AwsDir, "config");
    private string AwsCredentials => Path.Combine(AwsDir, "credentials");

    private static AwsCanaryCredentials Credentials(string suffix = "") =>
        TestDataBuilder.BuildAwsCredentials(accessKeyId: $"AKIACANARY{suffix}", secretAccessKey: $"secret{suffix}", sessionToken: $"session{suffix}");

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_NoAwsDirectory_CreatesConfigAndCredentials()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);

        await Deployer.WriteAwsKeyToConfigAsync(Credentials(), "canary", "eu-west-1", CancellationToken.None);

        Assert.Equal(Path.Combine(AwsDir, "credentials"), Deployer.AwsCredentials);
        var config = await File.ReadAllTextAsync(AwsConfig);
        Assert.Contains("[profile canary]", config);
        Assert.Contains("region = eu-west-1", config);

        var credentials = await File.ReadAllTextAsync(AwsCredentials);
        Assert.Contains("[canary]", credentials);
        Assert.Contains("aws_access_key_id = AKIACANARY", credentials);
        Assert.Contains("aws_secret_access_key = secret", credentials);
        Assert.Contains("aws_session_token = session", credentials);
    }

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_OnUnix_SetsRestrictivePermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);

        await Deployer.WriteAwsKeyToConfigAsync(Credentials(), "canary", "eu-west-1", CancellationToken.None);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(AwsDir));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(AwsConfig));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(AwsCredentials));
    }

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_ExistingProfiles_ArePreserved()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(AwsDir);
        await File.WriteAllTextAsync(AwsConfig, "[default]\nregion = us-east-1\noutput = json\n");
        await File.WriteAllTextAsync(AwsCredentials, "[default]\naws_access_key_id = AKIAREAL\naws_secret_access_key = realsecret\n");

        await Deployer.WriteAwsKeyToConfigAsync(Credentials(), "canary", "eu-west-1", CancellationToken.None);

        var config = await File.ReadAllTextAsync(AwsConfig);
        Assert.StartsWith("[default]\nregion = us-east-1\noutput = json\n", config.ReplaceLineEndings("\n"));
        Assert.Contains("[profile canary]", config);

        var credentials = await File.ReadAllTextAsync(AwsCredentials);
        Assert.StartsWith("[default]\naws_access_key_id = AKIAREAL\naws_secret_access_key = realsecret\n", credentials.ReplaceLineEndings("\n"));
        Assert.Contains("[canary]", credentials);
    }

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_Redeploy_ReplacesProfileInsteadOfDuplicating()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteAwsKeyToConfigAsync(Credentials("1"), "canary", "eu-west-1", CancellationToken.None);

        await Deployer.WriteAwsKeyToConfigAsync(Credentials("2"), "canary", "us-east-2", AwsProfileOwnership.Owned("AKIACANARY1"), CancellationToken.None);

        var config = await File.ReadAllTextAsync(AwsConfig);
        Assert.Equal(1, config.Split("[profile canary]").Length - 1);
        Assert.Contains("region = us-east-2", config);
        Assert.DoesNotContain("eu-west-1", config);

        var credentials = await File.ReadAllTextAsync(AwsCredentials);
        Assert.Equal(1, credentials.Split("[canary]").Length - 1);
        Assert.Contains("AKIACANARY2", credentials);
        Assert.DoesNotContain("AKIACANARY1", credentials);
    }

    [Fact]
    public async Task RemoveAwsCredential_RemovesProfileFromBothFiles()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(AwsDir);
        await File.WriteAllTextAsync(AwsConfig, "[default]\nregion = us-east-1\n");
        await File.WriteAllTextAsync(AwsCredentials, "[default]\naws_access_key_id = AKIAREAL\n");
        await Deployer.WriteAwsKeyToConfigAsync(Credentials(), "canary", "eu-west-1", CancellationToken.None);

        await Deployer.RemoveAwsCredential("canary", CancellationToken.None);

        var config = await File.ReadAllTextAsync(AwsConfig);
        Assert.DoesNotContain("canary", config);
        Assert.Contains("[default]", config);

        var credentials = await File.ReadAllTextAsync(AwsCredentials);
        Assert.DoesNotContain("canary", credentials);
        Assert.DoesNotContain("AKIACANARY", credentials);
        Assert.Contains("AKIAREAL", credentials);
    }

    [Fact]
    public async Task RemoveAwsCredential_FilesMissing_DoesNotThrow()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);

        await Deployer.RemoveAwsCredential("canary", CancellationToken.None);

        Assert.False(Directory.Exists(AwsDir));
    }

    [Fact]
    public async Task RemoveAwsCredential_OnlyCredentialsFileExists_RemovesFromIt()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(AwsDir);
        await File.WriteAllTextAsync(AwsCredentials, "[canary]\naws_access_key_id = AKIACANARY\n[default]\naws_access_key_id = AKIAREAL\n");

        await Deployer.RemoveAwsCredential("canary", CancellationToken.None);

        var credentials = await File.ReadAllTextAsync(AwsCredentials);
        Assert.DoesNotContain("AKIACANARY", credentials);
        Assert.Contains("AKIAREAL", credentials);
        Assert.False(File.Exists(AwsConfig));
    }

    [Theory]
    [InlineData("canary]\n[default", "eu-west-1")]
    [InlineData("canary", "eu-west-1\ncredential_process = /tmp/evil")]
    public async Task WriteAwsKeyToConfigAsync_InjectedProfileOrRegion_ThrowsAndWritesNothing(string profile, string region)
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(AwsDir);
        await File.WriteAllTextAsync(AwsConfig, "[default]\nregion = us-east-1\n");

        await Assert.ThrowsAsync<InvalidDeploymentInputException>(() =>
            Deployer.WriteAwsKeyToConfigAsync(Credentials(), profile, region, CancellationToken.None));

        Assert.Equal("[default]\nregion = us-east-1\n", (await File.ReadAllTextAsync(AwsConfig)).ReplaceLineEndings("\n"));
        Assert.False(File.Exists(AwsCredentials));
    }

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_InjectedSessionToken_ThrowsAndWritesNothing()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        var credentials = TestDataBuilder.BuildAwsCredentials(sessionToken: "session\n[profile other]\nregion = us-east-1");

        await Assert.ThrowsAsync<InvalidDeploymentInputException>(() =>
            Deployer.WriteAwsKeyToConfigAsync(credentials, "canary", "eu-west-1", CancellationToken.None));

        Assert.False(Directory.Exists(AwsDir));
    }

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_FreshDeployOverUnmanagedProfile_ThrowsAndLeavesFilesUntouched()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(AwsDir);
        const string config = "[profile canary]\nregion = us-east-1\n";
        const string credentials = "[canary]\naws_access_key_id = AKIAREAL\naws_secret_access_key = realsecret\n";
        await File.WriteAllTextAsync(AwsConfig, config);
        await File.WriteAllTextAsync(AwsCredentials, credentials);

        await Assert.ThrowsAsync<UnmanagedConfigEntryException>(() =>
            Deployer.WriteAwsKeyToConfigAsync(Credentials(), "canary", "eu-west-1", CancellationToken.None));

        Assert.Equal(config, (await File.ReadAllTextAsync(AwsConfig)).ReplaceLineEndings("\n"));
        Assert.Equal(credentials, (await File.ReadAllTextAsync(AwsCredentials)).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_FreshDeployOverProfileOnlyInCredentials_Throws()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(AwsDir);
        await File.WriteAllTextAsync(AwsCredentials, "[canary]\naws_access_key_id = AKIAREAL\n");

        await Assert.ThrowsAsync<UnmanagedConfigEntryException>(() =>
            Deployer.WriteAwsKeyToConfigAsync(Credentials(), "canary", "eu-west-1", CancellationToken.None));

        Assert.Contains("AKIAREAL", await File.ReadAllTextAsync(AwsCredentials));
    }

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_RefreshButProfileHoldsDifferentKey_ThrowsAndLeavesIt()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteAwsKeyToConfigAsync(Credentials("1"), "canary", "eu-west-1", CancellationToken.None);
        // User reused our profile name for a real key
        await File.WriteAllTextAsync(AwsCredentials, "[canary]\naws_access_key_id = AKIAREAL\naws_secret_access_key = realsecret\n");

        await Assert.ThrowsAsync<UnmanagedConfigEntryException>(() =>
            Deployer.WriteAwsKeyToConfigAsync(Credentials("2"), "canary", "eu-west-1", AwsProfileOwnership.Owned("AKIACANARY1"), CancellationToken.None));

        Assert.Contains("AKIAREAL", await File.ReadAllTextAsync(AwsCredentials));
        Assert.DoesNotContain("AKIACANARY2", await File.ReadAllTextAsync(AwsCredentials));
    }

    [Fact]
    public async Task WriteAwsKeyToConfigAsync_RefreshWithLegacyStateWithoutKeyId_Replaces()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteAwsKeyToConfigAsync(Credentials("1"), "canary", "eu-west-1", CancellationToken.None);

        await Deployer.WriteAwsKeyToConfigAsync(Credentials("2"), "canary", "eu-west-1", AwsProfileOwnership.Owned(null), CancellationToken.None);

        Assert.Contains("AKIACANARY2", await File.ReadAllTextAsync(AwsCredentials));
    }

    [Fact]
    public async Task RemoveAwsCredential_ProfileHoldsDifferentKey_ThrowsAndLeavesIt()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        Directory.CreateDirectory(AwsDir);
        await File.WriteAllTextAsync(AwsConfig, "[profile canary]\nregion = us-east-1\n");
        await File.WriteAllTextAsync(AwsCredentials, "[canary]\naws_access_key_id = AKIAREAL\n");

        await Assert.ThrowsAsync<UnmanagedConfigEntryException>(() =>
            Deployer.RemoveAwsCredential("canary", "AKIACANARY", CancellationToken.None));

        Assert.Contains("[profile canary]", await File.ReadAllTextAsync(AwsConfig));
        Assert.Contains("AKIAREAL", await File.ReadAllTextAsync(AwsCredentials));
    }

    [Fact]
    public async Task RemoveAwsCredential_ProfileHoldsOurKey_Removes()
    {
        using var _ = Deployer.SetHomeDirectoryForTest(TempDirectory);
        await Deployer.WriteAwsKeyToConfigAsync(Credentials(), "canary", "eu-west-1", CancellationToken.None);

        await Deployer.RemoveAwsCredential("canary", "AKIACANARY", CancellationToken.None);

        Assert.DoesNotContain("canary", await File.ReadAllTextAsync(AwsConfig));
        Assert.DoesNotContain("canary", await File.ReadAllTextAsync(AwsCredentials));
    }
}
