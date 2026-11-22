using BarkFluff.Client.Core.Services;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class StickerFrameCacheTests
{
    private static readonly Uri First = new("https://example.test/first.webp");

    [Fact]
    public async Task RepeatedDisplayReusesDecodedFrame()
    {
        var downloads = 0;
        var cache = new StickerFrameCache((_, _) =>
        {
            downloads++;
            return Task.FromResult(Array.Empty<byte>());
        }, Decode);
        cache.StartSession();

        var first = await cache.GetAsync(First, 24);
        Assert.NotNull(first);
        Assert.Same(first, await cache.GetAsync(First, 24));
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task ByteLimitEvictsLeastRecentlyUsedFrame()
    {
        var second = new Uri("https://example.test/second.webp");
        var third = new Uri("https://example.test/third.webp");
        var cache = new StickerFrameCache((_, _) => Task.FromResult(Array.Empty<byte>()),
            Decode, byteLimit: (24 * 24 + 32 * 32) * 4);
        cache.StartSession();

        var first = await cache.GetAsync(First, 24);
        var oldSecond = await cache.GetAsync(second, 32);
        Assert.Same(first, await cache.GetAsync(First, 24));
        await cache.GetAsync(third, 32);

        Assert.Same(first, await cache.GetAsync(First, 24));
        Assert.NotSame(oldSecond, await cache.GetAsync(second, 32));
        var oversized = await cache.GetAsync(First, 64);
        Assert.NotSame(oversized, await cache.GetAsync(First, 64));
        Assert.Same(first, await cache.GetAsync(First, 24));
    }

    [Fact]
    public async Task ParallelDisplaysShareDownloadAcrossSizesAndCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloads = 0;
        var cache = new StickerFrameCache((_, _) =>
        {
            Interlocked.Increment(ref downloads);
            started.TrySetResult();
            return release.Task;
        }, Decode);
        cache.StartSession();
        using var caller = new CancellationTokenSource();
        var canceled = cache.GetAsync(First, 24, caller.Token);
        await started.Task;
        var first = cache.GetAsync(First, 24);
        var second = cache.GetAsync(First, 24);
        var larger = cache.GetAsync(First, 64);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        release.SetResult([]);

        Assert.Same(await first, await second);
        Assert.Equal(64, (await larger)!.Width);
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task EndingSessionEvictsFramesAndCancelsInFlightLoad()
    {
        var pendingUri = new Uri("https://example.test/pending.webp");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloads = 0;
        var cache = new StickerFrameCache((uri, _) =>
        {
            if (uri == pendingUri && Interlocked.Increment(ref downloads) == 1)
            {
                started.SetResult();
                return release.Task;
            }
            return Task.FromResult(Array.Empty<byte>());
        }, Decode);
        cache.StartSession();
        var oldFrame = await cache.GetAsync(First, 24);
        var pending = cache.GetAsync(pendingUri, 24);
        await started.Task;

        cache.EndSession();
        Assert.Null(await cache.GetAsync(First, 24));
        cache.StartSession();
        var newFrame = await cache.GetAsync(pendingUri, 24);
        release.SetResult([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Same(newFrame, await cache.GetAsync(pendingUri, 24));
        Assert.NotSame(oldFrame, await cache.GetAsync(First, 24));
    }

    private static Task<StickerFrame?> Decode(byte[] bytes, int size, CancellationToken token) =>
        Task.FromResult<StickerFrame?>(new(size, size, new byte[size * size * 4]));
}
