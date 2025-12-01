using System.Diagnostics;
using System.Text;

namespace Tracebit.Cli.E2E.Tests.Helpers;

/// <summary>
/// Helper for executing the CLI binary in E2E tests
/// </summary>
public static class CliTestHelper
{
    /// <summary>
    /// Execute the CLI with the given arguments
    /// </summary>
    public static async Task<CommandResult> ExecuteAsync(
        string arguments,
        string? workingDirectory = null,
        Dictionary<string, string?>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var cliBinaryPath = GetCliBinaryPath();

        var startInfo = new ProcessStartInfo
        {
            FileName = cliBinaryPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDirectory != null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                if (kvp.Value != null)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
                else
                {
                    startInfo.EnvironmentVariables.Remove(kvp.Key);
                }
            }
        }

        using var process = new Process();
        process.StartInfo = startInfo;

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = outputBuilder.ToString().TrimEnd(),
            StandardError = errorBuilder.ToString().TrimEnd()
        };
    }

    /// <summary>
    /// Execute the CLI and assert it succeeds (exit code 0)
    /// </summary>
    public static async Task<CommandResult> ExecuteSuccessAsync(
        string arguments,
        string? workingDirectory = null,
        Dictionary<string, string?>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(arguments, workingDirectory, environmentVariables, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new Exception($"CLI command failed with exit code {result.ExitCode}.\n" +
                              $"Arguments: {arguments}\n" +
                              $"Stdout: {result.StandardOutput}\n" +
                              $"Stderr: {result.StandardError}");
        }

        return result;
    }

    /// <summary>
    /// Get the path to the CLI binary
    /// </summary>
    private static string GetCliBinaryPath()
    {
        // Look for the compiled binary in the output directory
        var solutionDir = FindSolutionDirectory();
        var binaryName = OperatingSystem.IsWindows() ? "tracebit.exe" : "tracebit";

        // Determine runtime identifier
        var rid = GetRuntimeIdentifier();

        List<string> ridPaths = rid is null ? [] :
            [
                Path.Combine(solutionDir, "Tracebit.Cli", "bin", "Debug", "net9.0", rid, binaryName),
                Path.Combine(solutionDir, "Tracebit.Cli", "bin", "Release", "net9.0", rid, binaryName),
            ];
        // Check Debug and Release configurations, with and without RID folder
        List<string> possiblePaths = [
            ..ridPaths,
            Path.Combine(solutionDir, "Tracebit.Cli", "bin", "Debug", "net9.0", binaryName),
            Path.Combine(solutionDir, "Tracebit.Cli", "bin", "Release", "net9.0", binaryName),
        ];

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(
            $"Could not find CLI binary. Tried:\n{string.Join("\n", possiblePaths)}\n\n" +
            "Please build the project first with: dotnet build");
    }

    /// <summary>
    /// Get the runtime identifier for the current platform
    /// </summary>
    private static string? GetRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "win-x64",
                System.Runtime.InteropServices.Architecture.Arm64 => "win-arm64",
                _ => null
            };
        }
        else if (OperatingSystem.IsMacOS())
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "osx-x64",
                System.Runtime.InteropServices.Architecture.Arm64 => "osx-arm64",
                _ => null
            };
        }
        else if (OperatingSystem.IsLinux())
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "linux-x64",
                System.Runtime.InteropServices.Architecture.Arm64 => "linux-arm64",
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// Find the solution directory by walking up from the current directory
    /// </summary>
    private static string FindSolutionDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();

        while (currentDir != null)
        {
            if (Directory.GetFiles(currentDir, "*.sln").Length != 0)
            {
                return currentDir;
            }

            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find solution directory");
    }
}

/// <summary>
/// Result of executing a CLI command
/// </summary>
public class CommandResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
}

