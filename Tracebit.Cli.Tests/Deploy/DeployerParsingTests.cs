using Tracebit.Cli.Deploy;

namespace Tracebit.Cli.Tests.Deploy;

public class DeployerParsingTests
{
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

    [Fact]
    public void FindAwsProfileAccessKeyId_ReturnsKeyFromTargetSectionOnly()
    {
        List<string> lines =
        [
            "[default]",
            "aws_access_key_id = AKIAREAL",
            "# [canary]",
            "[canary]",
            "  AWS_ACCESS_KEY_ID=AKIACANARY  ",
            "aws_secret_access_key = x",
            "[other]",
            "aws_access_key_id = AKIAOTHER"
        ];

        Assert.Equal("AKIACANARY", Deployer.FindAwsProfileAccessKeyId(lines, "canary"));
        Assert.Equal("AKIAREAL", Deployer.FindAwsProfileAccessKeyId(lines, "default"));
        Assert.Null(Deployer.FindAwsProfileAccessKeyId(lines, "missing"));
    }

    [Fact]
    public void AwsProfileExists_DistinguishesConfigAndCredentialsFormats()
    {
        List<string> config = ["[profile canary]", "region = eu-west-1"];
        List<string> credentials = ["[canary]", "aws_access_key_id = x"];

        Assert.True(Deployer.AwsProfileExists(config, "canary", hasProfilePrefix: true));
        Assert.False(Deployer.AwsProfileExists(config, "canary", hasProfilePrefix: false));
        Assert.True(Deployer.AwsProfileExists(credentials, "canary", hasProfilePrefix: false));
        Assert.False(Deployer.AwsProfileExists(credentials, "other", hasProfilePrefix: false));
    }

    [Theory]
    [InlineData("Host 10.0.0.1", "Host", new[] { "10.0.0.1" })]
    [InlineData("Host=10.0.0.1", "Host", new[] { "10.0.0.1" })]
    [InlineData("  IdentityFile = \"a b\" c", "IdentityFile", new[] { "a b", "c" })]
    [InlineData("Match host *.corp exec \"on-vpn\"", "Match", new[] { "host", "*.corp", "exec", "on-vpn" })]
    public void ParseSshConfigLine_SplitsKeywordAndArgs(string line, string keyword, string[] args)
    {
        var (k, a) = Deployer.ParseSshConfigLine(line);
        Assert.Equal(keyword, k);
        Assert.Equal(args, a);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# Host 10.0.0.1")]
    public void ParseSshConfigLine_BlankOrComment_ReturnsNullKeyword(string line)
    {
        var (k, a) = Deployer.ParseSshConfigLine(line);
        Assert.Null(k);
        Assert.Empty(a);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_Config_StopsAtAnySectionHeader()
    {
        List<string> lines =
        [
            "[profile canary]",
            "region = eu-west-1",
            "[default]",
            "region = us-east-1",
            "[sso-session corporate]",
            "sso_start_url = https://x",
            "[services corporate]",
            "s3 =",
            "  endpoint_url = https://y",
            "[profile keep]",
            "region = us-west-2"
        ];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: true);

        Assert.Equal(lines.Skip(2).ToList(), result);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_Credentials_StopsAtAnySectionHeader()
    {
        List<string> lines = ["[canary]", "aws_access_key_id = x", "[profile odd]", "aws_access_key_id = y", "[default]", "aws_access_key_id = z"];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: false);

        Assert.Equal(lines.Skip(2).ToList(), result);
    }

    [Theory]
    [InlineData("[profile canary] # my work profile")]
    [InlineData("[profile canary]   ; semicolon comment")]
    [InlineData("[ profile   canary ]")]
    public void RemoveAwsProfileFromLines_HeaderWithCommentOrExtraWhitespace_IsRecognised(string header)
    {
        List<string> lines = [header, "region = eu-west-1", "[default] # keep me", "region = us-east-1"];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: true);

        Assert.Equal(["[default] # keep me", "region = us-east-1"], result);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_DefaultProfileInConfig_MatchesBareDefaultHeader()
    {
        List<string> lines = ["[default]", "region = us-east-1", "[profile keep]", "region = x"];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "default", hasProfilePrefix: true);

        Assert.Equal(["[profile keep]", "region = x"], result);
    }

    [Fact]
    public void RemoveAwsProfileFromLines_SimilarlyNamedSections_AreNotRemoved()
    {
        List<string> lines = ["[profile canary2]", "region = a", "[canary]", "region = b", "[profile canary]", "region = c"];

        var result = Deployer.RemoveAwsProfileFromLines(lines, "canary", hasProfilePrefix: true);

        Assert.Equal(["[profile canary2]", "region = a", "[canary]", "region = b"], result);
    }

    [Theory]
    [InlineData("[profile x]", "profile x")]
    [InlineData("[profile x] # comment", "profile x")]
    [InlineData("[ profile\tx ]", "profile x")]
    [InlineData("[default]", "default")]
    public void ParseAwsSectionHeader_ReturnsNormalisedName(string line, string expected) =>
        Assert.Equal(expected, Deployer.ParseAwsSectionHeader(line));

    [Theory]
    [InlineData("region = eu-west-1")]
    [InlineData("[profile x] trailing junk")]
    [InlineData("[unterminated")]
    [InlineData("key = [not a header]")]
    public void ParseAwsSectionHeader_NotAHeader_ReturnsNull(string line) =>
        Assert.Null(Deployer.ParseAwsSectionHeader(line));

    [Fact]
    public void FindAwsProfileAccessKeyId_HandlesHeaderCommentAndValueComment()
    {
        List<string> lines = ["[canary] # work", "aws_access_key_id = AKIACANARY # rotated 2026", "[default]", "aws_access_key_id = AKIAREAL"];

        Assert.Equal("AKIACANARY", Deployer.FindAwsProfileAccessKeyId(lines, "canary"));
    }

    [Fact]
    public void BuildHostEntry_OneLinePerKey()
    {
        Assert.Equal(
            ["Host 10.0.0.1", "  IdentityFile a", "  IdentityFile b", "  IdentitiesOnly yes", "  PasswordAuthentication no"],
            Deployer.BuildHostEntry("10.0.0.1", ["a", "b"], "  "));
    }

    // ---- SSH host block editing: we only ever add, swap or drop our own IdentityFile line ----

    [Fact]
    public void FindHostBlock_ReturnsRangeUpToNextHostOrMatch()
    {
        List<string> lines = ["Host github.com", "  User git", "Host 10.0.0.1", "  IdentityFile k", "  User me", "Match host *.corp", "  ProxyJump b"];

        Assert.Equal((2, 5), Deployer.FindHostBlock(lines, "10.0.0.1"));
        Assert.Equal((0, 2), Deployer.FindHostBlock(lines, "github.com"));
        Assert.Null(Deployer.FindHostBlock(lines, "missing"));
    }

    [Fact]
    public void FindHostBlock_RunsToEndOfFile() =>
        Assert.Equal((1, 3), Deployer.FindHostBlock(["Host a", "Host 10.0.0.1", "  IdentityFile k"], "10.0.0.1"));

    [Fact]
    public void FindHostBlock_HostLineWithMultiplePatterns_IsNotOurs()
    {
        // A "Host a b" line configures multiple hosts; editing it would change unrelated configuration
        Assert.Null(Deployer.FindHostBlock(["Host 10.0.0.1 other.example.com", "  IdentityFile shared-key"], "10.0.0.1"));
    }

    [Fact]
    public void FindHostBlock_KeywordsCaseInsensitive_CommentsNotBoundaries()
    {
        List<string> lines = ["HOST 10.0.0.1", "  # Host not-really", "\tIDENTITYFILE key", "host other"];
        var block = Deployer.FindHostBlock(lines, "10.0.0.1");
        Assert.Equal((0, 3), block);
        Assert.Equal(["key"], Deployer.IdentityFilesIn(lines, block!.Value));
    }

    [Fact]
    public void IdentityFilesIn_EqualsSyntaxQuotedPathsAndMultipleLines()
    {
        List<string> lines = ["Host=10.0.0.1", "  IdentityFile = \"/home/u/My Keys/a\"", "  IdentityFile b", "Host = other"];
        Assert.Equal(["/home/u/My Keys/a", "b"], Deployer.IdentityFilesIn(lines, Deployer.FindHostBlock(lines, "10.0.0.1")!.Value));
    }

    [Fact]
    public void UpsertIdentityFile_NoBlock_AppendsFreshBlock()
    {
        var result = Deployer.UpsertIdentityFile(["Host a", "  User x"], "10.0.0.1", "/k/new", oldKeyPath: null);
        Assert.Equal(["Host a", "  User x", "Host 10.0.0.1", "  IdentityFile /k/new", "  IdentitiesOnly yes", "  PasswordAuthentication no"], result);
    }

    [Fact]
    public void UpsertIdentityFile_ForeignKeyInBlock_AddsOurLineAfterItAndLeavesEverythingElse()
    {
        List<string> lines = ["Host 10.0.0.1", "\tUser me", "\tIdentityFile /k/theirs", "\tProxyJump bastion", "Host z"];

        var result = Deployer.UpsertIdentityFile(lines, "10.0.0.1", "/k/ours", oldKeyPath: null);

        Assert.Equal(
        [
            "Host 10.0.0.1",
            "\tUser me",
            "\tIdentityFile /k/theirs",
            "\tIdentityFile /k/ours",
            "\tProxyJump bastion",
            "\tIdentitiesOnly yes",
            "\tPasswordAuthentication no",
            "Host z"
        ], result);
    }

    [Fact]
    public void UpsertIdentityFile_BlockWithoutIdentityFile_InsertsAfterHostLine()
    {
        var result = Deployer.UpsertIdentityFile(["Host 10.0.0.1", "  User me"], "10.0.0.1", "/k/ours", oldKeyPath: null);
        Assert.Equal(["Host 10.0.0.1", "  IdentityFile /k/ours", "  User me", "  IdentitiesOnly yes", "  PasswordAuthentication no"], result);
    }

    [Fact]
    public void UpsertIdentityFile_Rotation_ReplacesOurLineInPlace()
    {
        List<string> lines = ["Host 10.0.0.1", "  IdentityFile /k/theirs", "  IdentityFile /k/old", "  IdentitiesOnly yes", "  PasswordAuthentication no"];

        var result = Deployer.UpsertIdentityFile(lines, "10.0.0.1", "/k/new", oldKeyPath: "/k/old");

        Assert.Equal(["Host 10.0.0.1", "  IdentityFile /k/theirs", "  IdentityFile /k/new", "  IdentitiesOnly yes", "  PasswordAuthentication no"], result);
    }

    [Fact]
    public void UpsertIdentityFile_DoesNotOverrideUsersOwnIdentitiesOnlySetting()
    {
        List<string> lines = ["Host 10.0.0.1", "  IdentitiesOnly no", "  IdentityFile /k/theirs"];

        var result = Deployer.UpsertIdentityFile(lines, "10.0.0.1", "/k/ours", oldKeyPath: null);

        Assert.Equal(["Host 10.0.0.1", "  IdentitiesOnly no", "  IdentityFile /k/theirs", "  IdentityFile /k/ours", "  PasswordAuthentication no"], result);
    }

    [Fact]
    public void RemoveIdentityFile_OtherLinesPresent_DropsOnlyOurLine()
    {
        List<string> lines = ["Host 10.0.0.1", "  IdentityFile /k/theirs", "  IdentityFile /k/ours", "  IdentitiesOnly yes", "  PasswordAuthentication no", "Match host *.corp", "  ProxyJump b"];

        var result = Deployer.RemoveIdentityFile(lines, "10.0.0.1", "/k/ours");

        Assert.Equal(["Host 10.0.0.1", "  IdentityFile /k/theirs", "  IdentitiesOnly yes", "  PasswordAuthentication no", "Match host *.corp", "  ProxyJump b"], result);
    }

    [Fact]
    public void RemoveIdentityFile_LastKeyAndOnlyOurDirectivesLeft_RemovesBlock()
    {
        List<string> lines = ["Host a", "Host 10.0.0.1", "  IdentityFile /k/ours", "  # note", "  IdentitiesOnly yes", "  PasswordAuthentication no", "Host z"];

        Assert.Equal(["Host a", "Host z"], Deployer.RemoveIdentityFile(lines, "10.0.0.1", "/k/ours"));
    }

    [Fact]
    public void RemoveIdentityFile_LastKeyButUserDirectiveLeft_KeepsBlock()
    {
        List<string> lines = ["Host 10.0.0.1", "  IdentityFile /k/ours", "  User me", "  IdentitiesOnly yes"];

        Assert.Equal(["Host 10.0.0.1", "  User me", "  IdentitiesOnly yes"], Deployer.RemoveIdentityFile(lines, "10.0.0.1", "/k/ours"));
    }

    [Fact]
    public void RemoveIdentityFile_NoBlockOrNoSuchKey_Unchanged()
    {
        List<string> lines = ["Host 10.0.0.1", "  IdentityFile /k/theirs"];
        Assert.Equal(lines, Deployer.RemoveIdentityFile(lines, "10.0.0.1", "/k/ours"));
        Assert.Equal(lines, Deployer.RemoveIdentityFile(lines, "10.9.9.9", "/k/ours"));
    }
}
