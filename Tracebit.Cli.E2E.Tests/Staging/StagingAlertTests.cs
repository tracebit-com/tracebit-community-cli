using Tracebit.Cli.E2E.Tests.Helpers;

namespace Tracebit.Cli.E2E.Tests.Staging;

/// <summary>
/// Deploys a canary on staging, triggers it with <c>tracebit trigger</c>, and waits for Tracebit to raise an alert for
/// it. This proves the whole path: the CLI wrote a working config, the trigger used it, and the alert pipeline saw it.
/// </summary>
public class StagingAlertTests
{
    private static string UniqueName() => "e2e-" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<IsolatedCliEnvironment> LoggedInEnvironmentAsync()
    {
        var env = new IsolatedCliEnvironment(tracebitEnvironment: "stage");
        await env.WriteTokenAsync(StagingFactAttribute.Token!);
        return env;
    }

    /// <remarks>
    /// <c>ssh</c> reads the sandboxed config via <c>TRACEBIT_SSH_CONFIG</c>, so the alert only fires if deploy wrote a
    /// Host block that selects the canary key. SSH alerts arrive in ~30 seconds.
    /// </remarks>
    [StagingFact]
    public async Task TriggerSsh_DeployedCanary_RaisesAlert()
    {
        await using var env = await LoggedInEnvironmentAsync();
        using var alerts = new StagingAlertsClient(StagingFactAttribute.Token!);
        var name = UniqueName();
        await env.RunSuccessAsync($"deploy ssh --name {name}");

        var triggeredAt = DateTimeOffset.UtcNow;
        var trigger = await env.RunSuccessAsync($"trigger ssh --name {name}");
        Assert.Contains($"Credential {name} (SSH) triggered", trigger.StandardOutput);

        var alert = await alerts.WaitForAlertAsync(name, provider: "ssh", triggeredAt, timeout: TimeSpan.FromMinutes(3));

        Assert.Equal("High", alert.Severity);
        Assert.Contains("SSH", alert.Title);
    }

    /// <remarks>
    /// AWS alerts depend on CloudTrail delivery and take ~5 minutes, so this runs in CI only. The AWS CLI is pointed at
    /// the sandboxed files with <c>AWS_CONFIG_FILE</c> and <c>AWS_SHARED_CREDENTIALS_FILE</c>.
    /// </remarks>
    [CiStagingFact]
    public async Task TriggerAws_DeployedCanary_RaisesAlert()
    {
        await using var env = await LoggedInEnvironmentAsync();
        using var alerts = new StagingAlertsClient(StagingFactAttribute.Token!);
        var name = UniqueName();
        await env.RunSuccessAsync($"deploy aws --name {name}");

        var triggeredAt = DateTimeOffset.UtcNow;
        var trigger = await env.RunSuccessAsync($"trigger aws --name {name}");
        Assert.Contains($"Credential {name} (AWS) triggered", trigger.StandardOutput);

        var alert = await alerts.WaitForAlertAsync(name, provider: "aws", triggeredAt, timeout: TimeSpan.FromMinutes(12));

        Assert.Equal("High", alert.Severity);
    }
}
