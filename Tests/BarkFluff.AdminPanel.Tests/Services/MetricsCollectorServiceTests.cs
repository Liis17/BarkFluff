using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class MetricsCollectorServiceTests
{
    [Theory]
    [InlineData(0, 0L, 0L)]
    [InlineData(10_000, 2L, 1L)]
    [InlineData(10_001, 2L, 1L)]
    public async Task CollectSystemTrafficAsync_RebuildsWindowAndCountsEveryEvent(
        int eventCount,
        long expectedErrorCount,
        long expectedWarningCount)
    {
        var targetHour = TruncateToHour(DateTime.UtcNow).AddHours(-2);
        await using var fixture = await TrafficFixture.CreateAsync(new SeqTrafficHandler(targetHour, eventCount));

        await fixture.Collector.CollectSystemTrafficAsync(fixture.SeqService, CancellationToken.None);
        Assert.Equal(25, fixture.Cache.HourlyTraffic.Count());
        var traffic = Assert.IsType<HourlyTraffic>(fixture.Cache.HourlyTraffic.FindById(targetHour));
        Assert.Equal((long)eventCount, traffic.AllCount);
        Assert.Equal(expectedErrorCount, traffic.ErrorCount);
        Assert.Equal(expectedWarningCount, traffic.WarningCount);

        var stats = Assert.IsType<HourlyStats>(fixture.Cache.HourlyStats.FindById(targetHour));
        Assert.Equal((long)eventCount, stats.TotalEvents);
        Assert.Equal(expectedErrorCount, stats.ErrorCount);
        Assert.Equal(expectedWarningCount, stats.WarningCount);
        Assert.Equal((long)eventCount, stats.PerService.GetValueOrDefault("BarkFluff.Test"));
    }

    [Fact]
    public async Task CollectSystemTrafficAsync_KeepsCachedHourAfterPageFailureAndRetriesIt()
    {
        var targetHour = TruncateToHour(DateTime.UtcNow).AddHours(-2);
        await using var fixture = await TrafficFixture.CreateAsync(
            new SeqTrafficHandler(targetHour, 501, failFirstTargetReadAfterFirstPage: true),
            cache =>
            {
                cache.HourlyStats.Upsert(new HourlyStats { HourUtc = targetHour, TotalEvents = 777 });
                cache.HourlyTraffic.Upsert(new HourlyTraffic { HourUtc = targetHour, AllCount = 777 });
            });

        await fixture.Collector.CollectSystemTrafficAsync(fixture.SeqService, CancellationToken.None);
        Assert.Equal(777L, fixture.Cache.HourlyTraffic.FindById(targetHour)?.AllCount);
        Assert.Equal(777L, fixture.Cache.HourlyStats.FindById(targetHour)?.TotalEvents);

        await fixture.Collector.CollectSystemTrafficAsync(fixture.SeqService, CancellationToken.None);
        var traffic = Assert.IsType<HourlyTraffic>(fixture.Cache.HourlyTraffic.FindById(targetHour));
        Assert.Equal(501L, traffic.AllCount);
        Assert.Equal(2L, traffic.ErrorCount);
        Assert.Equal(1L, traffic.WarningCount);

        var stats = Assert.IsType<HourlyStats>(fixture.Cache.HourlyStats.FindById(targetHour));
        Assert.Equal(501L, stats.TotalEvents);
    }

    [Fact]
    public async Task TrafficEndpoint_EmptyCacheReadsMoreThanFiftyThousandEvents()
    {
        await using var fixture = await TrafficFixture.CreateAsync(new SeqTrafficHandler(fallbackEventCount: 50_001));

        using var traffic = await fixture.GetTrafficAsync();

        var total = traffic.RootElement.GetProperty("all").EnumerateArray()
            .Sum(point => point.GetProperty("count").GetInt64());
        Assert.Equal(50_001, total);
    }

    private static DateTime TruncateToHour(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    private sealed class TrafficFixture : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly WebApplication _app;
        private readonly ServiceProvider _serviceProvider;

        private TrafficFixture(
            string directory,
            WebApplication app,
            HttpClient client,
            MetricsCacheDbContext cache,
            SeqService seqService,
            MetricsCollectorService collector,
            ServiceProvider serviceProvider)
        {
            _directory = directory;
            _app = app;
            Client = client;
            Cache = cache;
            SeqService = seqService;
            Collector = collector;
            _serviceProvider = serviceProvider;
        }

        public HttpClient Client { get; }
        public MetricsCacheDbContext Cache { get; }
        public SeqService SeqService { get; }
        public MetricsCollectorService Collector { get; }

        public static async Task<TrafficFixture> CreateAsync(
            SeqTrafficHandler seqHandler,
            Action<MetricsCacheDbContext>? seedCache = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"adminpanel-traffic-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var cache = new MetricsCacheDbContext(new MetricsCacheSettings { Path = Path.Combine(directory, "metrics.db") });
            WebApplication? app = null;
            ServiceProvider? serviceProvider = null;

            try
            {
                seedCache?.Invoke(cache);
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseTestServer();
                builder.Services.AddSingleton(cache);
                builder.Services.AddSingleton(_ => new SeqService(
                    new HttpClient(seqHandler, disposeHandler: false),
                    Options.Create(new SeqSettings { ServerUrl = "http://seq" }),
                    NullLogger<SeqService>.Instance));
                builder.Services.AddSingleton<DockerService>(_ => null!);
                builder.Services.AddSingleton<DockerRegistryService>(_ => null!);

                app = builder.Build();
                app.MapSeqEndpoints();
                await app.StartAsync();

                serviceProvider = new ServiceCollection().BuildServiceProvider();
                var seqService = new SeqService(
                    new HttpClient(seqHandler, disposeHandler: false),
                    Options.Create(new SeqSettings { ServerUrl = "http://seq" }),
                    NullLogger<SeqService>.Instance);
                var collector = new MetricsCollectorService(
                    serviceProvider,
                    cache,
                    NullLogger<MetricsCollectorService>.Instance);

                return new TrafficFixture(directory, app, app.GetTestClient(), cache, seqService, collector, serviceProvider);
            }
            catch
            {
                if (app is null)
                    cache.Dispose();
                else
                    await app.DisposeAsync();
                if (serviceProvider is not null)
                    await serviceProvider.DisposeAsync();
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
                throw;
            }
        }

        public async Task<JsonDocument> GetTrafficAsync()
        {
            var response = await Client.GetAsync("/api/seq/dashboard/traffic?hours=24&interval=1h");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
            await _serviceProvider.DisposeAsync();
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class SeqTrafficHandler(
        DateTime? targetHour = null,
        int targetEventCount = 0,
        bool failFirstTargetReadAfterFirstPage = false,
        int fallbackEventCount = 0) : HttpMessageHandler
    {
        private int _targetReadCount;
        private bool _failedTargetPage;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = ParseQuery(request.RequestUri!.Query);
            var fromDateUtc = ParseDate(query, "fromDateUtc");
            var toDateUtc = ParseDate(query, "toDateUtc");
            var afterId = query.GetValueOrDefault("afterId");
            var pageSize = int.Parse(query["count"]);
            var isTargetHour = targetHour.HasValue && fromDateUtc.HasValue &&
                               TruncateToHour(fromDateUtc.Value) == targetHour && toDateUtc.HasValue;
            var isFallback = fallbackEventCount > 0 && fromDateUtc.HasValue && !toDateUtc.HasValue;

            var eventCount = isTargetHour ? targetEventCount : isFallback ? fallbackEventCount : 0;
            if (isTargetHour && afterId is null)
                _targetReadCount++;

            if (isTargetHour && failFirstTargetReadAfterFirstPage && _targetReadCount == 1 && afterId is not null && !_failedTargetPage)
            {
                _failedTargetPage = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("temporary Seq failure")
                });
            }

            var start = afterId is null ? 0 : int.Parse(afterId) + 1;
            var count = Math.Min(pageSize, Math.Max(0, eventCount - start));
            var timestamp = isFallback ? fromDateUtc!.Value.AddHours(1) : targetHour ?? DateTime.UtcNow;
            var events = Enumerable.Range(start, count).Select(id => new
            {
                Id = id.ToString(CultureInfo.InvariantCulture),
                Timestamp = timestamp.ToString("O", CultureInfo.InvariantCulture),
                Level = id switch { 0 => "Error", 1 => "Fatal", 2 => "Warning", _ => "Information" },
                Properties = new { Application = "BarkFluff.Test" }
            });
            var json = JsonSerializer.Serialize(events);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

        private static DateTime? ParseDate(IReadOnlyDictionary<string, string> query, string key) =>
            query.TryGetValue(key, out var value)
                ? DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : null;
    }
}
