using BarkFluff.GrpcServer.Metrics;
using BarkFluff.Proto.Shared;
using BarkFluff.Proto.Updates;
using BarkFluff.Updates.Features.SubscribeNewMessages;
using BarkFluff.Updates.Features.SubscribeNewMessages.Handlers;

using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;

namespace BarkFluff.Updates.Tests.Features.SubscribeNewMessages;

public class NewMessageNotificationHandlerTests
{
    [Fact]
    public async Task Handle_SerializesWritesToTheSameUserStream()
    {
        var subscriptions = new StreamSubscriptionsManager();
        var stream = new BlockingStream();
        subscriptions.RegisterSubscription(1, stream);
        var handler = new NewMessageNotificationHandler(
            subscriptions,
            NullLogger<NewMessageNotificationHandler>.Instance,
            new MetricsCollector());

        var first = handler.Handle(Notification(1), CancellationToken.None);
        await stream.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var second = handler.Handle(Notification(2), CancellationToken.None);
        await Task.Delay(50);

        stream.ReleaseFirstWrite();
        await Task.WhenAll(first, second);

        stream.ConcurrentWriteDetected.Should().BeFalse();
        stream.Writes.Should().HaveCount(2);
    }

    private static NewMessageNotification Notification(long messageId)
        => new(new Message { Id = messageId }, [1], Guid.NewGuid());

    private sealed class BlockingStream : IServerStreamWriter<NewMessageEvent>
    {
        private readonly TaskCompletionSource _releaseFirstWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeWrites;
        private int _writeCount;

        public TaskCompletionSource FirstWriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<NewMessageEvent> Writes { get; } = [];

        public bool ConcurrentWriteDetected { get; private set; }

        public WriteOptions? WriteOptions { get; set; }

        public async Task WriteAsync(NewMessageEvent message)
        {
            if (Interlocked.Increment(ref _activeWrites) > 1)
                ConcurrentWriteDetected = true;

            var writeNumber = Interlocked.Increment(ref _writeCount);
            if (writeNumber == 1)
            {
                FirstWriteStarted.TrySetResult();
                await _releaseFirstWrite.Task;
            }

            Writes.Add(message);
            Interlocked.Decrement(ref _activeWrites);
        }

        public void ReleaseFirstWrite() => _releaseFirstWrite.TrySetResult();
    }
}
