using Tracebit.Cli.API;

namespace Tracebit.Cli.Tests.API;

public class ExceptionsTests
{
    [Fact]
    public void QuotaExceededException_HasExpectedMessage()
    {
        var exception = new QuotaExceededException();

        Assert.Contains("maximum number of canary credentials", exception.Message);
        Assert.Contains("https://community.tracebit.com", exception.Message);
    }

    [Fact]
    public void TooManyEmailCanariesDeployed_PassesMessageThrough()
    {
        var customMessage = "Custom error message about email canaries";

        var exception = new TooManyEmailCanariesDeployed(customMessage);

        Assert.Equal(customMessage, exception.Message);
    }
}
