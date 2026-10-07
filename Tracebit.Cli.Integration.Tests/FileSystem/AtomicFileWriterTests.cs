using Tracebit.Cli.Deploy;
using Tracebit.Cli.Integration.Tests.Helpers;

namespace Tracebit.Cli.Integration.Tests.FileSystem;

public class AtomicFileWriterTests : BaseIntegrationTest
{
    [Fact]
    public async Task ReplaceContentsAsync_ReplacesContentAndLeavesNoTempFile()
    {
        var path = GetTempFilePath("config");
        await File.WriteAllTextAsync(path, "old");
        await using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await AtomicFileWriter.ReplaceContentsAsync(locked, async s =>
            {
                await using var w = new StreamWriter(s, leaveOpen: true);
                await w.WriteAsync("new content");
            });
        }

        Assert.Equal("new content", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(TempDirectory));
    }

    [Fact]
    public async Task ReplaceContentsAsync_PreservesUnixPermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        var path = GetTempFilePath("config");
        await File.WriteAllTextAsync(path, "old");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await AtomicFileWriter.ReplaceContentsAsync(locked, s => s.WriteAsync("new"u8.ToArray()).AsTask());
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task ReplaceContentsAsync_WriterThrows_OriginalUntouchedAndTempRemoved()
    {
        var path = GetTempFilePath("config");
        await File.WriteAllTextAsync(path, "old");
        await using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => AtomicFileWriter.ReplaceContentsAsync(locked, async s =>
            {
                await s.WriteAsync("half"u8.ToArray());
                throw new IOException("disk full");
            }));
        }

        Assert.Equal("old", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(TempDirectory));
    }

    [Fact]
    public async Task ReplaceContentsAsync_StaleTempFileFromEarlierRun_IsReplaced()
    {
        var path = GetTempFilePath("config");
        await File.WriteAllTextAsync(path, "old");
        await File.WriteAllTextAsync(path + ".tracebit-tmp", "stale");
        await using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await AtomicFileWriter.ReplaceContentsAsync(locked, s => s.WriteAsync("new"u8.ToArray()).AsTask());
        }

        Assert.Equal("new", await File.ReadAllTextAsync(path));
        Assert.Equal([path], Directory.GetFiles(TempDirectory));
    }
}
