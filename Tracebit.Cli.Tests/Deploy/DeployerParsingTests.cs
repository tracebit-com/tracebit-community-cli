using Tracebit.Cli.Deploy;

namespace Tracebit.Cli.Tests.Deploy;

public class DeployerParsingTests
{
    [Fact]
    public void RemoveHostEntry_RemovesTargetHostBlockAndReturnsIdentityFile()
    {
        List<string> lines =
        [
            "Host github.com",
            "  User git",
            "Host 10.0.0.1",
            "  IdentityFile /home/user/.ssh/old-key",
            "  PasswordAuthentication no",
            "Host example.com",
            "  Port 2222"
        ];

        var (result, identityFile) = Deployer.RemoveHostEntry(lines, "10.0.0.1");

        Assert.Equal(["Host github.com", "  User git", "Host example.com", "  Port 2222"], result);
        Assert.Equal("/home/user/.ssh/old-key", identityFile);
    }

    [Fact]
    public void RemoveHostEntry_TargetNotPresent_ReturnsLinesUnchanged()
    {
        List<string> lines = ["Host github.com", "  User git"];

        var (result, identityFile) = Deployer.RemoveHostEntry(lines, "10.0.0.1");

        Assert.Equal(lines, result);
        Assert.Null(identityFile);
    }

    [Fact]
    public void RemoveHostEntry_TargetAtEndOfFile_RemovesThroughEnd()
    {
        List<string> lines = ["Host github.com", "  User git", "Host 10.0.0.1", "  IdentityFile key"];

        var (result, identityFile) = Deployer.RemoveHostEntry(lines, "10.0.0.1");

        Assert.Equal(["Host github.com", "  User git"], result);
        Assert.Equal("key", identityFile);
    }

    [Fact]
    public void RemoveHostEntry_HostLineWithMultiplePatterns_IsNotRemoved()
    {
        // A "Host a b" line configures multiple hosts, removing it would break unrelated configuration
        List<string> lines = ["Host 10.0.0.1 other.example.com", "  IdentityFile shared-key"];

        var (result, identityFile) = Deployer.RemoveHostEntry(lines, "10.0.0.1");

        Assert.Equal(lines, result);
        Assert.Null(identityFile);
    }

    [Fact]
    public void RemoveHostEntry_KeywordsAreCaseInsensitive()
    {
        List<string> lines = ["HOST 10.0.0.1", "\tIDENTITYFILE key", "host other"];

        var (result, identityFile) = Deployer.RemoveHostEntry(lines, "10.0.0.1");

        Assert.Equal(["host other"], result);
        Assert.Equal("key", identityFile);
    }

    [Fact]
    public void GetConfigIndentation_ReturnsIndentOfFirstIndentedLine()
    {
        Assert.Equal("    ", Deployer.GetConfigIndentation(["Host a", "    User git"]));
        Assert.Equal("\t", Deployer.GetConfigIndentation(["Host a", "\tUser git"]));
    }

    [Fact]
    public void GetConfigIndentation_NoIndentedLines_ReturnsEmpty()
    {
        Assert.Equal("", Deployer.GetConfigIndentation(["Host a", "User git"]));
        Assert.Equal("", Deployer.GetConfigIndentation([]));
    }

    [Fact]
    public void RemoveAwsProfileFromLines_ConfigFormat_RemovesOnlyTargetProfile()
    {
        List<string> lines =
        [
            "[default]",
            "region = us-east-1",
            "[profile canary]",
            "region = eu-west-1",
            "output = json",
            "[profile other]",
            "region = us-west-2"
        ];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: true);

        Assert.Equal(["[default]", "region = us-east-1", "[profile other]", "region = us-west-2"], result);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_ConfigFormat_DoesNotMatchCredentialsStyleSection()
    {
        // In the config file, "[canary]" is not the canary profile ("[profile canary]" is)
        List<string> lines = ["[canary]", "region = eu-west-1"];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: true);

        Assert.Equal(lines, result);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_CredentialsFormat_RemovesOnlyTargetProfile()
    {
        List<string> lines =
        [
            "[default]",
            "aws_access_key_id = AKIA1",
            "[canary]",
            "aws_access_key_id = AKIA2",
            "aws_secret_access_key = secret",
            "[other]",
            "aws_access_key_id = AKIA3"
        ];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: false);

        Assert.Equal(["[default]", "aws_access_key_id = AKIA1", "[other]", "aws_access_key_id = AKIA3"], result);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_CredentialsFormat_DoesNotMatchProfilePrefixedSection()
    {
        List<string> lines = ["[profile canary]", "aws_access_key_id = AKIA2"];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: false);

        Assert.Equal(lines, result);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_KeepsCommentsInsideRemovedSection()
    {
        List<string> lines =
        [
            "[canary]",
            "# canary section comment",
            "aws_access_key_id = AKIA2",
            "[other]",
            "aws_access_key_id = AKIA3"
        ];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: false);

        Assert.Equal(["# canary section comment", "[other]", "aws_access_key_id = AKIA3"], result);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_TargetNotPresent_ReturnsLinesUnchanged()
    {
        List<string> lines = ["[default]", "aws_access_key_id = AKIA1", "", "[other]", "aws_access_key_id = AKIA3"];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: false);

        Assert.Equal(lines, result);
    }
}
