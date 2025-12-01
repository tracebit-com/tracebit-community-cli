using Tracebit.Cli.Commands;

namespace Tracebit.Cli.Tests.Commands;

public class UtilsTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public void GenerateRandomString_ReturnsStringOfCorrectLength(int length)
    {
        var result = Utils.GenerateRandomString(length);

        Assert.Equal(length, result.Length);
    }

    [Fact]
    public void GenerateRandomString_ContainsOnlyValidCharacters()
    {
        var result = Utils.GenerateRandomString(100);

        Assert.Matches("^[A-Za-z0-9]+$", result);
    }

    [Fact]
    public void GenerateRandomString_GeneratesDifferentStrings()
    {
        var result1 = Utils.GenerateRandomString(20);
        var result2 = Utils.GenerateRandomString(20);

        Assert.NotEqual(result1, result2);
    }

    [Theory]
    [InlineData("key=value", true)]
    [InlineData("key1=value1,key2=value2", true)]
    [InlineData("environment=prod,team=backend,region=us-east-1", true)]
    [InlineData("a=b", true)]
    [InlineData("", false)]
    [InlineData("key", false)]
    [InlineData("key=", false)]
    [InlineData("=value", false)]
    [InlineData("key=value,", false)]
    [InlineData(",key=value", false)]
    [InlineData("key=value,,another=value", false)]
    [InlineData("key=value=extra", false)]
    [InlineData("key1=value1,key2", false)]
    public void IsLabelFormat_ValidatesLabelFormat(string input, bool expected)
    {
        var result = Utils.IsLabelFormat(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void InvalidLabelFormatMessage_ReturnsExpectedMessage()
    {
        var result = Utils.InvalidLabelFormatMessage();

        Assert.Equal("Invalid label format, please use key1=value1,key2=value2", result);
    }

    [Fact]
    public void GenerateRandomPasswordManagerLoginName_ReturnsValidName()
    {
        var result = Utils.GenerateRandomPasswordManagerLoginName();

        Assert.False(string.IsNullOrEmpty(result));
        Assert.StartsWith("GitLab", result);
    }

    [Fact]
    public void AwsRegions_ContainsCommonRegions()
    {
        var regions = Utils.AwsRegions();

        Assert.Contains("us-east-1", regions);
        Assert.Contains("us-west-2", regions);
        Assert.Contains("eu-west-1", regions);
        Assert.Contains("ap-southeast-1", regions);
    }

    [Fact]
    public void AwsRegions_ReturnsUniqueRegions()
    {
        var regions = Utils.AwsRegions();

        Assert.Equal(regions.Count, regions.Distinct().Count());
    }

    [Fact]
    public void GetCurrentVersion_ReturnsVersion()
    {
        var version = Utils.GetCurrentVersion();

        Assert.NotNull(version);
        Assert.True(version.Major >= 0);
    }
}
