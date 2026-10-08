using Tracebit.Cli.E2E.Tests.Helpers;

namespace Tracebit.Cli.E2E.Tests.Staging;

/// <summary>
/// Deploys real canaries on community.stage.tracebit.com with the token in <c>TRACEBIT_STAGING_API_TOKEN</c>, then
/// checks what the CLI wrote to the sandboxed <c>~/.aws</c>, <c>~/.ssh</c> and state file. Every canary is removed again,
/// and each test uses a unique name so parallel CI jobs on the same tenant do not collide.
/// </summary>
/// <remarks>
/// Assertions on file contents use <c>Assert.True</c> with a short message on purpose: <c>Assert.Contains</c> would print
/// the whole file, canary keys included, into the CI log on failure.
/// </remarks>
public class StagingDeployTests
{
    private static string UniqueName() => "e2e-" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<IsolatedCliEnvironment> LoggedInEnvironmentAsync()
    {
        var env = new IsolatedCliEnvironment(tracebitEnvironment: "stage");
        await env.WriteTokenAsync(StagingFactAttribute.Token!);
        return env;
    }

    private static string ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    private static void AssertOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [StagingFact]
    public async Task AuthStatus_StagingToken_IsValid()
    {
        await using var env = await LoggedInEnvironmentAsync();

        var result = await env.RunAsync("auth status");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Tracebit credentials are valid", result.StandardOutput);
    }

    [StagingFact]
    public async Task DeployAws_WritesProfileAndState_RemoveCleansUp()
    {
        await using var env = await LoggedInEnvironmentAsync();
        var name = UniqueName();

        var deploy = await env.RunSuccessAsync($"deploy aws --name {name} --labels suite=e2e,run={name}");

        Assert.Contains($"Credential {name} (AWS) deployed", deploy.StandardOutput);
        var credential = Assert.Single(env.ReadState());
        Assert.Equal("aws", credential.TypeName);
        Assert.Equal(name, credential.Name);
        Assert.Equal("e2e", credential.Labels["suite"]);
        Assert.Equal(name, credential.Labels["run"]);
        Assert.False(string.IsNullOrEmpty(credential.AwsProfile));
        Assert.Equal(Path.GetFullPath(env.AwsCredentialsFile), Path.GetFullPath(credential.Path));

        var credentials = ReadFile(env.AwsCredentialsFile);
        Assert.True(credentials.Contains($"[{credential.AwsProfile}]"), "~/.aws/credentials should hold the canary profile");
        Assert.True(credentials.Contains("aws_access_key_id"), "~/.aws/credentials should hold an access key");
        Assert.True(credentials.Contains("aws_secret_access_key"), "~/.aws/credentials should hold a secret key");
        Assert.True(ReadFile(env.AwsConfigFile).Contains($"[profile {credential.AwsProfile}]"), "~/.aws/config should hold the canary profile");
        AssertOwnerOnly(env.AwsCredentialsFile);

        var show = await env.RunSuccessAsync("show aws");
        Assert.Contains("AWS", show.StandardOutput);
        Assert.DoesNotContain("No credentials", show.StandardOutput);

        await env.RunSuccessAsync("remove all");

        Assert.Empty(env.ReadState());
        Assert.False(ReadFile(env.AwsCredentialsFile).Contains($"[{credential.AwsProfile}]"), "Profile should be removed from ~/.aws/credentials");
        Assert.False(ReadFile(env.AwsConfigFile).Contains($"[profile {credential.AwsProfile}]"), "Profile should be removed from ~/.aws/config");
    }

    [StagingFact]
    public async Task DeployAws_CustomProfileAndRegion_AreUsed()
    {
        await using var env = await LoggedInEnvironmentAsync();
        var name = UniqueName();
        var profile = $"profile-{name}";

        await env.RunSuccessAsync($"deploy aws --name {name} --aws-profile {profile} --aws-region eu-west-2");

        Assert.Equal(profile, Assert.Single(env.ReadState()).AwsProfile);
        Assert.True(ReadFile(env.AwsCredentialsFile).Contains($"[{profile}]"), "~/.aws/credentials should use the requested profile");
        var config = ReadFile(env.AwsConfigFile);
        Assert.True(config.Contains($"[profile {profile}]") && config.Contains("region = eu-west-2"), "~/.aws/config should use the requested profile and region");
    }

    [StagingFact]
    public async Task DeployAws_ProfileOwnedByUser_IsRefusedAndLeftUntouched()
    {
        await using var env = await LoggedInEnvironmentAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(env.AwsCredentialsFile)!);
        const string userCredentials = "[work]\naws_access_key_id = AKIAUSEROWNED000000\naws_secret_access_key = user-owned\n";
        await File.WriteAllTextAsync(env.AwsCredentialsFile, userCredentials);

        var result = await env.RunAsync($"deploy aws --name {UniqueName()} --aws-profile work");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(userCredentials, ReadFile(env.AwsCredentialsFile));
        Assert.Empty(env.ReadState());
    }

    [StagingFact]
    public async Task DeployAws_SameNameTwice_SecondIsRefused()
    {
        await using var env = await LoggedInEnvironmentAsync();
        var name = UniqueName();
        await env.RunSuccessAsync($"deploy aws --name {name}");

        var second = await env.RunAsync($"deploy aws --name {name}");

        Assert.Equal(1, second.ExitCode);
        Assert.Contains("already exists", second.StandardOutput);
        Assert.Single(env.ReadState());
    }

    [StagingFact]
    public async Task DeploySsh_WritesKeyAndHostBlock_RemoveCleansUp()
    {
        await using var env = await LoggedInEnvironmentAsync();
        var name = UniqueName();

        var deploy = await env.RunSuccessAsync($"deploy ssh --name {name}");

        Assert.Contains($"Credential {name} (SSH) deployed", deploy.StandardOutput);
        var credential = Assert.Single(env.ReadState());
        Assert.Equal("ssh", credential.TypeName);
        Assert.False(string.IsNullOrEmpty(credential.Target));
        var keyFile = credential.Path;
        Assert.Equal(Path.GetFullPath(env.SshDirectory), Path.GetDirectoryName(Path.GetFullPath(keyFile)));
        Assert.True(File.Exists(keyFile), "Private key should be written");
        Assert.True(File.Exists(keyFile + ".pub"), "Public key should be written");
        AssertOwnerOnly(keyFile);
        AssertOwnerOnly(env.SshConfigFile);

        var config = ReadFile(env.SshConfigFile);
        Assert.Contains($"Host {credential.Target}", config);
        Assert.Contains($"IdentityFile {keyFile}", config);

        await env.RunSuccessAsync("remove all");

        Assert.Empty(env.ReadState());
        Assert.False(File.Exists(keyFile), "Private key should be deleted");
        Assert.False(File.Exists(keyFile + ".pub"), "Public key should be deleted");
        Assert.DoesNotContain(keyFile, ReadFile(env.SshConfigFile));
    }

    [StagingFact]
    public async Task DeploySsh_ExistingUserConfig_IsPreservedThroughDeployAndRemove()
    {
        await using var env = await LoggedInEnvironmentAsync();
        Directory.CreateDirectory(env.SshDirectory);
        const string userHost = "Host github.com\n    User git\n    IdentityFile ~/.ssh/id_ed25519\n";
        await File.WriteAllTextAsync(env.SshConfigFile, userHost);

        await env.RunSuccessAsync($"deploy ssh --name {UniqueName()}");
        Assert.Contains(userHost, ReadFile(env.SshConfigFile));

        await env.RunSuccessAsync("remove all");
        Assert.Contains(userHost, ReadFile(env.SshConfigFile));
    }

    [StagingFact]
    public async Task DeployAwsAndSsh_SameName_ShowListsBothAndRefreshKeepsThem()
    {
        await using var env = await LoggedInEnvironmentAsync();
        var name = UniqueName();
        await env.RunSuccessAsync($"deploy aws --name {name}");
        await env.RunSuccessAsync($"deploy ssh --name {name}");

        var show = await env.RunSuccessAsync("show");
        Assert.Contains("AWS", show.StandardOutput);
        Assert.Contains("SSH", show.StandardOutput);

        var refresh = await env.RunSuccessAsync("refresh");
        Assert.Contains("All credentials are already up to date", refresh.StandardOutput);
        Assert.Equal(["aws", "ssh"], env.ReadState().Select(c => c.TypeName).Order());

        await env.RunSuccessAsync("remove all");
        Assert.Empty(env.ReadState());
    }

    [SandboxedFact]
    public async Task Deploy_ForgedToken_IsRejectedByServerAndWritesNothing()
    {
        await using var env = new IsolatedCliEnvironment(tracebitEnvironment: "stage");
        // Passes the CLI's local checks but its signature is not one the server issued
        await env.WriteTokenAsync(TestTokens.Create(DateTimeOffset.UtcNow.AddDays(1)));

        var result = await env.RunAsync($"deploy aws --name {UniqueName()}");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("problem authenticating with the Tracebit server", result.StandardOutput);
        Assert.False(Directory.Exists(Path.Combine(env.HomeDirectory, ".aws")));
        Assert.Empty(env.ReadState());
    }
}
