using Tracebit.Cli.API;

namespace Tracebit.Cli.Tests.API;

public class ConstantsTests
{
    [Fact]
    public void RequireHttps_HttpsUrl_Passes() =>
        Assert.Equal("https://localhost:7210/", Constants.RequireHttps(new Uri("https://localhost:7210")).ToString());

    [Theory]
    [InlineData("http://localhost:7210")]
    [InlineData("http://community.tracebit.com")]
    [InlineData("ftp://example.com")]
    public void RequireHttps_OtherScheme_Throws(string url) =>
        Assert.Throws<InvalidOperationException>(() => Constants.RequireHttps(new Uri(url)));
}
