using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Tracebit.Cli.E2E.Tests.Helpers;

/// <summary>
/// Reads alerts from the Tracebit staging API (<c>GET /api/v1/alerts</c> and <c>/api/v1/alerts/{id}/logs</c>). The
/// token needs the <c>alerts:all:list</c> and <c>alerts:all:get</c> scopes.
/// </summary>
public sealed class StagingAlertsClient : IDisposable
{
    private static readonly Uri StagingUrl = new("https://community.stage.tracebit.com");
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    public StagingAlertsClient(string token)
    {
        _http = new HttpClient { BaseAddress = StagingUrl, Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Polls until an alert has a log for the canary credential called <paramref name="canaryName"/>, and returns it.
    /// </summary>
    /// <remarks>
    /// Matches on the canary name rather than start time: an alert stays open and can absorb later activity, so a
    /// trigger may land in an alert that started before it. Canary names are unique per test.
    /// </remarks>
    public async Task<StagingAlert> WaitForAlertAsync(string canaryName, string provider, DateTimeOffset triggeredAt, TimeSpan timeout)
    {
        // Alerts started well before the trigger can still absorb it, so look back further than the trigger itself
        var startTimeAfter = triggeredAt.UtcDateTime.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        using var cts = new CancellationTokenSource(timeout);
        var checkedAlerts = 0;

        while (true)
        {
            var alerts = await ListAlertsAsync(startTimeAfter, cts.Token);
            foreach (var alert in alerts.Where(a => a.Provider == provider))
            {
                checkedAlerts++;
                if (await HasLogForCanaryAsync(alert.Id, canaryName, cts.Token))
                    return alert;
            }

            try
            {
                await Task.Delay(PollInterval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"No '{provider}' alert for canary '{canaryName}' within {timeout} of triggering at {triggeredAt:u} " +
                    $"({checkedAlerts} alert checks)");
            }
        }
    }

    private async Task<List<StagingAlert>> ListAlertsAsync(string startTimeAfter, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"/api/v1/alerts?limit=500&start_time_after={startTimeAfter}", cancellationToken);
        return document.RootElement.GetProperty("alerts").EnumerateArray()
            .Select(a => new StagingAlert(
                Id: a.GetProperty("id").GetString()!,
                Provider: a.GetProperty("provider").GetString()!,
                Severity: a.GetProperty("severity").GetString()!,
                Title: a.GetProperty("title").GetString()!))
            .ToList();
    }

    private async Task<bool> HasLogForCanaryAsync(string alertId, string canaryName, CancellationToken cancellationToken)
    {
        string? next = $"/api/v1/alerts/{alertId}/logs";
        for (var page = 0; next is not null && page < 10; page++)
        {
            using var document = await GetJsonAsync(next, cancellationToken);
            var found = document.RootElement.GetProperty("logs").EnumerateArray().Any(log =>
                log.TryGetProperty("canary_credential", out var credential) &&
                credential.ValueKind == JsonValueKind.Object &&
                credential.GetProperty("name").GetString() == canaryName);
            if (found)
                return true;

            next = document.RootElement.TryGetProperty("next_page", out var nextPage) && nextPage.ValueKind == JsonValueKind.String
                ? nextPage.GetString()
                : null;
        }
        return false;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(
                $"Alerts API returned {(int)response.StatusCode}: {StagingFactAttribute.TokenVariable} needs the alerts:all:list and alerts:all:get scopes");
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    public void Dispose() => _http.Dispose();
}

public sealed record StagingAlert(string Id, string Provider, string Severity, string Title);
