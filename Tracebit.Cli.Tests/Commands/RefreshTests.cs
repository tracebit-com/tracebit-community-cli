using Tracebit.Cli.Commands;
using Tracebit.Cli.State;

namespace Tracebit.Cli.Tests.Commands;

public class RefreshTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AwsCredentialData Aws(DateTime? expiresAt) => new()
    {
        Name = "aws",
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = expiresAt,
        Path = ""
    };

    private static SshCredentialData Ssh(DateTime createdAt) => new()
    {
        Name = "ssh",
        CreatedAt = createdAt,
        ExpiresAt = null,
        Path = ""
    };

    private static GitlabCookieCredentialData Http(DateTime? expiresAt) => new()
    {
        Name = "cookie",
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = expiresAt,
        Path = ""
    };

    [Fact]
    public void AwsShouldRefresh_NoExpiry_ReturnsFalse()
    {
        Assert.False(Refresh.AwsShouldRefresh(Aws(null), Now));
    }

    [Fact]
    public void AwsShouldRefresh_ExpiresInMoreThanTwoHours_ReturnsFalse()
    {
        Assert.False(Refresh.AwsShouldRefresh(Aws(Now.AddHours(2).AddMinutes(1)), Now));
    }

    [Fact]
    public void AwsShouldRefresh_ExpiresWithinTwoHours_ReturnsTrue()
    {
        Assert.True(Refresh.AwsShouldRefresh(Aws(Now.AddHours(2)), Now));
        Assert.True(Refresh.AwsShouldRefresh(Aws(Now.AddMinutes(30)), Now));
    }

    [Fact]
    public void AwsShouldRefresh_AlreadyExpired_ReturnsTrue()
    {
        Assert.True(Refresh.AwsShouldRefresh(Aws(Now.AddHours(-1)), Now));
    }

    [Fact]
    public void SshShouldRefresh_CreatedLessThanADayAgo_ReturnsFalse()
    {
        Assert.False(Refresh.SshShouldRefresh(Ssh(Now.AddHours(-23)), Now));
    }

    [Fact]
    public void SshShouldRefresh_CreatedAtLeastADayAgo_ReturnsTrue()
    {
        Assert.True(Refresh.SshShouldRefresh(Ssh(Now.AddDays(-1)), Now));
        Assert.True(Refresh.SshShouldRefresh(Ssh(Now.AddDays(-10)), Now));
    }

    [Fact]
    public void HttpShouldRefresh_NoExpiry_ReturnsFalse()
    {
        Assert.False(Refresh.HttpShouldRefresh(Http(null), Now));
    }

    [Fact]
    public void HttpShouldRefresh_ExpiresWithinTwoHours_ReturnsTrue()
    {
        Assert.True(Refresh.HttpShouldRefresh(Http(Now.AddHours(1)), Now));
    }

    [Fact]
    public void HttpShouldRefresh_ExpiresInMoreThanTwoHours_ReturnsFalse()
    {
        Assert.False(Refresh.HttpShouldRefresh(Http(Now.AddDays(1)), Now));
    }
}
