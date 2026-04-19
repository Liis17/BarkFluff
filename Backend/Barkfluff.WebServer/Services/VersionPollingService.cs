using System.Text.Json;

namespace Barkfluff.WebServer.Services;

public class VersionPollingService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private static readonly (string Url, Action<VersionStore, string> Setter)[] Endpoints =
    [
        ("https://storage.barkfluff.com/get/barkfluffkotlin/release/version",
            (s, v) => s.SetAndroidRelease(v)),
        ("https://storage.barkfluff.com/get/barkfluffkotlin/beta/version",
            (s, v) => s.SetAndroidBeta(v)),
        ("https://storage.barkfluff.com/get/barkfluffwindows/release/version",
            (s, v) => s.SetWindowsRelease(v)),
        ("https://storage.barkfluff.com/get/barkfluffwindows/beta/version",
            (s, v) => s.SetWindowsBeta(v)),
    ];

    private readonly IHttpClientFactory _httpFactory;
    private readonly VersionStore _store;
    private readonly ILogger<VersionPollingService> _logger;

    public VersionPollingService(
        IHttpClientFactory httpFactory,
        VersionStore store,
        ILogger<VersionPollingService> logger)
    {
        _httpFactory = httpFactory;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PollAllAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PollAllAsync(stoppingToken);
    }

    private async Task PollAllAsync(CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        foreach (var (url, setter) in Endpoints)
            await FetchVersionAsync(http, url, setter, ct);
    }

    private async Task FetchVersionAsync(
        HttpClient http,
        string url,
        Action<VersionStore, string> setter,
        CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var version = doc.RootElement.GetProperty("version").GetString();
            if (!string.IsNullOrEmpty(version))
            {
                setter(_store, version);
                _logger.LogDebug("Version fetched from {Url}: {Version}", url, version);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to fetch version from {Url}: {Message}", url, ex.Message);
        }
    }
}
