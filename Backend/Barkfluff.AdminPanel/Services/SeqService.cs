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
