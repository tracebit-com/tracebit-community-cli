using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using DeployCommand = Tracebit.Cli.Commands.Deploy;

namespace Tracebit.Cli.Tests.Commands;

public class DeployOptionsTests : IClassFixture<DeployOptionsTests.DeployCommandFixture>
{
    // Deploy.Command mutates a static command, so it must only be built once per test run
    public sealed class DeployCommandFixture
    {
        public RootCommand Root { get; }

        public DeployCommandFixture()
        {
            Root = Program.RootCommand();
            Root.Subcommands.Add(DeployCommand.Command(new ServiceCollection().BuildServiceProvider()));
        }
    }

    private readonly RootCommand _root;

    public DeployOptionsTests(DeployCommandFixture fixture)
    {
        _root = fixture.Root;
    }

    [Fact]
    public void LabelsParsing_ParsesKeyValuePairs()
    {
        var labels = DeployCommand._labelsParsing("env=prod,team=backend");

        Assert.Equal(2, labels.Count);
        Assert.Equal("env", labels[0].Name);
        Assert.Equal("prod", labels[0].Value);
        Assert.Equal("team", labels[1].Name);
        Assert.Equal("backend", labels[1].Value);
    }

    [Fact]
    public void LabelsParsing_SplitsOnFirstEqualsOnly()
    {
        var labels = DeployCommand._labelsParsing("url=https://example.com/?a=b");

        Assert.Single(labels);
        Assert.Equal("url", labels[0].Name);
        Assert.Equal("https://example.com/?a=b", labels[0].Value);
    }

    [Fact]
    public void LabelsParsing_SkipsEntriesWithoutValue()
    {
        var labels = DeployCommand._labelsParsing("env=prod,broken,,team=backend");

        Assert.Equal(["env", "team"], labels.Select(l => l.Name));
    }

    [Theory]
    [InlineData("deploy aws --labels env=prod,team=backend")]
    [InlineData("deploy aws --name my-canary")]
    [InlineData("deploy all --aws-profile canary --aws-region eu-west-1 --ssh-key-file canary-key")]
    [InlineData("deploy ssh --ssh-key-file id_canary")]
    public void Parse_ValidArguments_HasNoErrors(string commandLine)
    {
        var result = _root.Parse(commandLine);

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("deploy aws --labels env", "Invalid label format")]
    [InlineData("deploy aws --labels env=prod,", "Invalid label format")]
    [InlineData("deploy aws --labels =prod", "Invalid label format")]
    public void Parse_InvalidLabels_ReportsError(string commandLine, string expectedError)
    {
        var result = _root.Parse(commandLine);

        Assert.Contains(result.Errors, e => e.Message.Contains(expectedError));
    }

    [Theory]
    [InlineData("deploy aws --aws-profile \"my profile\"", "Invalid AWS profile name")]
    [InlineData("deploy aws --aws-profile profile", "AWS profile name cannot be 'profile'")]
    public void Parse_InvalidAwsProfile_ReportsError(string commandLine, string expectedError)
    {
        var result = _root.Parse(commandLine);

        Assert.Contains(result.Errors, e => e.Message.Contains(expectedError));
    }

    [Fact]
    public void Parse_SshKeyFileWithWhitespace_ReportsError()
    {
        var result = _root.Parse("deploy ssh --ssh-key-file \"my key\"");

        Assert.Contains(result.Errors, e => e.Message.Contains("Invalid SSH key file name"));
    }

    [Fact]
    public void Parse_LabelsValue_IsAvailableAsParsedLabels()
    {
        var result = _root.Parse("deploy aws --labels env=prod");
        var labelsOption = result.CommandResult.Command.Options.Single(o => o.Name == "--labels");

        var labels = result.GetValue((Option<List<Tracebit.Cli.API.Label>>)labelsOption);

        Assert.NotNull(labels);
        Assert.Single(labels);
        Assert.Equal("env", labels[0].Name);
    }

    [Fact]
    public void Parse_NameDefaultsToMachineName()
    {
        var result = _root.Parse("deploy aws");
        var nameOption = result.CommandResult.Command.Options.Single(o => o.Name == "--name");

        Assert.Equal(Environment.MachineName, result.GetValue((Option<string>)nameOption));
    }
}
