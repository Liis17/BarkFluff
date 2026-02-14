using System.Text.Json;
using Barkfluff.AdminPanel.Models;
using Microsoft.Extensions.Options;

namespace Barkfluff.AdminPanel.Services;

public class SeqService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<SeqSettings> _settings;
    private readonly ILogger<SeqService> _logger;

    public SeqService(HttpClient httpClient, IOptions<SeqSettings> settings, ILogger<SeqService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(settings.Value.ServerUrl);

        // Add API key header if configured
        if (!string.IsNullOrEmpty(settings.Value.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Seq-ApiKey", settings.Value.ApiKey);
        }
    }

    public async Task<JsonElement?> GetEventsAsync(string? filter = null, int count = 50, DateTime? fromDateUtc = null, string? afterId = null)
    {
        var query = $"/api/events?count={count}&render=true";

        if (!string.IsNullOrEmpty(filter))
            query += $"&filter={Uri.EscapeDataString(filter)}";

        if (fromDateUtc.HasValue)
            query += $"&fromDateUtc={fromDateUtc.Value:O}";

        if (!string.IsNullOrEmpty(afterId))
            query += $"&afterId={Uri.EscapeDataString(afterId)}";

        try
        {
            var response = await _httpClient.GetAsync(query);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Seq events API returned {StatusCode}: {Body}", (int)response.StatusCode, json);
                return null;
            }
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch events from Seq");
            return null;
        }
    }

    public async Task<JsonElement?> RunSqlQueryAsync(string sqlQuery, DateTime? fromDateUtc = null, DateTime? toDateUtc = null)
    {
        var query = $"/api/sqlquery?q={Uri.EscapeDataString(sqlQuery)}";

        if (fromDateUtc.HasValue)
            query += $"&fromDateUtc={fromDateUtc.Value:O}";

        if (toDateUtc.HasValue)
            query += $"&toDateUtc={toDateUtc.Value:O}";

        try
        {
            var response = await _httpClient.GetAsync(query);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Seq SQL API returned {StatusCode} for query '{Query}': {Body}", (int)response.StatusCode, sqlQuery, json);
                return null;
            }
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute SQL query on Seq: {Query}", sqlQuery);
            return null;
        }
    }

    /// <summary>
    /// Fetches events from Seq with pagination, returning a flat list of event JSON elements.
    /// Uses the Events API which is available in all Seq editions.
    /// </summary>
    public async Task<List<JsonElement>?> GetAllEventsListAsync(
        string? filter = null,
        DateTime? fromDateUtc = null,
        int maxEvents = 5000)
    {
        var allEvents = new List<JsonElement>();
        string? afterId = null;
        const int pageSize = 500;

        while (allEvents.Count < maxEvents)
        {
            var remaining = Math.Min(pageSize, maxEvents - allEvents.Count);
            var result = await GetEventsAsync(filter, remaining, fromDateUtc, afterId);

            if (result == null)
                return allEvents.Count > 0 ? allEvents : null;

            if (result.Value.ValueKind != JsonValueKind.Object ||
                !result.Value.TryGetProperty("Events", out var events) ||
                events.ValueKind != JsonValueKind.Array)
                return allEvents.Count > 0 ? allEvents : null;

            var pageEvents = events.EnumerateArray().ToList();
            if (pageEvents.Count == 0) break;

            allEvents.AddRange(pageEvents);

            // Get afterId for next page
            var lastEvent = pageEvents[^1];
            if (lastEvent.TryGetProperty("Id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                afterId = idProp.GetString();
            else
                break;

            if (pageEvents.Count < remaining) break; // Last page
        }

        return allEvents;
    }

    public async Task<JsonElement?> GetSignalsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/signals");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch signals from Seq");
            return null;
        }
    }
}
