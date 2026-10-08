# Tracebit CLI Testing Guide

This document describes the testing infrastructure for the Tracebit CLI project.

## Test Projects

The solution contains three test projects:

### 1. `Tracebit.Cli.Tests` - Unit Tests
Location: `Tracebit.Cli.Tests/`

**Purpose**: Fast, isolated tests for individual components and functions.

### 2. `Tracebit.Cli.Integration.Tests` - Integration Tests
Location: `Tracebit.Cli.Integration.Tests/`

**Purpose**: Tests that verify components work correctly together with real dependencies.

- Uses WireMock.Net for HTTP mocking
- Temporary file system for file operations

### 3. `Tracebit.Cli.E2E.Tests` - End-to-End Tests
Location: `Tracebit.Cli.E2E.Tests/`

**Purpose**: Tests that execute the compiled CLI binary as a user would.

- Tests complete user workflows
- Cross-platform testing (Windows, Linux, macOS)
- Tests error handling and user experience
- Every test runs the CLI in a sandbox (`IsolatedCliEnvironment`): a temporary `HOME` and a temporary
  `TRACEBIT_CONFIG_DIR` for `credentials.json` and `state.json`, so your real `~/.aws`, `~/.ssh` and CLI state are
  never read or changed
- Tests that write `~/.aws` or `~/.ssh` (`[SandboxedFact]`) skip on Windows, where the home directory cannot be
  redirected

#### Staging deploy tests

`Staging/StagingDeployTests.cs` (`[StagingFact]`) deploys real AWS and SSH canaries on
`community.stage.tracebit.com`, checks the files and state the CLI writes, then removes them again. They need a staging
API token and skip without one:

```bash
export TRACEBIT_STAGING_API_TOKEN=<staging API token>
dotnet test Tracebit.Cli.E2E.Tests/Tracebit.Cli.E2E.Tests.csproj
```

`Staging/StagingAlertTests.cs` goes one step further: it deploys a canary, runs `tracebit trigger`, and polls the
alerts API (`GET /api/v1/alerts` and `/api/v1/alerts/{id}/logs`) until an alert for that canary's name appears. The
token therefore needs the `canary-credentials:all:create`, `alerts:all:list` and `alerts:all:get` scopes, and the `ssh`
and `aws` CLIs must be installed.

- The SSH alert test runs by default and takes ~30 seconds. `ssh` ignores `HOME`, so the sandbox sets the test-only
  `TRACEBIT_SSH_CONFIG`, which makes `tracebit trigger ssh` pass `-F <sandboxed ~/.ssh/config>`. The alert therefore
  only fires if deploy wrote a working Host block. Real users never set it, so trigger runs what an attacker would.
- The AWS alert test (`[CiStagingFact]`) waits ~5 minutes for CloudTrail, so it only runs when `CI=true`, which GitHub
  Actions sets. Run it locally with `CI=true dotnet test ...`.

In CI the token comes from the `TRACEBIT_STAGING_API_TOKEN` repository secret. Each test uses a unique canary name, so
the parallel OS jobs do not collide on the shared staging tenant.

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run Specific Test Project
```bash
# Unit tests only
dotnet test Tracebit.Cli.Tests/Tracebit.Cli.Tests.csproj

# Integration tests only
dotnet test Tracebit.Cli.Integration.Tests/Tracebit.Cli.Integration.Tests.csproj

# E2E tests only
dotnet test Tracebit.Cli.E2E.Tests/Tracebit.Cli.E2E.Tests.csproj
```

### Run with Code Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Run Specific Test
```bash
dotnet test --filter "FullyQualifiedName~Tracebit.Cli.Tests.Commands.UtilsTests
```

## Writing New Tests

### Unit Test Example

```csharp
using FluentAssertions;
using Moq;
using Xunit;

namespace Tracebit.Cli.Tests.State;

public class StateManagerTests
{
    [Fact]
    public void AddAwsCredential_NewCredential_AddsToList()
    {
        var stateManager = new StateManager();
        var credentials = TestDataBuilder.BuildAwsCredentials();

        var result = stateManager.AddAwsCredential(
            "test-cred",
            null,
            credentials
        );

        result.Should().NotBeNull();
        result.Name.Should().Be("test-cred");
        result.Type.Should().Be("aws");
        stateManager.AwsCredentials.Should().ContainSingle();
    }
}
```

### Integration Test Example

```csharp
using FluentAssertions;
using Xunit;

namespace Tracebit.Cli.Integration.Tests.FileSystem;

public class StateManagerFileTests : BaseIntegrationTest
{
    [Fact]
    public async Task SaveAsync_PersistsToFile()
    {
        var stateFile = GetTempFilePath("state.json");
        // ... setup StateManager with temp file

        await stateManager.SaveAsync(CancellationToken.None);

        File.Exists(stateFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(stateFile);
        content.Should().Contain("\"Credentials\":");
    }
}
```

### E2E Test Example

```csharp
using FluentAssertions;
using Xunit;

namespace Tracebit.Cli.E2E.Tests.Workflows;

public class AuthWorkflowTests
{
    [Fact]
    public async Task Auth_Status_WithoutCredentials_ShowsHelpfulMessage()
    {
        var result = await CliTestHelper.ExecuteAsync("auth status");

        result.ExitCode.Should().Be(1);
        result.StandardOutput.Should().Contain("not yet logged into Tracebit");
        result.StandardOutput.Should().Contain("auth");
    }
}
```

## Best Practices

1. **Test Naming**: Use descriptive names following the pattern `MethodName_Scenario_ExpectedBehavior`
2. **Arrange-Act-Assert**: Structure tests clearly with these three sections
3. **Isolation**: Each test should be independent and not rely on other tests
4. **Cleanup**: Use `IDisposable` for test fixtures that need cleanup
5. **Fast Tests**: Unit tests should run in milliseconds
6. **Deterministic**: Tests should not be flaky or depend on timing
7. **Readable**: Use FluentAssertions for clear, readable assertions




