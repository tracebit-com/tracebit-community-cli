using Microsoft.Extensions.DependencyInjection;

namespace Tracebit.Cli.Integration.Tests.Helpers;

/// <summary>
/// Base class for integration tests that need temporary file system and DI container
/// </summary>
public abstract class BaseIntegrationTest : IDisposable
{
    protected string TempDirectory { get; private set; }
    protected ServiceProvider Services { get; private set; }

    protected BaseIntegrationTest()
    {
        // Create a unique temporary directory for this test
        TempDirectory = Path.Combine(Path.GetTempPath(), $"tracebit-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(TempDirectory);

        // Setup DI container with default configuration
        var services = new ServiceCollection();
        Services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Get a temporary file path within the test directory
    /// </summary>
    protected string GetTempFilePath(string filename)
    {
        return Path.Combine(TempDirectory, filename);
    }

    /// <summary>
    /// Create a temporary subdirectory
    /// </summary>
    protected string CreateTempSubdirectory(string subdirName)
    {
        var path = Path.Combine(TempDirectory, subdirName);
        Directory.CreateDirectory(path);
        return path;
    }

    public virtual void Dispose()
    {
        Services.Dispose();

        // Clean up temporary directory
        if (Directory.Exists(TempDirectory))
        {
            try
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup - ignore errors
            }
        }

        GC.SuppressFinalize(this);
    }
}


