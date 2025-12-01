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




