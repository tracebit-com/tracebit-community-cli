namespace Tracebit.Cli.Deploy;

/// <summary>
/// Replaces a file's contents without ever leaving it half-written. The new contents go to a temporary file in the
/// same directory, with the same permissions, flushed to disk, then renamed over the original. A crash or full disk
/// mid-write leaves the original untouched and at worst a stray temporary file, which the next write cleans up.
/// </summary>
internal static class AtomicFileWriter
{
    private const string TempSuffix = ".tracebit-tmp";

    /// <summary>
    /// Atomically replaces the contents of the file behind <paramref name="lockedStream"/>. The caller keeps that
    /// stream open with FileShare.None so other CLI processes stay blocked throughout. On Windows an exclusive handle
    /// prevents the rename, so there the write falls back to rewriting in place through the locked stream.
    /// </summary>
    public static async Task ReplaceContentsAsync(FileStream lockedStream, Func<Stream, Task> writeContents)
    {
        var path = lockedStream.Name;
        var tempPath = path + TempSuffix;
        File.Delete(tempPath); // leftover from an interrupted earlier run

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = File.GetUnixFileMode(path);

        try
        {
            await using (var temp = new FileStream(tempPath, options))
            {
                await writeContents(temp);
                temp.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception e) when (OperatingSystem.IsWindows() && e is IOException or UnauthorizedAccessException)
            {
                // Fall through to the in-place write below
            }

            await using var staged = File.OpenRead(tempPath);
            lockedStream.Seek(0, SeekOrigin.Begin);
            await staged.CopyToAsync(lockedStream);
            lockedStream.SetLength(lockedStream.Position);
            lockedStream.Flush(flushToDisk: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
