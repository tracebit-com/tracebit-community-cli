using System.Text.Json;

using Tracebit.Cli.API.CanaryCredentials;

namespace Tracebit.Cli.API;

using System.Text.Json.Serialization;

using Labels = List<Label>;

public class IssueCredentialsRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("types")]
    public required List<string> Types { get; set; }

    [JsonPropertyName("source")]
    public required string Source { get; set; }

    [JsonPropertyName("sourceType")]
    public required string SourceType { get; set; }

    [JsonPropertyName("labels")]
    public List<Label>? Labels { get; set; }
}

public class Label
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("value")]
    public required string Value { get; set; }
}

public class IssueCredentialsResponse
{
    [JsonPropertyName("aws")]
    public AwsCanaryCredentials? Aws { get; set; }

    [JsonPropertyName("ssh")]
    public SshCanaryCredentials? Ssh { get; set; }

    [JsonPropertyName("http")]
    public Dictionary<string, HttpCanaryCredentials>? Http { get; set; }
}

public class ConfirmCredentialsRequest
{
    public required Guid Id { get; set; }
}

internal class GenerateAndSendCanaryEmailRequest
{
    [JsonPropertyName("labels")] public Labels? Labels { get; set; } = [];
}

internal class UpdateStatusRequest
{
    [JsonPropertyName("name")] public required string Name { get; set; }
}

public class GenerateAndSendCanaryEmailResponse
{
    [JsonPropertyName("emailFrom")] public required string EmailFrom { get; set; }
    [JsonPropertyName("emailTo")] public required string EmailTo { get; set; }
    [JsonPropertyName("emailSubject")] public required string EmailSubject { get; set; }

    [JsonPropertyName("credentialExpiresAt")]
    public required DateTime? CredentialExpiresAt { get; set; }

    [JsonPropertyName("credentialTriggerableAfter")]
    public required DateTime? CredentialTriggerableAfter { get; set; }
}

internal class ExpireCredentialsRequest
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("type")] public required string Type { get; set; }
}

public class GenerateCredentialsMetadataResponse
{
    [JsonPropertyName("awsProfileName")]
    public required string AwsProfileName { get; set; }

    [JsonPropertyName("awsRegion")]
    public required string AwsRegion { get; set; }

    [JsonPropertyName("sshKeyFileName")]
    public required string SshKeyFileName { get; set; }
}

public class TracebitClient(HttpClient httpClient) : AuthenticatedApiClient(httpClient)
{
    private const string IssueCredentialsPath = "v1/credentials/issue-credentials";
    private const string ConfirmCredentialsPath = "v1/credentials/confirm-credentials";
    private const string GenerateCredentialsMetadataPath = "v1/credentials/generate-metadata";
    private const string EmailCanaryPath = "_internal/v1/cli/email";
    private const string StatusPath = "_internal/v1/cli/status";
    private const string RemovePath = "_internal/v1/cli/remove";

    private static readonly Label VersionLabel = new()
    {
        Name = "tracebit_cli_version",
        Value = Commands.Utils.GetCurrentVersion()?.ToFullString() ?? "unknown"
    };

    public async Task<IssueCredentialsResponse> IssueCredentials(string name, List<string> credentialTypes, List<Label>? labels)
    {
        var requestBody = CreateIssueRequest(name, credentialTypes, labels);

        var jsonContent = JsonSerializer.Serialize(
            requestBody,
            ApiJsonSerializerContext.Default.IssueCredentialsRequest
        );

        var request = new HttpRequestMessage(HttpMethod.Post, IssueCredentialsPath)
        {
            Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
        };

        var response = await HttpClient.SendAsync(request);

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var errorMessage = await response.Content.ReadAsStringAsync();
            if (errorMessage.Contains("quota exceeded", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new QuotaExceededException();
            }
            throw;
        }

        IssueCredentialsResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<IssueCredentialsResponse>(
                await response.Content.ReadAsStringAsync(),
                ApiJsonSerializerContext.Default.IssueCredentialsResponse
            );
        }
        catch (JsonException e)
        {
            throw new Exception("Response body is not valid JSON.", e);
        }

        if (result is null)
        {
            throw new Exception("Could not parse response");
        }

        return result;
    }

    public async Task ConfirmCredentialsAsync(Guid confirmationId, CancellationToken cancellationToken)
    {
        var requestBody = new ConfirmCredentialsRequest { Id = confirmationId };

        var jsonContent = JsonSerializer.Serialize(
            requestBody,
            ApiJsonSerializerContext.Default.ConfirmCredentialsRequest
        );

        var request = new HttpRequestMessage(HttpMethod.Post, ConfirmCredentialsPath)
        {
            Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
        };

        var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static IssueCredentialsRequest CreateIssueRequest(string name, List<string> credentialTypes, Labels? labels)
    {
        var requestLabels = labels?.ToList() ?? [];
        requestLabels.Add(VersionLabel);
        return new IssueCredentialsRequest
        {
            Name = name,
            Types = credentialTypes,
            Source = Constants.TracebitCliSource,
            SourceType = Constants.SourceTypeDefault,
            Labels = requestLabels
        };
    }

    public async Task<GenerateAndSendCanaryEmailResponse> GenerateAndSendCanaryEmailAsync(Labels? labels, CancellationToken cancellationToken)
    {
        var requestLabels = labels?.ToList() ?? [];
        requestLabels.Add(VersionLabel);
        var requestBody = new GenerateAndSendCanaryEmailRequest
        {
            Labels = requestLabels,
        };

        var jsonContent = JsonSerializer.Serialize(
            requestBody,
            ApiJsonSerializerContext.Default.GenerateAndSendCanaryEmailRequest
        );

        var request = new HttpRequestMessage(HttpMethod.Post, EmailCanaryPath)
        {
            Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
        };

        var response = await HttpClient.SendAsync(request, cancellationToken);

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var errorMessage = await response.Content.ReadAsStringAsync(cancellationToken);
            if (errorMessage.Contains("quota exceeded", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new QuotaExceededException();
            }
            if (errorMessage.Contains("Too many email canaries", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new TooManyEmailCanariesDeployed(errorMessage);
            }
            throw;
        }

        GenerateAndSendCanaryEmailResponse? result;
        try
        {
            result = JsonSerializer.Deserialize<GenerateAndSendCanaryEmailResponse>(
                await response.Content.ReadAsStringAsync(cancellationToken),
                ApiJsonSerializerContext.Default.GenerateAndSendCanaryEmailResponse
            );
        }
        catch (JsonException e)
        {
            throw new Exception("Response body is not valid JSON.", e);
        }

        if (result is null)
        {
            throw new Exception("Could not parse response");
        }

        return result;
    }

    public async Task UpdateStatusAsync(string deviceName, CancellationToken cancellationToken)
    {
        var requestBody = new UpdateStatusRequest
        {
            Name = deviceName
        };

        var jsonContent = JsonSerializer.Serialize(
            requestBody,
            ApiJsonSerializerContext.Default.UpdateStatusRequest
        );

        var request = new HttpRequestMessage(HttpMethod.Post, StatusPath)
        {
            Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
        };

        var response = await HttpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
            throw new Exception($"Expected '204 NoContent', Received '{(int)response.StatusCode} {response.StatusCode}'");
    }

    public async Task RemoveAsync(string name, string type, CancellationToken cancellationToken)
    {
        var requestBody = new ExpireCredentialsRequest
        {
            Name = name,
            Type = type,
        };

        var jsonContent = JsonSerializer.Serialize(
            requestBody,
            ApiJsonSerializerContext.Default.ExpireCredentialsRequest
        );

        var request = new HttpRequestMessage(HttpMethod.Post, RemovePath)
        {
            Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
        };

        var response = await HttpClient.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<GenerateCredentialsMetadataResponse> GenerateCredentialMetadataAsync(CancellationToken cancellationToken)
    {
        var response = await HttpClient.GetAsync(GenerateCredentialsMetadataPath, cancellationToken);
        response.EnsureSuccessStatusCode();
        GenerateCredentialsMetadataResponse? result;
        try
        {
            result = JsonSerializer.Deserialize(
                await response.Content.ReadAsStringAsync(cancellationToken),
                ApiJsonSerializerContext.Default.GenerateCredentialsMetadataResponse
            );
        }
        catch (JsonException e)
        {
            throw new Exception("Response body is not valid JSON.", e);
        }

        if (result is null)
        {
            throw new Exception("Could not parse response");
        }

        return result;
    }
}
