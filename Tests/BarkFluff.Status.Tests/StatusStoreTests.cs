using BarkFluff.Status.Probing;
using BarkFluff.Status.Storage;
using Xunit;

namespace BarkFluff.Status.Tests;

public sealed class StatusStoreTests : IDisposable
{
    private const string Endpoint = "https://messages.example.test";
    private const string Service = "messages";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "barkfluff-status-tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "status.db");

    public void Dispose()
    {
        SqliteFiles.TryDelete(_directory);
    }

    private static readonly long Day0 = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static ProbeRecord Record(long slot, ProbeState state, string endpoint = Endpoint, string service = Service) =>
        new(endpoint, service, slot, state, slot + 1, 12);

    [Fact]
    public void RepeatedBatchForSameSlotsDoesNotChangeCounters()
    {
        using var store = Open();
        var batch = new[] { Record(Day0, ProbeState.Up), Record(Day0 + 30, ProbeState.Down) };

        Assert.Equal(2, store.WriteBatch(batch));
        Assert.Equal(0, store.WriteBatch(batch));

        var counts = store.Days(Endpoint, Service, "2026-11-20")["2026-11-20"];
        Assert.Equal(1, counts.Up);
        Assert.Equal(1, counts.Down);
        Assert.Equal(2, counts.Observed);
    }

    [Fact]
    public void SameSlotForAlternateEndpointIsKeptSeparately()
    {
        using var store = Open();
        store.WriteBatch([Record(Day0, ProbeState.Up)]);
        store.WriteBatch([Record(Day0, ProbeState.Down, endpoint: "https://alt.example.org")]);

        Assert.Equal(1, store.Days(Endpoint, Service, "2026-11-20")["2026-11-20"].Up);
        Assert.Equal(1, store.Days("https://alt.example.org", Service, "2026-11-20")["2026-11-20"].Down);
        Assert.Equal(ProbeState.Up, store.Latest(Endpoint, Service)!.State);
        Assert.Equal(ProbeState.Down, store.Latest("https://alt.example.org", Service)!.State);
    }

    [Fact]
    public void LatestKeepsNewestSlotRegardlessOfWriteOrder()
    {
        using var store = Open();
        store.WriteBatch([Record(Day0 + 60, ProbeState.Degraded)]);
        store.WriteBatch([Record(Day0 + 30, ProbeState.Up)]);

        var latest = store.Latest(Endpoint, Service);
        Assert.NotNull(latest);
        Assert.Equal(Day0 + 60, latest.SlotUtc);
        Assert.Equal(ProbeState.Degraded, latest.State);
    }

    [Fact]
    public void DataSurvivesRestartAndStaysIdempotent()
    {
        using (var first = Open())
        {
            first.WriteBatch([Record(Day0, ProbeState.Up), Record(Day0 + 30, ProbeState.Starting)]);
        }

        using var second = Open();
        Assert.Equal(0, second.WriteBatch([Record(Day0, ProbeState.Up)]));
        var counts = second.Days(Endpoint, Service, "2026-11-20")["2026-11-20"];
        Assert.Equal(1, counts.Up);
        Assert.Equal(1, counts.Starting);
        Assert.Equal(Day0, second.FirstSlot(Endpoint, Service));
        Assert.Equal(ProbeState.Starting, second.Latest(Endpoint, Service)!.State);
    }

    [Fact]
    public void DayRolloverCreatesSeparateCounters()
    {
        using var store = Open();
        store.WriteBatch([Record(Day0 + 86_370, ProbeState.Up), Record(Day0 + 86_400, ProbeState.Down)]);

        var days = store.Days(Endpoint, Service, "2026-11-20");
        Assert.Equal(1, days["2026-11-20"].Up);
        Assert.Equal(1, days["2026-11-21"].Down);
    }

    [Fact]
    public void NoDataReturnsEmptyHistoryAndNoFirstSlot()
    {
        using var store = Open();

        Assert.Empty(store.Days(Endpoint, Service, "2026-01-01"));
        Assert.Null(store.FirstSlot(Endpoint, Service));
        Assert.Null(store.Latest(Endpoint, Service));
    }

    [Fact]
    public void PruneRemovesOnlyRowsBeforeCutoff()
    {
        using var store = Open();
        store.WriteBatch([Record(Day0 - 86_400, ProbeState.Up), Record(Day0, ProbeState.Up)]);

        store.Prune(Day0, "2026-11-20");

        Assert.Equal(Day0, store.FirstSlot(Endpoint, Service));
        Assert.Single(store.Days(Endpoint, Service, "2026-01-01"));
    }

    private StatusStore Open()
    {
        var store = new StatusStore(DatabasePath);
        store.Initialize();
        return store;
    }
}

internal static class SqliteFiles
{
    public static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Файл может держаться до завершения финализаторов; временные данные теста в каталоге temp.
        }
    }
}
