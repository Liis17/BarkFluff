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
    }

    public async Task<JsonElement?> GetEventsAsync(string? filter = null, int count = 50, DateTime? fromDateUtc = null)
    {
        var query = $"/api/events?count={count}";

        if (!string.IsNullOrEmpty(filter))
            query += $"&filter={Uri.EscapeDataString(filter)}";

        if (fromDateUtc.HasValue)
            query += $"&fromDateUtc={fromDateUtc.Value:O}";

        try
        {
            var response = await _httpClient.GetAsync(query);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch events from Seq");
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
