namespace Tracebit.Cli.Integration.Tests.Helpers;

/// <summary>
/// Helper methods for file system operations in tests
/// </summary>
public static class FileSystemHelper
{
    /// <summary>
    /// Create a temporary directory that will be cleaned up
    /// </summary>
    public static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"tracebit-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Create an SSH config file with the given content
    /// </summary>
    public static void CreateSshConfigFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Set Unix file permissions (only on Unix systems)
    /// </summary>
    public static void SetUnixPermissions(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    /// <summary>
    /// Get Unix file permissions (only on Unix systems)
    /// </summary>
    public static UnixFileMode? GetUnixPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        return File.GetUnixFileMode(path);
    }

    /// <summary>
    /// Create a temporary SSH directory structure for testing
    /// </summary>
    public static (string sshDir, string configPath) CreateTempSshDirectory()
    {
        var tempDir = CreateTempDirectory();
        var sshDir = Path.Combine(tempDir, ".ssh");
        Directory.CreateDirectory(sshDir);

        var configPath = Path.Combine(sshDir, "config");
        File.WriteAllText(configPath, "");

        return (sshDir, configPath);
    }

    /// <summary>
    /// Clean up a directory (best effort)
    /// </summary>
    public static void CleanupDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best effort - ignore errors
            }
        }
    }
}
