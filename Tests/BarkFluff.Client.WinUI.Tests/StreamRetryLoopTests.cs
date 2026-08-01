using BarkFluff.Client.Core.Infrastructure.Realtime;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class StreamRetryLoopTests
{
    /// <summary>Пауз нет: цикл заканчивается тем, что сам <c>connect</c> отменяет токен на нужной попытке.</summary>
    private static readonly Func<int, TimeSpan> NoDelay = _ => TimeSpan.Zero;

    [Fact]
    public async Task RunAsync_ReconnectsAfterStreamEnds()
    {
        using var cancellation = new CancellationTokenSource();
        var connects = 0;
        var received = new List<int>();

        await StreamRetryLoop.RunAsync<int>(
            _ =>
            {
                connects++;
                if (connects > 2)
                {
                    cancellation.Cancel();
                    return Task.FromResult<IAsyncEnumerable<int>?>(null);
                }

                return Task.FromResult<IAsyncEnumerable<int>?>(Stream(connects));
            },
            received.Add,
            onConnectedChanged: null,
            NoDelay,
            cancellation.Token);

        Assert.Equal(3, connects);
        Assert.Equal([1, 2], received);
    }

    [Fact]
    public async Task RunAsync_WhenConnectReturnsNull_KeepsRetrying()
    {
        using var cancellation = new CancellationTokenSource();
        var connects = 0;

        await StreamRetryLoop.RunAsync<int>(
            _ =>
            {
                connects++;
                if (connects == 3)
                {
                    cancellation.Cancel();
                }

                return Task.FromResult<IAsyncEnumerable<int>?>(null);
            },
            _ => { },
            onConnectedChanged: null,
            NoDelay,
            cancellation.Token);

        Assert.Equal(3, connects);
    }

    [Fact]
    public async Task RunAsync_WhenConnectThrows_KeepsRetrying()
    {
        using var cancellation = new CancellationTokenSource();
        var connects = 0;

        await StreamRetryLoop.RunAsync<int>(
            _ =>
            {
                connects++;
                if (connects == 3)
                {
                    cancellation.Cancel();
                }

                throw new InvalidOperationException("канал недоступен");
            },
            _ => { },
            onConnectedChanged: null,
            NoDelay,
            cancellation.Token);

        Assert.Equal(3, connects);
    }

    [Fact]
    public async Task RunAsync_ReportsConnectedThenDisconnected()
    {
        using var cancellation = new CancellationTokenSource();
        var connects = 0;
        var states = new List<bool>();

        await StreamRetryLoop.RunAsync<int>(
            _ =>
            {
                connects++;
                if (connects > 2)
                {
                    cancellation.Cancel();
                    return Task.FromResult<IAsyncEnumerable<int>?>(null);
                }

                return Task.FromResult<IAsyncEnumerable<int>?>(Stream());
            },
            _ => { },
            states.Add,
            NoDelay,
            cancellation.Token);

        Assert.Equal([true, false, true, false], states);
    }

    /// <summary>
    /// Остановка сервиса и перезапуск после обновления токена отменяют токен цикла.
    /// Считать это потерей связи нельзя: индикатор мигал бы каждые несколько минут.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnCancellation_DoesNotReportDisconnected()
    {
        using var cancellation = new CancellationTokenSource();
        var states = new List<bool>();

        await StreamRetryLoop.RunAsync<int>(
            _ => Task.FromResult<IAsyncEnumerable<int>?>(Stream(1)),
            _ => cancellation.Cancel(),
            states.Add,
            NoDelay,
            cancellation.Token);

        Assert.Equal([true], states);
    }

    /// <summary>
    /// На этом держится таймаут в 1 с у <c>StopCoreAsync</c> обоих сервисов: неотменяемая
    /// пауза заставила бы закрытие приложения и выход из аккаунта ждать до тридцати секунд.
    /// </summary>
    [Fact]
    public async Task RunAsync_CancelledDuringBackoff_CompletesPromptly()
    {
        using var cancellation = new CancellationTokenSource();
        var connected = new TaskCompletionSource();

        var loop = StreamRetryLoop.RunAsync<int>(
            _ =>
            {
                connected.TrySetResult();
                return Task.FromResult<IAsyncEnumerable<int>?>(null);
            },
            _ => { },
            onConnectedChanged: null,
            _ => TimeSpan.FromSeconds(30),
            cancellation.Token);

        await connected.Task;
        await cancellation.CancelAsync();

        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunAsync_GrowsAttemptNumberWhileConnectionIsUnstable()
    {
        using var cancellation = new CancellationTokenSource();
        var connects = 0;
        var attempts = new List<int>();

        await StreamRetryLoop.RunAsync<int>(
            _ =>
            {
                connects++;
                if (connects == 4)
                {
                    cancellation.Cancel();
                }

                return Task.FromResult<IAsyncEnumerable<int>?>(null);
            },
            _ => { },
            onConnectedChanged: null,
            attempt =>
            {
                attempts.Add(attempt);
                return TimeSpan.Zero;
            },
            cancellation.Token);

        Assert.Equal([1, 2, 3], attempts);
    }

    [Fact]
    public void NextAttempt_AfterStableConnection_StartsOver()
    {
        var lifetime = (long)StreamRetryLoop.StableConnection.TotalMilliseconds;

        Assert.Equal(1, StreamRetryLoop.NextAttempt(7, lifetime));
        Assert.Equal(8, StreamRetryLoop.NextAttempt(7, lifetime - 1));
    }

    [Fact]
    public void Backoff_DoublesAndStopsAtMaximum()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), StreamRetryLoop.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(4), StreamRetryLoop.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(8), StreamRetryLoop.Backoff(3));
        Assert.Equal(TimeSpan.FromSeconds(16), StreamRetryLoop.Backoff(4));
        Assert.Equal(TimeSpan.FromSeconds(30), StreamRetryLoop.Backoff(5));
        Assert.Equal(TimeSpan.FromSeconds(30), StreamRetryLoop.Backoff(50));
    }

    private static async IAsyncEnumerable<int> Stream(params int[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
