using Barkfluff.AdminPanel.Models;

using Microsoft.Extensions.Options;

using System.Text.Json;

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

    public async Task<JsonElement?> GetEventsAsync(string? filter = null, int count = 50, DateTime? fromDateUtc = null, string? afterId = null, DateTime? toDateUtc = null)
    {
        var query = $"/api/events?count={count}&render=true";

        if (!string.IsNullOrEmpty(filter))
            query += $"&filter={Uri.EscapeDataString(filter)}";

        if (fromDateUtc.HasValue)
            query += $"&fromDateUtc={fromDateUtc.Value:O}";

        if (toDateUtc.HasValue)
            query += $"&toDateUtc={toDateUtc.Value:O}";

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
    /// Handles both response formats: bare array [...] and wrapped {"Events": [...]}.
    /// </summary>
    public async Task<List<JsonElement>?> GetAllEventsListAsync(
        string? filter = null,
        DateTime? fromDateUtc = null,
        int maxEvents = 5000,
        DateTime? toDateUtc = null)
    {
        var allEvents = new List<JsonElement>();
        string? afterId = null;
        const int pageSize = 500;

        while (allEvents.Count < maxEvents)
        {
            var remaining = Math.Min(pageSize, maxEvents - allEvents.Count);
            var result = await GetEventsAsync(filter, remaining, fromDateUtc, afterId, toDateUtc);

            if (result == null)
                return allEvents.Count > 0 ? allEvents : null;

            // Seq may return events as a bare array [...] or wrapped {"Events": [...]}
            var pageEvents = ExtractEventsArray(result.Value);
            if (pageEvents == null)
                return allEvents.Count > 0 ? allEvents : null;

            if (pageEvents.Count == 0) break;

            allEvents.AddRange(pageEvents);

            // Get afterId for next page (Seq uses "Id" or "id")
            var lastEvent = pageEvents[^1];
            afterId = GetEventId(lastEvent);
            if (afterId == null) break;

            if (pageEvents.Count < remaining) break; // Last page
        }

        return allEvents;
    }

    /// <summary>
    /// Extracts events array from Seq response, handling both formats:
    /// bare array [...] and wrapped {"Events": [...]}.
    /// </summary>
    public static List<JsonElement>? ExtractEventsArray(JsonElement response)
    {
        // Format 1: bare array [...]
        if (response.ValueKind == JsonValueKind.Array)
            return response.EnumerateArray().ToList();

        // Format 2: wrapped {"Events": [...]}
        if (response.ValueKind == JsonValueKind.Object)
        {
            if (response.TryGetProperty("Events", out var events) && events.ValueKind == JsonValueKind.Array)
                return events.EnumerateArray().ToList();
            if (response.TryGetProperty("events", out var eventsLower) && eventsLower.ValueKind == JsonValueKind.Array)
                return eventsLower.EnumerateArray().ToList();
        }

        return null;
    }

    private static string? GetEventId(JsonElement evt)
    {
        if (evt.ValueKind != JsonValueKind.Object) return null;
        if (evt.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String)
            return id.GetString();
        if (evt.TryGetProperty("id", out var idLower) && idLower.ValueKind == JsonValueKind.String)
            return idLower.GetString();
        return null;
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

    public async Task<long?> CountEventsAsync(DateTime? fromDateUtc = null, DateTime? toDateUtc = null)
    {
        var resp = await RunSqlQueryAsync("select count(*) as Total from stream", fromDateUtc, toDateUtc);
        if (resp is null) return null;
        return ExtractFirstScalar(resp.Value);
    }

    public async Task<long?> DeleteEventsAsync(DateTime? fromDateUtc = null, DateTime? toDateUtc = null)
    {
        var query = "/api/events";
        var parts = new List<string>();
        if (fromDateUtc.HasValue) parts.Add($"fromDateUtc={Uri.EscapeDataString(fromDateUtc.Value.ToString("O"))}");
        if (toDateUtc.HasValue) parts.Add($"toDateUtc={Uri.EscapeDataString(toDateUtc.Value.ToString("O"))}");
        if (parts.Count > 0) query += "?" + string.Join("&", parts);

        var response = await _httpClient.DeleteAsync(query);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Seq DELETE /api/events returned {StatusCode}: {Body}", (int)response.StatusCode, json);
            throw new InvalidOperationException($"Seq вернул {(int)response.StatusCode}: {json}");
        }

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            return TryReadDeletedCount(doc);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? TryReadDeletedCount(JsonElement doc)
    {
        if (doc.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in new[] { "Count", "count", "Deleted", "EventsDeleted", "deleted" })
        {
            if (doc.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt64();
        }
        return null;
    }

    private static long? ExtractFirstScalar(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        if (root.TryGetProperty("Rows", out var rows) && rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0)
        {
            var firstRow = rows[0];
            if (firstRow.ValueKind == JsonValueKind.Array && firstRow.GetArrayLength() > 0
                && firstRow[0].ValueKind == JsonValueKind.Number)
                return firstRow[0].GetInt64();
        }

        if (root.TryGetProperty("Slices", out var slices) && slices.ValueKind == JsonValueKind.Array && slices.GetArrayLength() > 0)
        {
            var slice = slices[0];
            if (slice.ValueKind == JsonValueKind.Object
                && slice.TryGetProperty("Rows", out var sliceRows)
                && sliceRows.ValueKind == JsonValueKind.Array
                && sliceRows.GetArrayLength() > 0)
            {
                var firstRow = sliceRows[0];
                if (firstRow.ValueKind == JsonValueKind.Array && firstRow.GetArrayLength() > 0
                    && firstRow[0].ValueKind == JsonValueKind.Number)
                    return firstRow[0].GetInt64();
            }
        }

        return null;
    }
}
