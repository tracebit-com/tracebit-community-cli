namespace Tracebit.Cli.E2E.Tests.Helpers;

/// <summary>
/// A test that writes to <c>~/.aws</c> or <c>~/.ssh</c>. Skipped on Windows, where the CLI resolves the home directory
/// from the OS rather than <c>USERPROFILE</c>, so it cannot be sandboxed.
/// </summary>
/// <remarks>
/// The skip reason is worked out when xUnit reads <see cref="Skip"/> rather than in a constructor, because
/// <see cref="FactAttribute.Skip"/> is virtual.
/// </remarks>
public class SandboxedFactAttribute : FactAttribute
{
    private string? _skip;

    public override string? Skip
    {
        get => _skip ?? SkipReason();
        set => _skip = value;
    }

    protected virtual string? SkipReason() =>
        OperatingSystem.IsWindows() ? "The home directory cannot be sandboxed on Windows" : null;
}

/// <summary>
/// A sandboxed test that deploys real canaries on the Tracebit staging environment. Skipped unless
/// <c>TRACEBIT_STAGING_API_TOKEN</c> holds a staging API token.
/// </summary>
public class StagingFactAttribute : SandboxedFactAttribute
{
    public const string TokenVariable = "TRACEBIT_STAGING_API_TOKEN";

    public static string? Token => Environment.GetEnvironmentVariable(TokenVariable) is { Length: > 0 } token ? token : null;

    protected override string? SkipReason() =>
        base.SkipReason() ??
        (Token is null ? $"Set {TokenVariable} to a community.stage.tracebit.com API token to run staging tests" : null);
}

/// <summary>
/// A staging test too slow to run on every local test run, such as waiting ~5 minutes for an AWS alert. Runs in CI
/// (where GitHub Actions sets <c>CI=true</c>); set <c>CI=true</c> locally to run it anyway.
/// </summary>
public sealed class CiStagingFactAttribute : StagingFactAttribute
{
    protected override string? SkipReason() =>
        base.SkipReason() ??
        (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
            ? null
            : "Slow staging test, runs in CI only (set CI=true to run locally)");
}
