using Barkfluff.AdminPanel.Data;
using Barkfluff.AdminPanel.Endpoints;
using Barkfluff.AdminPanel.Models;
using Barkfluff.AdminPanel.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Net;
using System.Text;
using System.Text.Json;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Endpoints;

public class SeqEndpointsLastUploadTests
{
    [Fact]
    public async Task LastUpload_ReturnsFreshSeqSnapshotInsteadOfStaleRollup()
    {
        var rollupCompletedAtUtc = DateTime.UtcNow;
        var observedAtUtc = rollupCompletedAtUtc.AddMinutes(-5);
        var seqResponse = $$"""
            [{
              "Id":"evt-2",
              "Timestamp":"{{observedAtUtc:O}}",
              "Properties":{
                "Application":"BarkFluff.Files",
                "Metrics":{
                  "SchemaVersion":2,
                  "ServiceName":"BarkFluff.Files",
                  "Counters":{},
                  "Gauges":{
                    "files_last_upload_total_ms":920,
                    "files_last_upload_hashing_ms":18,
                    "files_last_upload_processing_ms":4,
                    "files_last_upload_s3_ms":689
                  }
                }
              }
            }]
            """;

        var result = await GetLastUploadAsync(seqResponse, seedCache: cache =>
        {
            SeedCachedSnapshot(cache);
            cache.MetricRollupHours.Upsert(new MetricRollupHour
            {
                HourUtc = TruncateToHour(rollupCompletedAtUtc),
                CompletedAtUtc = rollupCompletedAtUtc
            });
        });
        var payload = result.Payload;

        Assert.Equal(920, payload.GetProperty("totalMs").GetInt64());
        Assert.Equal(14, payload.GetProperty("bufferingMs").GetInt64());
        Assert.Equal(18, payload.GetProperty("hashingMs").GetInt64());
        Assert.Equal(4, payload.GetProperty("processingMs").GetInt64());
        Assert.Equal(689, payload.GetProperty("s3Ms").GetInt64());
        Assert.Equal("seq", payload.GetProperty("source").GetString());
        Assert.Equal(observedAtUtc, payload.GetProperty("observedAtUtc").GetDateTime());
        Assert.True(GetFromDateUtc(result.SeqRequestUri) <= observedAtUtc);
    }

    [Fact]
    public async Task LastUpload_WhenSeqIsUnavailable_ReturnsCachedSnapshot()
    {
        var result = await GetLastUploadAsync(
            "{}",
            HttpStatusCode.ServiceUnavailable,
            cache => SeedCachedSnapshot(cache, DateTime.UtcNow.AddDays(-10)));
        var payload = result.Payload;

        Assert.Equal(100, payload.GetProperty("totalMs").GetInt64());
        Assert.Equal(14, payload.GetProperty("bufferingMs").GetInt64());
        Assert.Equal(20, payload.GetProperty("hashingMs").GetInt64());
        Assert.Equal(30, payload.GetProperty("processingMs").GetInt64());
        Assert.Equal(40, payload.GetProperty("s3Ms").GetInt64());
        Assert.Equal("cache", payload.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("observedAtUtc").ValueKind);
    }

    [Fact]
    public async Task LastUpload_WithoutSeqOrCachedData_ReturnsEmptySnapshot()
    {
        var payload = (await GetLastUploadAsync("{}", HttpStatusCode.ServiceUnavailable)).Payload;

        foreach (var property in new[]
                 {
                     "observedAtUtc", "totalMs", "bufferingMs", "hashingMs",
                     "processingMs", "s3Ms", "source"
                 })
        {
            Assert.Equal(JsonValueKind.Null, payload.GetProperty(property).ValueKind);
        }
    }

    private static async Task<LastUploadTestResult> GetLastUploadAsync(
        string seqResponse,
        HttpStatusCode seqStatus = HttpStatusCode.OK,
        Action<MetricsCacheDbContext>? seedCache = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"adminpanel-last-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var cache = new MetricsCacheDbContext(new MetricsCacheSettings
        {
            Path = Path.Combine(directory, "metrics.db")
        });
        WebApplication? app = null;

        try
        {
            seedCache?.Invoke(cache);

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(cache);
            var handler = new StaticJsonHandler(seqStatus, seqResponse);
            builder.Services.AddSingleton(_ => new SeqService(
                new HttpClient(handler),
                Options.Create(new SeqSettings { ServerUrl = "http://seq" }),
                NullLogger<SeqService>.Instance));
            builder.Services.AddSingleton<DockerService>(_ => null!);
            builder.Services.AddSingleton<DockerRegistryService>(_ => null!);

            app = builder.Build();
            app.MapSeqEndpoints();
            await app.StartAsync();
            using var client = app.GetTestClient();

            var response = await client.GetAsync("/api/seq/dashboard/files/last-upload");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return new LastUploadTestResult(document.RootElement.Clone(), handler.LastRequestUri!);
        }
        finally
        {
            if (app is null)
                cache.Dispose();
            else
                await app.DisposeAsync();

            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static void SeedCachedSnapshot(MetricsCacheDbContext cache, DateTime? hourUtc = null)
    {
        cache.HourlyServiceMetrics.Insert(new HourlyServiceMetrics
        {
            HourUtc = TruncateToHour(hourUtc ?? DateTime.UtcNow),
            ServiceName = "BarkFluff.Files",
            Gauges = new Dictionary<string, long>
            {
                ["files_last_upload_total_ms"] = 100,
                ["files_last_upload_buffering_ms"] = 14,
                ["files_last_upload_hashing_ms"] = 20,
                ["files_last_upload_processing_ms"] = 30,
                ["files_last_upload_s3_ms"] = 40
            },
            SchemaVersion = 2
        });
    }

    private static DateTime TruncateToHour(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    private static DateTime GetFromDateUtc(Uri requestUri)
    {
        var pair = requestUri.Query.TrimStart('?').Split('&')
            .Single(value => value.StartsWith("fromDateUtc=", StringComparison.Ordinal));
        return DateTime.Parse(
            Uri.UnescapeDataString(pair[(pair.IndexOf('=') + 1)..]),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);
    }

    private sealed record LastUploadTestResult(JsonElement Payload, Uri SeqRequestUri);

    private sealed class StaticJsonHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
