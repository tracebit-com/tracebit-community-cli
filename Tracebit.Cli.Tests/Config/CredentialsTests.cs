using Tracebit.Cli.Config;

namespace Tracebit.Cli.Tests.Config;

/// <summary>
/// Tests for Credentials class
/// Note: File I/O tests are in Integration tests.
/// These tests verify the model and path generation.
/// </summary>
public class CredentialsTests
{
    [Fact]
    public void File_ReturnsExpectedPathStructure()
    {
        var file = Credentials.File();

        Assert.NotNull(file);
        Assert.Equal("credentials.json", file.Name);
        Assert.Contains("tracebit", file.DirectoryName);
    }

    [Fact]
    public void File_PathIncludesLocalApplicationData()
    {
        var file = Credentials.File();
        var expectedBasePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(expectedBasePath, file.FullName);
    }
}
