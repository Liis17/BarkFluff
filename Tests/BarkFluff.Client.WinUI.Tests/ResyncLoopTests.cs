using System.Threading.Channels;
using BarkFluff.Client.Core.Infrastructure.Realtime;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class ResyncLoopTests
{
    [Fact]
    public async Task ImmediateAndDebouncedConfirmation_CoalesceWhileSnapshotIsRunning()
    {
        using var lifetime = new CancellationTokenSource();
        var delays = Channel.CreateUnbounded<DelayRequest>();
        var snapshots = Channel.CreateUnbounded<TaskCompletionSource<bool>>();
        var loop = new ResyncLoop(_ =>
        {
            var completion = new TaskCompletionSource<bool>();
            snapshots.Writer.TryWrite(completion);
            return completion.Task;
        }, lifetime.Token)
        {
            Delay = (duration, token) =>
            {
                var completion = new TaskCompletionSource();
                delays.Writer.TryWrite(new(duration, token, completion));
                return completion.Task;
            }
        };

        loop.RequestConfirmation();
        var superseded = await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        loop.RequestConfirmation();
        var confirmation = await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(superseded.Token.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromMilliseconds(750), confirmation.Duration);

        loop.RequestImmediate();
        var first = await snapshots.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        confirmation.Completion.SetResult();
        superseded.Completion.SetResult();
        loop.RequestImmediate();
        Assert.False(snapshots.Reader.TryRead(out _));

        first.SetResult(true);
        var followup = await snapshots.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        followup.SetResult(true);
        Assert.False(snapshots.Reader.TryRead(out _));
    }

    [Fact]
    public async Task FailedSnapshot_BacksOffAndWaitsForConnectivity()
    {
        using var lifetime = new CancellationTokenSource();
        var delays = Channel.CreateUnbounded<DelayRequest>();
        var snapshots = Channel.CreateUnbounded<TaskCompletionSource<bool>>();
        var loop = new ResyncLoop(_ =>
        {
            var completion = new TaskCompletionSource<bool>();
            snapshots.Writer.TryWrite(completion);
            return completion.Task;
        }, lifetime.Token)
        {
            Delay = (duration, token) =>
            {
                var completion = new TaskCompletionSource();
                delays.Writer.TryWrite(new(duration, token, completion));
                return completion.Task;
            }
        };

        loop.RequestImmediate();
        (await snapshots.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).SetResult(false);
        var firstDelay = await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(2), firstDelay.Duration);
        firstDelay.Completion.SetResult();
        (await snapshots.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).SetResult(false);
        var secondDelay = await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(4), secondDelay.Duration);

        loop.SetOnline(false);
        secondDelay.Completion.SetResult();
        Assert.False(snapshots.Reader.TryRead(out _));
        loop.SetOnline(true);
        (await snapshots.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).SetResult(true);
    }

    private sealed record DelayRequest(TimeSpan Duration, CancellationToken Token, TaskCompletionSource Completion);
}
