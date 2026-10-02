using System.Text.Json;

using Tracebit.Cli.API.CanaryCredentials;

namespace Tracebit.Cli.Tests.API;

public class HttpCanaryCredentialsTests
{
    [Fact]
    public void DeployUrl_UsesFirstHostNameAndDeploymentId()
    {
        var deploymentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var credentials = new HttpCanaryCredentials
        {
            ConfirmationId = Guid.NewGuid(),
            BrowserDeploymentId = deploymentId,
            HostNames = ["gitlab.example.com", "other.example.com"],
            ExpiresAt = null,
            Credentials = JsonSerializer.SerializeToElement(new { })
        };

        var url = credentials.DeployUrl();

        Assert.Equal("https", url.Scheme);
        Assert.Equal("gitlab.example.com", url.Host);
        Assert.Equal("/_browserDeploy", url.AbsolutePath);
        Assert.Equal($"?id={deploymentId}", url.Query);
    }
}
